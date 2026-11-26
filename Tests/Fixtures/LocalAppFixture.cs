using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Tests.Clients;

namespace Tests.Fixtures;

// Not TUnit's TestWebApplicationFactory: in TUnit 1.83 its IHttpClientFactory filter hangs (CPU spin in CreateHandlerEntry)
// when parallel tests make their first API call at the same time
#pragma warning disable TUnit0064
public class LocalAppFixture(Dictionary<string, string> variables) : WebApplicationFactory<Program>
#pragma warning restore TUnit0064
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        foreach (var variable in variables) Environment.SetEnvironmentVariable(variable.Key, variable.Value);

        return base.CreateHost(builder);
    }

    // The in-memory server has no HTTP span or traceparent of its own; ApiTracingHandler adds both.
    public new HttpClient CreateClient() => CreateDefaultClient(new ApiTracingHandler());
}
