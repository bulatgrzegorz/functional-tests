using Tests.Fixtures;

namespace Tests.Stubs;

// Stands in for one third party the AppHost declares with AddExternalService: the session points the AppHost's URL
// parameter at WireMock, and the subclass sets up that service's responses. Register each one in ApiFixture.Stubs.
public abstract class ExternalServiceStub(WireMockFixture wireMock, string urlParameter)
{
  public string UrlParameter => urlParameter;

  // Containers on Aspire's network can't reach the test host's localhost.
  public string UrlFor(ApiHost host) => host == ApiHost.Container ? wireMock.UrlForContainers : wireMock.Url;

  protected Task DefineMock(WireMockFixture.MockedRequest request, WireMockFixture.MockedResponse response) =>
    wireMock.DefineMock(request, response);
}
