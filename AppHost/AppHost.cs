using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;

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

// What the API calls but doesn't own: just a URL, set per environment (Parameters:some-service-url - a test or stage
// environment in a real API). The functional tests point it at WireMock.
var someService = builder.AddExternalService("some-service", builder.AddParameter("some-service-url"));

// Opt-in, docker-compose style: `aspire run -- --ApiContainer true` runs the API from its published image instead of as
// a local process. The default stays a project; the functional tests re-host that one.
if (builder.Configuration.GetValue<bool>("ApiContainer"))
{
    Wire(
        ApiContainer()
            .WithHttpEndpoint(port: 5160, targetPort: 8080) // SDK images listen on 8080; 5160 as in launchSettings (CreateOrder.http)
            .WithOtlpExporter()
            // Kafka advertises a separate listener to containers; its connection string is the host one.
            .WithEnvironment("Kafka__ConnectionString", ReferenceExpression.Create($"{kafka.Resource.InternalEndpoint.Property(EndpointProperty.HostAndPort)}"))
    );

    // Inside the container localhost is the container itself; this machine is behind the Docker host alias.
    builder.Configuration["Parameters:some-service-url"] = builder.Configuration["Parameters:some-service-url"]?.Replace("//localhost:", "//host.docker.internal:");
}
else
{
    Wire(
        builder
            .AddDotnetProject("api", "../Api/Api.csproj")
            .WithEnvironment("Kafka__ConnectionString", kafka)
        );
}

// Example only. A real API's third party lives outside its solution (think youtube.com); this repo ships a small one
// so `aspire run` has something to call. appsettings.json points some-service-url at it (port from its launchSettings).
builder.AddDotnetProject("some-service-example", "../SomeService/SomeService.csproj");

builder.Build().Run();

return;

// The API as a container, from an image built on every run. Here the .NET SDK builds it, no Dockerfile needed.
// With a Dockerfile in Api/, Aspire builds and runs it itself instead: return builder.AddDockerfile("api", "../Api");
IResourceBuilder<ContainerResource> ApiContainer()
{
    var publish = builder.AddExecutable("api-image", "dotnet", "../Api", "publish", "-t:PublishContainer", "-c", "Release");
    return builder.AddContainer("api", "api", "latest").WaitForCompletion(publish); // image name the SDK derives from Api.csproj
}

// What the API connects to, wherever it runs.
void Wire<T>(IResourceBuilder<T> api) where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
{
    api
        .WithReference(ordersDb, "Database").WaitFor(ordersDb)
        .WaitFor(kafka)
        .WithEnvironment("SomeService__BaseUrl", someService)
        .WithHttpHealthCheck("/health");
}
