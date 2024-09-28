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
using Microsoft.Extensions.Options;
using Octans.Core;
using Refit;
using Xunit.Abstractions;

namespace Octans.Tests;

public class EndpointTest : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    protected readonly WebApplicationFactory<Program> _factory;
    
    // Database
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    protected ServerDbContext _context = null!;
    
    // Filesystem
    protected readonly string _appRoot = "C:/app";
    protected readonly MockFileSystem _fileSystem = new();
    
    // Channels
    protected readonly SpyChannelWriter<ThumbnailCreationRequest> _spyChannel = new();

    protected readonly IOctansApi _api;
    
    protected EndpointTest(WebApplicationFactory<Program> factory, ITestOutputHelper testOutputHelper)
    {
        _factory = factory.WithWebHostBuilder(builder =>
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
                    s.AppRoot = _appRoot;
                });
            });
        });
        
        _api = RestService.For<IOctansApi>(_factory.CreateClient());
    }
    
    /// <summary>
    /// Checks we've correctly set things up with fakes rather than the real DB, filesystem etc.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        
        _context = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDbContext>();

        var connectionString = _context.Database.GetConnectionString();
        
        if (connectionString != "DataSource=:memory:")
        {
            throw new InvalidOperationException(
                "The in-memory database wasn't set up for tests (check the service replacement code)");
        }
        
        await _context.Database.EnsureCreatedAsync();

        var filesystem = _factory.Services.GetRequiredService<IFileSystem>();

        if (filesystem is FileSystem)
        {
            throw new InvalidOperationException("Test system was set up with a real filesystem, not a mock one (check the service replacement code)");
        }
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void ReplaceNormalServices(IServiceCollection services)
    {
        services.ReplaceExistingRegistrationsWith(_fileSystem.Path);
        services.ReplaceExistingRegistrationsWith(_fileSystem.DirectoryInfo);
        services.ReplaceExistingRegistrationsWith(_fileSystem.File);
        services.ReplaceExistingRegistrationsWith<IFileSystem>(_fileSystem);
        
        services.ReplaceExistingRegistrationsWith<ChannelWriter<ThumbnailCreationRequest>>(_spyChannel);
    }

    private void AddFakeDatabase(IServiceCollection services)
    {
        services.RemoveAll(typeof(DbContextOptions<ServerDbContext>));

        services.AddDbContext<ServerDbContext>(options =>
        {
            options.UseSqlite(_connection);
        });
    }
}