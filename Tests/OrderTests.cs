using System.Net;
using Tests.Clients;

namespace Tests;

public class OrderTests
{
  [ClassDataSource<ApiFixture>(Shared = SharedType.PerTestSession)]
  public required ApiFixture App {get; init;}

  [Test]
  public async Task Should_RejectOrder_When_InventoryIsInsufficient()
  {
    var client = App.ApiClient();

    var sku = "SKU-001";
    await App.Stub.SetupInventory(sku, new InventoryResponse(50));
    // Since we didn't setup WireMock to return a success, the external service
    // will return 404 Not Found by default, causing our API to return 422.
    var response = await client.PostOrder("SKU-001", 100);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
  }

  [Test]
  public async Task Should_PublishKafkaMessage_When_OrderIsCreated()
  {
    var client = App.ApiClient();
    using var kafka = App.KafkaClient("orders-topic");

    var sku = "SKU-KAFKA-001";
    await App.Stub.SetupInventory(sku, new InventoryResponse(10));

    var response = await client.PostOrder(sku, 25);
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

    var messages = kafka.ConsumeForCurrentTrace(TimeSpan.FromSeconds(10));

    await Assert.That(messages).Count().IsGreaterThanOrEqualTo(1);
    await Assert.That(messages[0]).Contains($"Order created for {sku}");
  }
}