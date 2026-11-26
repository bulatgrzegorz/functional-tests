using System.Net.Http.Json;

namespace Tests.Clients;

public class ApiClient(HttpClient client)
{
    public Task<HttpResponseMessage> PostOrder(string sku, int quantity) => client.PostAsJsonAsync("/api/orders", new { Sku = sku, Quantity = quantity });
}