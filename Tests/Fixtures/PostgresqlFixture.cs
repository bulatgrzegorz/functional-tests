using Testcontainers.PostgreSql;

namespace Tests.Fixtures;

public class PostgresqlFixture : FixtureBase
{
    [ClassDataSource<DockerNetworkFixture>(Shared = SharedType.PerTestSession)]
    public required DockerNetworkFixture DockerNetwork { get; init; }
    
    private PostgreSqlContainer _container = null!;
    public string ConnectionString => TestsConfiguration.LocalMode ? _container.GetConnectionString() : $"Host={AliasName};Port={PostgreSqlBuilder.PostgreSqlPort};Database=postgres;Username=postgres;Password=postgres";

    private const string AliasName = "postgres";
    
    protected override async Task Initialize()
    {
        _container = new PostgreSqlBuilder("postgres:17")
            .WithNetwork(DockerNetwork.Network)
            .WithNetworkAliases("postgres")
            .Build();
        await _container.StartAsync();

        await using var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("CREATE TABLE IF NOT EXISTS orders (sku VARCHAR(50), quantity INT);", connection);
        await cmd.ExecuteNonQueryAsync();
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