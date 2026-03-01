using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.Fixtures;

public class ContainerAppFixture(Dictionary<string, string> variables) : IAsyncDisposable
{
    private IContainer _container = null!;

    public async Task InitializeAsync(DockerNetworkFixture network)
    {
        var rootDirectory = CommonDirectoryPath.GetSolutionDirectory();
        var projectPath = Path.Combine(rootDirectory.DirectoryPath, "Api", "Api.csproj");
        
        var startInfo = new ProcessStartInfo("dotnet", $"publish {projectPath} -t:PublishContainer -c Release")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        using var process = Process.Start(startInfo);
        if (process is not null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Failed to build container: {error}");
            }
        }
        
        var builder = new ContainerBuilder("api:latest")
            .WithNetwork(network.Network)
            .WithPortBinding(8080, true)
            .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
            .WithEnvironment(variables);
        //TODO: health check

        _container = builder.Build();
        await _container.StartAsync();
    }

    public HttpClient CreateClient() 
    {
        return new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}")
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}