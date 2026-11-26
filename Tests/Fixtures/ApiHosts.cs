using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Fixtures;

// InProcess mode: the AppHost's "api" project runs inside the test process (LocalAppFixture, a WebApplicationFactory)
// with the configuration Aspire would have given it, so the AppHost stays the only place that says what the API
// connects to. Container mode doesn't come here: it is the AppHost's own --ApiContainer mode.
//
// ApiFixture keeps the "api" resource object, then has TUnit remove it from the model, so Aspire never starts it.
// The object still carries its environment callbacks: the AppHost's (WithReference(ordersDb, "Database"),
// Kafka__ConnectionString, InventoryService__BaseUrl) and the ones Aspire adds to every project (OTEL_*, the project's
// own URLs and ports). Running them yields its settings.
public static class ApiHosts
{
  // The API's settings as text, with addresses reachable from this machine. LocalAppFixture sets them as environment
  // variables, where `__` reads as `:`, so Program.cs finds them as usual (ConnectionStrings:Database,
  // Kafka:ConnectionString, InventoryService:BaseUrl): same Postgres, Kafka and WireMock as the rest of the run.
  public static async Task<Dictionary<string, string>> InProcessEnvironmentAsync(IResource api, DistributedApplication app)
  {
    var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();
    var environment = new Dictionary<string, string>();
    foreach (var (key, value) in await WiringAsync(api, executionContext, CancellationToken.None))
    {
      // The app is running now, so a resource or parameter can give its current value: ordersdb its connection string
      // with the port Aspire picked, inventory-service-url the WireMock URL ApiFixture set.
      environment[key] = value switch
      {
        string text => text,
        IValueProvider provider => await provider.GetValueAsync() ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
      };
    }

    // Both are templates that DCP (Aspire's process launcher) fills in when it starts a process. Nothing launches this
    // one: without the name, the template text itself would become the service name in traces.
    environment["OTEL_SERVICE_NAME"] = api.Name;
    environment.Remove("OTEL_RESOURCE_ATTRIBUTES");
    return environment;
  }

  // Runs every environment callback on `api` and collects what they set, still as objects (a resource, a parameter, an
  // expression), not text. Drops the values that point back at `api` itself (ASPNETCORE_URLS, HTTP_PORTS): they are the
  // port a separately launched API would listen on, and WebApplicationFactory serves it in memory instead.
  private static async Task<Dictionary<string, object>> WiringAsync(IResource api, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
  {
    var context = new EnvironmentCallbackContext(executionContext, api, cancellationToken: cancellationToken);
    foreach (var callback in api.Annotations.OfType<EnvironmentCallbackAnnotation>())
    {
      await callback.Callback(context);
    }

    return context.EnvironmentVariables.Where(e => !References(e.Value, api)).ToDictionary();
  }

  // Does the value point at the resource, either directly or through any part of an expression or endpoint built from it?
  private static bool References(object? value, IResource resource) => value switch
  {
    IResource referenced => referenced == resource,
    IValueWithReferences withReferences => withReferences.References.Any(r => References(r, resource)),
    _ => false,
  };
}
