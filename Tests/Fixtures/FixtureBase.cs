using OpenTelemetry;
using TUnit.Core.Interfaces;

namespace Tests.Fixtures;

public abstract class FixtureBase : IAsyncInitializer, IAsyncDisposable
{
    public async Task InitializeAsync()
    {
        //that's being done to make sure we have no collision between candidates for trace root - that should always be "test name" trace, no some random http call made during initialization.
        using (SuppressInstrumentationScope.Begin())
        {
            await Initialize();
        }
    }

    protected abstract Task Initialize();

    public abstract ValueTask DisposeAsync();
}