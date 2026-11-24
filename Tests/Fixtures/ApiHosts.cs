using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Fixtures;

// Re-hosts the AppHost's "api" resource without re-declaring its wiring: the AppHost stays the only place that says
// what the API connects to, these hosts only change where it runs.
public static class ApiHosts
{
  public const string ContainerName = "api-container";

  public static async Task<Dictionary<string, string>> InProcessEnvironmentAsync(IResource api, DistributedApplication app)
  {
    var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();
    var environment = new Dictionary<string, string>();
    foreach (var (key, value) in await WiringAsync(api, executionContext, CancellationToken.None))
    {
      environment[key] = value switch
      {
        string text => text,
        IValueProvider provider => await provider.GetValueAsync() ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
      };
    }

    // DCP fills these templates for the processes it launches; in-process there is no DCP.
    environment["OTEL_SERVICE_NAME"] = api.Name;
    environment.Remove("OTEL_RESOURCE_ATTRIBUTES");
    return environment;
  }

  public static void AddContainer(IDistributedApplicationTestingBuilder builder, IResource api)
  {
    var container = builder.AddContainer(ContainerName, "api", "latest") // image from PublishImageAsync
      .WithHttpEndpoint(targetPort: 8080) // SDK-built images listen on 8080
      .WithHttpHealthCheck("/health")
      .WithEnvironment(async context =>
      {
        // Values stay unresolved here, so Aspire resolves them for the container network (postgres:5432, ...)
        foreach (var (key, value) in await WiringAsync(api, context.ExecutionContext, context.CancellationToken))
        {
          // Kafka advertises a separate listener to containers; Aspire's Kafka connection string is the host one.
          context.EnvironmentVariables[key] = value is ConnectionStringReference { Resource: KafkaServerResource kafka }
            ? kafka.InternalEndpoint.Property(EndpointProperty.HostAndPort)
            : value;
        }
      });

    // Same dependencies as the project, except its build step: the image comes from PublishImageAsync (type is internal).
    foreach (var wait in api.Annotations.OfType<WaitAnnotation>().Where(w => w.Resource.GetType().Name != "DotnetProjectBuildResource"))
    {
      container.WithAnnotation(wait);
    }
  }

  public static async Task PublishImageAsync()
  {
    var projectPath = Path.Combine(CommonDirectoryPath.GetSolutionDirectory().DirectoryPath, "Api", "Api.csproj");
    var startInfo = new ProcessStartInfo("dotnet", $"publish {projectPath} -t:PublishContainer -c Release")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true
    };

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet publish");
    var output = process.StandardOutput.ReadToEndAsync(); // drain while waiting; MSBuild reports errors on stdout
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
      throw new Exception($"Failed to build container: {await output}{await process.StandardError.ReadToEndAsync()}");
    }
  }

  // The AppHost's environment callbacks for `api`, unresolved, minus values that point at the project's own endpoints
  // (ASPNETCORE_URLS, HTTP_PORTS, ...): those describe the host being replaced.
  private static async Task<Dictionary<string, object>> WiringAsync(IResource api, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
  {
    var context = new EnvironmentCallbackContext(executionContext, api, cancellationToken: cancellationToken);
    foreach (var callback in api.Annotations.OfType<EnvironmentCallbackAnnotation>())
    {
      await callback.Callback(context);
    }

    return context.EnvironmentVariables.Where(e => !References(e.Value, api)).ToDictionary();
  }

  private static bool References(object? value, IResource resource) => value switch
  {
    IResource referenced => referenced == resource,
    IValueWithReferences withReferences => withReferences.References.Any(r => References(r, resource)),
    _ => false,
  };
}
