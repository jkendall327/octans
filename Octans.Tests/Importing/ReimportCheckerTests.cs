using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Octans.Core.Importing;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Models;
using Octans.Server;
using Xunit.Abstractions;
using Octans.Tests;

namespace Octans.Tests.Importing;

public class ReimportCheckerTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;
    private readonly ServiceProvider _provider;
    private readonly ReimportChecker _sut;
    private readonly ServerDbContext _dbContext;
    private readonly MockFileSystem _fileSystem;
    private readonly string _appRoot = "/app";

    public ReimportCheckerTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));

        services.AddDbContext<ServerDbContext>(options => { options.UseSqlite(databaseFixture.Connection); },
            optionsLifetime: ServiceLifetime.Scoped);

        services.AddScoped<ReimportChecker>();

        _fileSystem = new MockFileSystem();
        services.AddSingleton<IFileSystem>(_fileSystem);
        services.Configure<GlobalSettings>(s => s.AppRoot = _appRoot);
        services.AddSingleton<SubfolderManager>();
        services.AddSingleton<FilesystemWriter>();

        _provider = services.BuildServiceProvider();
        _dbContext = _provider.GetRequiredService<ServerDbContext>();
        _sut = _provider.GetRequiredService<ReimportChecker>();

        var subfolderManager = _provider.GetRequiredService<SubfolderManager>();
        subfolderManager.MakeSubfolders();
    }

    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckIfPreviouslyDeleted_RestoresContent_WhenContentMissing()
    {
        // Arrange
        var bytes = TestingConstants.MinimalJpeg;
        var hashed = HashedBytes.FromUnhashed(bytes);

        // Add hash to DB as deleted
        var hashItem = new HashItem
        {
            Hash = hashed.Bytes,
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };
        _dbContext.Hashes.Add(hashItem);
        await _dbContext.SaveChangesAsync();

        // Ensure file is NOT on filesystem
        var subfolderManager = _provider.GetRequiredService<SubfolderManager>();
        var destination = subfolderManager.GetDestination(hashed, bytes);
        _fileSystem.FileExists(destination).Should().BeFalse();

        // Act
        var result = await _sut.CheckIfPreviouslyDeleted(hashed, true, bytes);

        // Assert
        result!.Ok.Should().BeTrue();
        _fileSystem.FileExists(destination).Should().BeTrue();
    }
}
