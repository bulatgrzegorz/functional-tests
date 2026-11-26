extern alias apphost;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Tests.Clients;
using Tests.Fixtures;
using Tests.Stubs;
using TUnit.Aspire;

namespace Tests;

// Test overlay on the AppHost: the topology, schema, topic and wiring all come from AppHost.cs.
// This class only says what differs in a test - fakes for third parties, where telemetry goes, where the API runs.
public class ApiFixture : AspireFixture<apphost::Projects.AppHost>
{
  [ClassDataSource<AspireDashboardFixture>(Shared = SharedType.PerTestSession)]
  public required AspireDashboardFixture AspireDashboard { get; init; }

  [ClassDataSource<WireMockFixture>(Shared = SharedType.PerTestSession)]
  public required WireMockFixture Wiremock { get; init; }

  [ClassDataSource<InventoryServiceStub>(Shared = SharedType.PerTestSession)]
  public required InventoryServiceStub InventoryStub { get; init; }

  public ApiClient ApiClient() => new(_createHttpClient());
  public KafkaClient KafkaClient(string topic) => new(_kafkaBootstrapServers, topic);

  public static ApiHost Mode => TestsConfiguration.ApiHost;
  private IResource _api = null!;
  private LocalAppFixture? _inProcessApi;
  private Func<HttpClient> _createHttpClient = null!;
  private string _kafkaBootstrapServers = null!;

  protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(3); // first run pulls images and builds the API

  // Container: the AppHost's own container mode (it builds the image and runs it). An arg, because the AppHost reads it
  // while building the model, before ConfigureBuilder.
  protected override string[] Args => Mode == ApiHost.Container ? ["--ApiContainer=true"] : [];

  // api-image is a one-shot build that exits when done (TUnit counts that as failed); api waits for it, so that covers it.
  protected override bool ShouldWaitForResource(IResource resource) => resource.Name != "api-image" && base.ShouldWaitForResource(resource);

  // The in-process host replaces the AppHost's project (TUnit also drops its children and edges to it).
  protected override IEnumerable<string> ResourcesToRemove() => Mode == ApiHost.InProcess ? ["api"] : [];

  protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
  {
    ConfigureExternalServicesAddresses(builder);
    
    builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = AspireDashboardFixture.OtlpGrpcEndpoint;
    
    _api = builder.Resources.Single(r => r.Name == "api");

    // Health checks fail by design while containers boot; TUnit reports any resource that never becomes healthy.
    builder.Services.AddLogging(logging => logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None));
  }

  public override async Task InitializeAsync()
  {
    // Keep Aspire's own HTTP traffic (DCP, health checks, OTLP forwarding) out of the test traces.
    using (SuppressInstrumentationScope.Begin())
    {
      await base.InitializeAsync();

      _kafkaBootstrapServers = (await GetConnectionStringAsync("kafka"))!;

      if (Mode == ApiHost.InProcess) _inProcessApi = new LocalAppFixture(await ApiHosts.InProcessEnvironmentAsync(_api, App));

      _createHttpClient = Mode == ApiHost.InProcess ? () => _inProcessApi!.CreateClient() : () => CreateHttpClient("api");
    }
  }

  public override async ValueTask DisposeAsync()
  {
    if (_inProcessApi is not null) await _inProcessApi.DisposeAsync();
    await base.DisposeAsync();
  }
  
  private void ConfigureExternalServicesAddresses(IDistributedApplicationTestingBuilder builder)
  {
    foreach (var external in builder.Resources.OfType<ExternalServiceResource>())
    {
      var parameter = external.UrlParameter ?? throw new InvalidOperationException($"External service '{external.Name}' has a fixed URL the tests can't fake; give it a URL parameter.");
      builder.Configuration[$"Parameters:{parameter.Name}"] = Wiremock.UrlForApi;
    }
  }
}
