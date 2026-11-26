using System.Net;
using test_id_generator;

namespace Tests;

public class OrderTests
{
  [ClassDataSource<ApiFixture>(Shared = SharedType.PerTestSession)]
  public required ApiFixture App {get; init;}

  [Test]
  public async Task Should_RejectOrder_When_InventoryIsInsufficient()
  {
    var client = App.ApiClient();

    var sku = IdGenerator.GetStringId();
    await App.InventoryStub.SetupInventory(sku, new InventoryResponse(50));
    var response = await client.PostOrder(sku, 100);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
  }

  [Test]
  public async Task Should_PublishKafkaMessage_When_OrderIsCreated()
  {
    var client = App.ApiClient();
    using var kafka = App.KafkaClient("orders-topic");

    var sku = IdGenerator.GetStringId();
    await App.InventoryStub.SetupInventory(sku, new InventoryResponse(100));

    var response = await client.PostOrder(sku, 25);
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

    var hasMessage = kafka.ConsumeForCurrentTrace(TimeSpan.FromSeconds(10), out var receivedMessage);

    await Assert.That(hasMessage).IsTrue();
    Assert.NotNull(receivedMessage);
    await Assert.That(receivedMessage.Sku).IsEqualTo(sku);
    await Assert.That(receivedMessage.Quantity).IsEqualTo(25);
  }
}