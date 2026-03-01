using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.Fixtures;

public class AspireDashboardFixture : FixtureBase
{
    [ClassDataSource<DockerNetworkFixture>(Shared = SharedType.PerTestSession)]
    public required DockerNetworkFixture DockerNetwork { get; init; }
    
    private const int DashboardPort = 18888;
    private const int OtlpGrpcPort = 18889;
    private const string AliasName = "aspire_dashboard";
    private IContainer _container = null!;
    
    public string OtlpEndpointForContainer => $"http://{AliasName}:{OtlpGrpcPort}";
    public string OtlpEndpointForHost => $"http://localhost:{_container.GetMappedPublicPort(OtlpGrpcPort)}";
    public string ConnectionString => TestsConfiguration.LocalMode ? OtlpEndpointForHost : OtlpEndpointForContainer; 

    protected override async Task Initialize()
    {
        var builder = new ContainerBuilder("mcr.microsoft.com/dotnet/aspire-dashboard:latest")
            .WithNetworkAliases(AliasName)
            .WithNetwork(DockerNetwork.Network)
            .WithPortBinding(DashboardPort, true)
            .WithPortBinding(OtlpGrpcPort, true)
            .WithEnvironment("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");

        if (TestsConfiguration.LocalMode)
        {
            builder = builder.WithCleanUp(false).WithReuse(true);
        }
            
        _container = builder.Build();
        
        await _container.StartAsync();
        
        GlobalHooks.SetupOtlpExporter(OtlpEndpointForHost);
    }

    public override async ValueTask DisposeAsync()
    {
        if(TestsConfiguration.LocalMode) return; // if local mode, we reuse aspire dashboard, so no disposal

        try
        {
            await _container.DisposeAsync();
        }
        catch
        {
            // ignored
        }
    }
}