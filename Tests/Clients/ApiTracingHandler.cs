using System.Diagnostics;
using System.Net.Http.Headers;
using Tests.Fixtures;

namespace Tests.Clients;

// Every call a test makes to the API, as a client span in the test's trace, with the request and response bodies
// (TraceBodies). It also sends the trace context (traceparent) the API's spans, WireMock stubs and Kafka messages are
// matched by, so every HttpClient for the API goes through it.
// shortcut: reads whole bodies, so a streamed response (SSE) is traced only when it ends; skip those when the API has one.
public class ApiTracingHandler : DelegatingHandler
{
  public static readonly ActivitySource Source = new("Tests.Api");

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    using var activity = Source.StartActivity($"{request.Method} {request.RequestUri?.AbsolutePath}", ActivityKind.Client);
    DistributedContextPropagator.Current.Inject(Activity.Current, request.Headers, (carrier, key, value) =>
    {
      if (carrier is HttpRequestHeaders headers) headers.TryAddWithoutValidation(key, value);
    });

    var traceBodies = activity is not null && TestsConfiguration.TraceBodies;
    activity?.SetTag("http.request.method", request.Method.Method);
    activity?.SetTag("url.full", request.RequestUri?.ToString());
    if (traceBodies && request.Content is not null) activity!.SetTag("http.request.body", await request.Content.ReadAsStringAsync(cancellationToken));

    var response = await base.SendAsync(request, cancellationToken);

    activity?.SetTag("http.response.status_code", (int)response.StatusCode);
    if (traceBodies) activity!.SetTag("http.response.body", await response.Content.ReadAsStringAsync(cancellationToken));
    return response;
  }
}
