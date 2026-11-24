namespace Tests.Fixtures;

// Aspire: the API is launched by Aspire exactly like `aspire run` (default).
// InProcess: WebApplicationFactory inside the test process - breakpoints, faster inner loop.
// Container: the published image as its own container on Aspire's network, like k8s (CI: TESTS_API_HOST=Container).
public enum ApiHost { Aspire, InProcess, Container }

public static class TestsConfiguration
{
    public static ApiHost ApiHost { get; } =
        Enum.Parse<ApiHost>(Environment.GetEnvironmentVariable("TESTS_API_HOST") ?? nameof(ApiHost.Aspire), ignoreCase: true);
}
