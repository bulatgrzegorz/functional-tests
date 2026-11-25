using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;

namespace Tests.Fixtures;

// Aspire: the API is launched by Aspire exactly like `aspire run` (default).
// InProcess: WebApplicationFactory inside the test process - breakpoints, faster inner loop.
// Container: the published image as its own container on Aspire's network, like k8s (CI: TESTS_API_HOST=Container).
public enum ApiHost { Aspire, InProcess, Container }

public static class TestsConfiguration
{
    // Read from the project folder, not the output: the Api's appsettings.json is copied into the output too.
    private static readonly IConfiguration Settings = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(CommonDirectoryPath.GetProjectDirectory().DirectoryPath, "appsettings.json"), optional: true)
        .Build();

    // appsettings.json for the IDE; TESTS_API_HOST wins over it for CI and the command line.
    public static ApiHost ApiHost { get; } = Enum.Parse<ApiHost>(
        Environment.GetEnvironmentVariable("TESTS_API_HOST") ?? Settings["ApiHost"] ?? nameof(ApiHost.Aspire), ignoreCase: true);
}
