using Tests.Fixtures;

namespace Tests;

public static class GlobalHooks
{
    [Before(TestDiscovery)]
    public static void BeforeTestDiscovery()
    {
        // TUnit.OpenTelemetry (via TUnit.Aspire) exports the runner's spans (TUnit, TUnit.Lifecycle, HttpClient) here,
        // and TUnit.Aspire forwards the API's telemetry here too. Both read it as http/protobuf despite the name,
        // and right after this hook, so it has to be known before the dashboard container starts.
        Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", AspireDashboardFixture.OtlpHttpEndpoint);
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", "functional-tests");
        // TUnit's HTML report listens to every ActivitySource, which makes the auto-start step aside without this
        Environment.SetEnvironmentVariable("TUNIT_OTEL_AUTOSTART", "1");
    }

    [BeforeEvery(Test)]
    public static void BeforeEveryTest()
    {
        // trace list in Aspire shows the root span name - make it the test name instead of TUnit's generic "test case"
        var testCase = TestContext.Current!.Activity;
        testCase?.DisplayName = TestContext.Current.Metadata.TestName;
        // TUnit opens the span before waiting for shared fixtures (their setup is in the session trace as
        // "initialize ...") - start it once they're ready, so the test's trace shows only the test
        testCase?.SetStartTime(DateTime.UtcNow);
    }

    [AfterEvery(Test)]
    public static void AfterEveryTest()
    {
        // ...and end it before the last test to finish disposes the shared fixtures (Stop keeps an end time set here)
        TestContext.Current!.Activity?.SetEndTime(DateTime.UtcNow);
    }
}
