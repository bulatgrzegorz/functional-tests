using System.Text.Json;
using Npgsql;
using Confluent.Kafka;
using Confluent.Kafka.Extensions.Diagnostics;
using Confluent.Kafka.Extensions.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add OpenTelemetry - endpoint, protocol and service name come from OTEL_* (set by Aspire, or by the tests)
var openTelemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddConfluentKafkaInstrumentation();
        tracing.AddNpgsql();
    })
    .WithLogging();

if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT")))
{
    openTelemetry.UseOtlpExporter();
}

builder.Services.AddHealthChecks();

// Postgres
var dbConnectionString = builder.Configuration.GetConnectionString("Database");
builder.Services.AddTransient<NpgsqlConnection>(_ => new NpgsqlConnection(dbConnectionString));

// Kafka
var kafkaConnectionString = builder.Configuration.GetValue<string>("Kafka:ConnectionString");
var producerConfig = new ProducerConfig { BootstrapServers = kafkaConnectionString };
builder.Services.AddSingleton(new ProducerBuilder<Null, OrderCreated>(producerConfig).SetValueSerializer(Json<OrderCreated>.Create()).BuildWithInstrumentation());

// Configure HttpClient for InventoryService
var inventoryServiceUrl = builder.Configuration.GetValue<string>("InventoryService:BaseUrl")!;
builder.Services.AddHttpClient("InventoryService", client => { client.BaseAddress = new Uri(inventoryServiceUrl); });

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetService<TracerProvider>()?.Dispose();
});

app.MapHealthChecks("/health");

app.MapPost("/api/orders", async (
    [FromBody] CreateOrderRequest request,
    [FromServices] NpgsqlConnection db,
    [FromServices] IProducer<Null, OrderCreated> kafka,
    [FromServices] IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("InventoryService");
    var response = await client.GetFromJsonAsync<InventoryResponse>($"/inventory/{request.Sku}");
    if (response is null || response.Inventory < request.Quantity)
    {
        return Results.UnprocessableEntity(new { Error = "Insufficient inventory" });
    }

    await db.OpenAsync();
    await using var cmd = new NpgsqlCommand("INSERT INTO orders (sku, quantity) VALUES (@sku, @qty)", db);
    cmd.Parameters.AddWithValue("sku", request.Sku);
    cmd.Parameters.AddWithValue("qty", request.Quantity);
    await cmd.ExecuteNonQueryAsync();
    
    await kafka.ProduceAsync("orders-topic", new Message<Null, OrderCreated>(){Value = new OrderCreated(request.Sku, request.Quantity)});

    return Results.Ok(new CreateOrderResponse("Order created"));
});

app.Run();

public record CreateOrderRequest(string Sku, int Quantity);
public record CreateOrderResponse(string Message);
public record InventoryResponse(int Inventory);
public record OrderCreated(string Sku, int Quantity);

public class Json<T> : ISerializer<T>, IDeserializer<T>
{
    public static Json<T> Create() => new();
    public byte[] Serialize(T data, SerializationContext context) => JsonSerializer.SerializeToUtf8Bytes(data);
    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) => JsonSerializer.Deserialize<T>(data)!;
}

public partial class Program { }
