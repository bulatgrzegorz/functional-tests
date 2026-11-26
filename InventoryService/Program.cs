using OpenTelemetry;
using OpenTelemetry.Trace;

// Stand-in for the third-party inventory service the API calls, so `aspire run` works end to end on a laptop.
// The tests never run it: they point the API at a WireMock fake they control per test.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .UseOtlpExporter(); // endpoint and service name come from OTEL_* set by Aspire

var app = builder.Build();

app.MapGet("/inventory/{sku}", (string sku) => new
{
    Inventory = sku switch
    {
        "SKU-001" => 100, 
        "SKU-002" => 0, 
        _ => 5
    }
});

app.Run();
