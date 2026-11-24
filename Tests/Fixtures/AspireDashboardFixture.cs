using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.Fixtures;

// Standalone dashboard, reused across runs so traces accumulate (Aspire's own test dashboard dies with the run).
// Fixed ports: telemetry endpoints are read before test discovery, long before this container starts.
public class AspireDashboardFixture : FixtureBase
{
    public const string Url = "http://localhost:18888";
    public const string OtlpGrpcEndpoint = "http://localhost:18889";
    public const string OtlpHttpEndpoint = "http://localhost:18890";
    private IContainer _container = null!;

    protected override async Task Initialize()
    {
        // Same version as the AppHost's Aspire (the old dotnet/aspire-dashboard repo stops at 13.6)
        _container = new ContainerBuilder("mcr.microsoft.com/aspire/dashboard:17.0")
            .WithPortBinding(18888, 18888)
            .WithPortBinding(18889, 18889)
            .WithPortBinding(18890, 18890)
            .WithEnvironment("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true")
            .WithCleanUp(false)
            .WithReuse(true)
            .Build();

        await _container.StartAsync();
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask; // reused across runs
}
