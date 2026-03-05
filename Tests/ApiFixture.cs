using OpenTelemetry;
using Tests.Clients;
using Tests.Fixtures;
using Tests.Stubs;
using TUnit.Core.Interfaces;

namespace Tests;

public class ApiFixture : IAsyncInitializer, IAsyncDisposable
{
  public ApiClient ApiClient() => new(_httpClientFactory());
  public KafkaClient KafkaClient(string topic) => new(Kafka.BootstrapAddress, topic);

  public ExampleServiceStub Stub { get; private set; } = null!;

  private IAsyncDisposable? _app;

  private Func<HttpClient> _httpClientFactory = null!;
  
  [ClassDataSource<DockerNetworkFixture>(Shared = SharedType.PerTestSession)]
  public required DockerNetworkFixture DockerNetwork { get; init; }
  
  [ClassDataSource<AspireDashboardFixture>(Shared = SharedType.PerTestSession)]
  public required AspireDashboardFixture AspireDashboard { get; init; }
  
  [ClassDataSource<PostgresqlFixture>(Shared = SharedType.PerTestSession)]
  public required PostgresqlFixture Postgresql { get; init; }
  
  [ClassDataSource<KafkaFixture>(Shared = SharedType.PerTestSession)]
  public required KafkaFixture Kafka { get; init; }
  
  [ClassDataSource<WireMockFixture>(Shared = SharedType.PerTestSession)]
  public required WireMockFixture Wiremock { get; init; }

  public IDisposable InstrumentationSuppress = SuppressInstrumentationScope.Begin();

  public async Task InitializeAsync()
  {
    var variables = new Dictionary<string, string>()
    {
      ["ConnectionStrings__Database"] = Postgresql.ConnectionString,
      ["Kafka__ConnectionString"] = Kafka.ConnectionString,
      ["SomeService__BaseUrl"] = Wiremock.Url
    };

    if(TestsConfiguration.UseAspire){
      variables["OTEL_EXPORTER_OTLP_ENDPOINT"] = AspireDashboard.ConnectionString;
      variables["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc";
    }

    if(TestsConfiguration.LocalMode)
    {
      var app = new LocalAppFixture(variables);
      
      _httpClientFactory = () => app.CreateClient();

      _app = app;
    }
    else
    {
      var app = new ContainerAppFixture(variables);
      await app.InitializeAsync(DockerNetwork);

      _httpClientFactory = () => app.CreateClient();

      _app = app;
    }

    Stub = new ExampleServiceStub(Wiremock);
    
    InstrumentationSuppress.Dispose();
  }

  public async ValueTask DisposeAsync()
  {
    if (_app != null) await _app.DisposeAsync();
  }
}