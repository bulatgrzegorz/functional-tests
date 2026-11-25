using System.Net;
using Tests.Fixtures;

namespace Tests.Stubs;

public class ExampleServiceStub(WireMockFixture wireMock) : ExternalServiceStub(wireMock, "some-service-url")
{
    public Task SetupInventory(string sku, InventoryResponse response)
    {
        return DefineMock(new WireMockFixture.MockedRequest()
            {
                Method = WireMockFixture.Method.Get,
                Path = $"/inventory/{sku}",
            },
            new WireMockFixture.MockedResponse()
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = response
            });
    }
}
