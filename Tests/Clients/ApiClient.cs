using System.Diagnostics;
using System.Net.Http.Json;

namespace Tests.Clients;

public class ApiClient(HttpClient client)
{
    public Task<HttpResponseMessage> PostOrder(string sku, int quantity) => client.PostAsJsonAsync("/api/orders", new { Sku = sku, Quantity = quantity });
}

// Every API response becomes an event on the test's span, so its body is in the trace however the test checks it.
// shortcut: reads the whole body, so a streamed response (SSE) would only be recorded at its end; skip those when the API has one.
public class ResponseRecorder : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Activity.Current?.AddEvent(new ActivityEvent($"{request.Method} {request.RequestUri?.PathAndQuery} {(int)response.StatusCode}", tags: new ActivityTagsCollection
        {
            ["http.response.status_code"] = (int)response.StatusCode,
            ["http.response.body"] = await response.Content.ReadAsStringAsync(cancellationToken),
        }));
        return response;
    }
}
