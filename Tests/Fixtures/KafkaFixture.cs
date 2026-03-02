using Testcontainers.Kafka;
using TUnit.Core.Interfaces;

namespace Tests.Fixtures;

public class KafkaFixture : FixtureBase
{
    [ClassDataSource<DockerNetworkFixture>(Shared = SharedType.PerTestSession)]
    public required DockerNetworkFixture DockerNetwork { get; init; }
    
    private KafkaContainer _container = null!;
    public string ConnectionString => TestsConfiguration.LocalMode ? _container.GetBootstrapAddress() : $"{_container.Name.TrimStart('/')}:{KafkaBuilder.BrokerPort}";
    public string BootstrapAddress => _container.GetBootstrapAddress();
    protected override async Task Initialize()
    {
        _container = new KafkaBuilder("confluentinc/cp-kafka:7.8.0")
            .WithNetwork(DockerNetwork.Network)
            .WithNetworkAliases("kafka")
            .Build();
        await _container.StartAsync();

        var config = new Confluent.Kafka.AdminClientConfig { BootstrapServers = _container.GetBootstrapAddress() };
        using var adminClient = new Confluent.Kafka.AdminClientBuilder(config).Build();
        
        await adminClient.CreateTopicsAsync([
            new Confluent.Kafka.Admin.TopicSpecification { Name = "orders-topic", NumPartitions = 1, ReplicationFactor = 1 }
        ]);
    }
    
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        catch
        {
            // ignored
        }
    }
}