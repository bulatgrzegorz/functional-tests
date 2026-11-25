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

  [ClassDataSource<ExampleServiceStub>(Shared = SharedType.PerTestSession)]
  public required ExampleServiceStub Stub { get; init; }

  public ApiClient ApiClient() => new(_createHttpClient());
  public KafkaClient KafkaClient(string topic) => new(_kafkaBootstrapServers, topic);

  private static ApiHost Mode => TestsConfiguration.ApiHost;
  private IResource _api = null!;
  private LocalAppFixture? _inProcessApi;
  private Func<HttpClient> _createHttpClient = null!;
  private string _kafkaBootstrapServers = null!;

  protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(3); // first run pulls images and builds the API

  // In-process and container hosts replace the AppHost's project (TUnit also drops its children and edges to it).
  protected override IEnumerable<string> ResourcesToRemove() => Mode == ApiHost.Aspire ? [] : ["api"];

  protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
  {
    // 1. Every third party the AppHost declares resolves to WireMock, whatever the configuration says (e.g. a stage
    //    URL in user secrets). Parameters are read lazily, so setting them here is in time.
    var fake = Mode == ApiHost.Container ? Wiremock.UrlForContainers : Wiremock.Url;
    foreach (var external in builder.Resources.OfType<ExternalServiceResource>())
    {
      var parameter = external.UrlParameter
        ?? throw new InvalidOperationException($"External service '{external.Name}' has a fixed URL the tests can't fake; give it a URL parameter.");
      builder.Configuration[$"Parameters:{parameter.Name}"] = fake;
    }

    // 2. Where Aspire points the API's OTLP exporter (in-process and container hosts export straight there).
    builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = AspireDashboardFixture.OtlpGrpcEndpoint;

    // 3. Where the API runs.
    _api = builder.Resources.Single(r => r.Name == "api");
    if (Mode == ApiHost.Container) ApiHosts.AddContainer(builder, _api);

    // Health checks fail by design while containers boot; TUnit reports any resource that never becomes healthy.
    builder.Services.AddLogging(logging => logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None));
  }

  public override async Task InitializeAsync()
  {
    // Keep Aspire's own HTTP traffic (DCP, health checks, OTLP forwarding) out of the test traces.
    using (SuppressInstrumentationScope.Begin())
    {
      if (Mode == ApiHost.Container) await ApiHosts.PublishImageAsync();

      await base.InitializeAsync();

      _kafkaBootstrapServers = (await GetConnectionStringAsync("kafka"))!;

      if (Mode == ApiHost.InProcess) _inProcessApi = new LocalAppFixture(await ApiHosts.InProcessEnvironmentAsync(_api, App));

      _createHttpClient = Mode switch
      {
        ApiHost.InProcess => () => _inProcessApi!.CreateClient(),
        ApiHost.Container => () => CreateHttpClient(ApiHosts.ContainerName),
        _ => () => CreateHttpClient("api"),
      };
    }
  }

  public override async ValueTask DisposeAsync()
  {
    if (_inProcessApi is not null) await _inProcessApi.DisposeAsync();
    await base.DisposeAsync();
  }
}
