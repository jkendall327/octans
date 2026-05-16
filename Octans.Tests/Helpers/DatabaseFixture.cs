using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Data.Models;

namespace Octans.Tests.Helpers;

public class DatabaseFixture : IAsyncLifetime
{
    private SqliteConnection Connection { get; } = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await Connection.OpenAsync();
    }

    public void RegisterDbContext(IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        services.AddDbContext<ServerDbContext>(options => { options.UseSqlite(Connection); },
            optionsLifetime: lifetime);

        services.AddDbContextFactory<ServerDbContext>();
    }

    public static async Task ResetAsync(IServiceProvider provider)
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