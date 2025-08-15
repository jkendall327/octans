using Microsoft.Data.Sqlite;

namespace Octans.Tests;

public class DatabaseFixture : IAsyncLifetime
{
    public SqliteConnection Connection { get; } = new("DataSource=:memory:");
    
    public async Task InitializeAsync()
    {
        await Connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}