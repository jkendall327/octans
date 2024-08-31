using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Channels;
using Octans.Core.Models;
using Octans.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Octans.Core;

namespace Octans.Tests;

public class EndpointTest : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    protected readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    protected readonly MockFileSystem _fileSystem = new();
    protected ServerDbContext _context = null!;
    protected readonly string _appRoot = "C:/app";
    protected readonly SpyChannelWriter<ThumbnailCreationRequest> _spyChannel = new();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        
        _context = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDbContext>();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    protected EndpointTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IPath));
                services.RemoveAll(typeof(IDirectoryInfo));
                services.RemoveAll(typeof(IFile));
                services.RemoveAll(typeof(IFileSystem));

                services.AddSingleton(_fileSystem.Path);
                services.AddSingleton(_fileSystem.DirectoryInfo);
                services.AddSingleton(_fileSystem.File);
                services.AddSingleton<IFileSystem>(_fileSystem);

                services.RemoveAll(typeof(ChannelWriter<ThumbnailCreationRequest>));
                services.AddSingleton<ChannelWriter<ThumbnailCreationRequest>>(_spyChannel);
                
                services.RemoveAll(typeof(DbContextOptions<ServerDbContext>));

                services.AddDbContext<ServerDbContext>(options =>
                {
                    options.UseSqlite(_connection);
                });

                services.RemoveAll(typeof(IOptions<GlobalSettings>));
                
                services.Configure<GlobalSettings>(s =>
                {
                    s.AppRoot = _appRoot;
                });
                
                // Ensure the database is created
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                
                db.Database.EnsureCreated();
            });
            
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {
                        "DatabaseRoot", _appRoot
                    }
                });
                
            });
        });
    }
}