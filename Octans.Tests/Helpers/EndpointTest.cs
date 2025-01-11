using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Octans.Core.Models;
using Octans.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Octans.Core;
using Octans.Core.Communication;
using Refit;
using Xunit.Abstractions;

namespace Octans.Tests;

public class EndpointTest : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    protected WebApplicationFactory<Program> Factory { get; }

    // Database
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    protected ServerDbContext Context { get; private set; } = null!;

    // Filesystem
    protected const string AppRoot = "C:/app";
    protected MockFileSystem FileSystem { get; } = new();

    // Other
    protected SpyChannelWriter<ThumbnailCreationRequest> SpyChannel { get; } = new();
    protected IOctansApi Api { get; }

    protected EndpointTest(WebApplicationFactory<Program> factory, ITestOutputHelper testOutputHelper)
    {
        Factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new XUnitLoggerProvider(testOutputHelper));
            });

            builder.ConfigureServices(services =>
            {
                ReplaceNormalServices(services);

                AddFakeDatabase(services);

                // This will replace any IOptions<GlobalSettings> already configured.
                services.Configure<GlobalSettings>(s =>
                {
                    s.AppRoot = AppRoot;
                });
            });
        });

        Api = RestService.For<IOctansApi>(Factory.CreateClient());
    }

    /// <summary>
    /// Checks we've correctly set things up with fakes rather than the real DB, filesystem etc.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        Context = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDbContext>();

        var connectionString = Context.Database.GetConnectionString();

        if (connectionString != "DataSource=:memory:")
        {
            throw new InvalidOperationException(
                "The in-memory database wasn't set up for tests (check the service replacement code)");
        }

        await Context.Database.EnsureCreatedAsync();

        var filesystem = Factory.Services.GetRequiredService<IFileSystem>();

        if (filesystem is FileSystem)
        {
            throw new InvalidOperationException("Test system was set up with a real filesystem, not a mock one (check the service replacement code)");
        }
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void ReplaceNormalServices(IServiceCollection services)
    {
        services.ReplaceExistingRegistrationsWith(FileSystem.Path);
        services.ReplaceExistingRegistrationsWith(FileSystem.DirectoryInfo);
        services.ReplaceExistingRegistrationsWith(FileSystem.File);
        services.ReplaceExistingRegistrationsWith<IFileSystem>(FileSystem);

        services.ReplaceExistingRegistrationsWith<ChannelWriter<ThumbnailCreationRequest>>(SpyChannel);
    }

    private void AddFakeDatabase(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<ServerDbContext>>();

        services.AddDbContext<ServerDbContext>(options =>
        {
            options.UseSqlite(_connection);
        });
    }
}