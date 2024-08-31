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

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    protected EndpointTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            SetupFakeConfiguration(builder);

            builder.ConfigureServices(services =>
            {
                ReplaceNormalServices(services);
                
                ReplaceConfiguredOptions(services);

                AddFakeDatabase(services);
            });
        });
    }

    private void ReplaceConfiguredOptions(IServiceCollection services)
    {
        services.RemoveAll(typeof(IOptions<GlobalSettings>));
        services.Configure<GlobalSettings>(s =>
        {
            s.AppRoot = _appRoot;
        });
    }

    private void ReplaceNormalServices(IServiceCollection services)
    {
        services.ReplaceExistingRegistrationsWith(_fileSystem.Path);
        services.ReplaceExistingRegistrationsWith(_fileSystem.DirectoryInfo);
        services.ReplaceExistingRegistrationsWith(_fileSystem.File);
        services.ReplaceExistingRegistrationsWith<IFileSystem>(_fileSystem);
        
        services.ReplaceExistingRegistrationsWith<ChannelWriter<ThumbnailCreationRequest>>(_spyChannel);
    }

    private void SetupFakeConfiguration(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection([new("DatabaseRoot", _appRoot)]);
        });
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