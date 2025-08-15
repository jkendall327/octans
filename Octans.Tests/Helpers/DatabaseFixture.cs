using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core.Models;

namespace Octans.Tests;

public class DatabaseFixture : IAsyncLifetime
{
    public SqliteConnection Connection { get; } = new("DataSource=:memory:");
    
    public async Task InitializeAsync()
    {
        await Connection.OpenAsync();
    }

    public async Task ResetAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}