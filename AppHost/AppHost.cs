using Confluent.Kafka;
using Confluent.Kafka.Admin;

var builder = DistributedApplication.CreateBuilder(args);

// What the API owns: its database (schema in Database/orders.sql) and its topic.
var postgres = builder.AddPostgres("postgres").WithInitFiles("./Database");
var ordersDb = postgres.AddDatabase("ordersdb");

var kafka = builder.AddKafka("kafka")
    .OnResourceReady(async (kafka, _, ct) =>
    {
        var bootstrapServers = await kafka.ConnectionStringExpression.GetValueAsync(ct);
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = "orders-topic", NumPartitions = 1, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    });

var api = builder.AddDotnetProject("api", "../Api/Api.csproj")
    .WithReference(ordersDb, "Database").WaitFor(ordersDb)
    .WithEnvironment("Kafka__ConnectionString", kafka).WaitFor(kafka)
    .WithHttpHealthCheck("/health");

// What the API calls but doesn't own. In the real world it's just a URL (Parameters:some-service-url - set per
// environment, and always set by the functional tests to their fake). Without one, a local stand-in runs instead.
if (builder.Configuration["Parameters:some-service-url"] is not null)
{
    api.WithEnvironment("SomeService__BaseUrl", builder.AddExternalService("some-service", builder.AddParameter("some-service-url")));
}
else
{
    var standIn = builder.AddDotnetProject("some-service", "../SomeService/SomeService.csproj");
    api.WithEnvironment("SomeService__BaseUrl", standIn.GetEndpoint("http")).WaitFor(standIn);
}

builder.Build().Run();
