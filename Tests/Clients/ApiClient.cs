using System.Net;
using System.Net.Http.Json;

namespace Tests.Clients;

public class ApiClient(HttpClient client)
{
    public Task<HttpResponseMessage> PostOrder(string sku, int quantity) => client.PostAsJsonAsync("/api/orders", new { Sku = sku, Quantity = quantity });
}

public static class ApiResponseAssertions
{
    extension(HttpResponseMessage response)
    {
        // The status alone rarely says why a call failed; the body (an error message, a stack trace) usually does.
        public async Task ShouldHaveStatus(HttpStatusCode expected) =>
            await Assert.That(response.StatusCode).IsEqualTo(expected)
                .Because($"the response body was: {await response.Content.ReadAsStringAsync()}");
    }
}
