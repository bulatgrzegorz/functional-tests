using System.Diagnostics;
using OpenTelemetry;
using Tests.Fixtures;
using TUnit.Core.Interfaces;

namespace Tests.Stubs;

// Mocks for a third party the AppHost declares. ApiFixture points every external service's URL at WireMock, so a stub
// only says what every test gets (Initialize) and the setups tests call. TUnit creates it once per session and injects
// WireMock; expose it on ApiFixture with ClassDataSource.
public abstract class ExternalServiceStub : IAsyncInitializer
{
  [ClassDataSource<WireMockFixture>(Shared = SharedType.PerTestSession)]
  public required WireMockFixture WireMock { get; init; }

  // Mocks every test gets, set up once before the first test. Return Task.CompletedTask when there are none.
  protected abstract Task Initialize();

  public async Task InitializeAsync()
  {
    // No trace here, so Initialize's mocks match every test; and no setup traffic in the dashboard.
    Activity.Current = null;
    using (SuppressInstrumentationScope.Begin()) await Initialize();
  }

  protected Task DefineMock(WireMockFixture.MockedRequest request, WireMockFixture.MockedResponse response) =>
    WireMock.DefineMock(request, response);
}
