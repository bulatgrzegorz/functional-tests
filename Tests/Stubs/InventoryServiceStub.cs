using System.Net;
using Tests.Fixtures;

namespace Tests.Stubs;

public class InventoryServiceStub : ExternalServiceStub
{
    protected override Task Initialize() => Task.CompletedTask;

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
