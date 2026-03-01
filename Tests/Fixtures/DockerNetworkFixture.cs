using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using TUnit.Core.Interfaces;

namespace Tests.Fixtures;

public class DockerNetworkFixture : FixtureBase
{
    public INetwork Network { get; private set; } = null!;
    protected override async Task Initialize()
    {
        Network = new NetworkBuilder().Build();
        await Network.CreateAsync();
    }
    
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await Network.DisposeAsync();
        }
        catch
        {
            // ignored
        }
    }
}