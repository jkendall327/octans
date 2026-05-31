using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Octans.Client;
using Octans.Core;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public sealed class OctansApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly ITestOutputHelper? _output;

    public OctansApiFactory(ITestOutputHelper? output = null)
    {
        _output = output;
        AppRoot = $"/octans-api-{Guid.NewGuid():N}";
        FileSystem = new();
        TimeProvider = new(TestClock.UtcNow);

        var connectionString = $"Data Source=OctansApiFactory-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new(connectionString);
        _keepAliveConnection.Open();
        ConnectionString = connectionString;
    }

    public string AppRoot { get; }
    public MockFileSystem FileSystem { get; }
    public FakeTimeProvider TimeProvider { get; }
    public string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalSettings:AppRoot"] = AppRoot,
                ["Octans:BackgroundWorkers:Enabled"] = "false",
                ["ImportFolder:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveOctansBackgroundWorkers();

            services.RemoveAll<ServerDbContext>();
            services.RemoveAll<DbContextOptions<ServerDbContext>>();
            services.RemoveAll<IDbContextFactory<ServerDbContext>>();

            services.AddDbContextFactory<ServerDbContext>(options => options.UseSqlite(ConnectionString));
            services.AddDbContext<ServerDbContext>(
                options => options.UseSqlite(ConnectionString),
                optionsLifetime: ServiceLifetime.Singleton);

            services.RemoveAll<IFileSystem>();
            services.AddSingleton<IFileSystem>(FileSystem);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);

            services.Configure<GlobalSettings>(settings => settings.AppRoot = AppRoot);

            if (_output is not null)
            {
                services.AddLogging(logging => logging.AddProvider(new XUnitLoggerProvider(_output)));
            }
        });
    }

    public AsyncServiceScope CreateAsyncScope()
    {
        return Services.CreateAsyncScope();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}
