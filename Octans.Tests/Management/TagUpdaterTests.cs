using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Tags;
using Xunit.Abstractions;

namespace Octans.Tests;

public class TagUpdaterTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private const string AppRoot = "/app";
    private readonly TagUpdater _sut;

    private readonly IServiceProvider _provider;
    private readonly DatabaseFixture _databaseFixture;
    private readonly MockFileSystem _fileSystem = new();

    public TagUpdaterTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;

        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));
        services.AddBusinessServices();

        databaseFixture.RegisterDbContext(services);

        services.AddSingleton<IFileSystem>(_fileSystem);

        services.Configure<GlobalSettings>(s => s.AppRoot = AppRoot);

        _provider = services.BuildServiceProvider();

        _sut = _provider.GetRequiredService<TagUpdater>();
    }

    [Fact]
    public async Task UpdateTags_ValidRequest_ReturnsOk()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = await SetupInitialData(db);

        var request = new UpdateTagsRequest(hash.Id,
            [new("character", "samus aran")],
            [new("weapon", "laser")]);

        var response = await _sut.UpdateTags(request);

        response
            .Should()
            .Be(TagUpdateResult.TagsUpdated);

        var updatedTags = await db.Mappings
            .Where(m => m.Hash.Id == hash.Id)
            .Select(m => new
            {
                Namespace = m.Tag.Namespace.Value,
                Subtag = m.Tag.Subtag.Value
            })
            .ToListAsync();

        updatedTags.Should().ContainSingle(t => t.Namespace == "character" && t.Subtag == "samus aran");
        updatedTags.Should().NotContain(t => t.Namespace == "weapon" && t.Subtag == "laser");
    }

    [Fact]
    public async Task UpdateTags_InvalidHashId_ReturnsNotFound()
    {
        var tag = new TagModel("new", "tag");

        var request = new UpdateTagsRequest(999, [tag], []);

        var response = await _sut.UpdateTags(request);

        response
            .Should()
            .Be(TagUpdateResult.HashNotFound);
    }

    private async Task<HashItem> SetupInitialData(ServerDbContext db)
    {
        var hash = new HashItem { Hash = [1, 2, 3, 4] };

        db.Hashes.Add(hash);

        var tag = new Tag
        {
            Namespace = new() { Value = "weapon" },
            Subtag = new() { Value = "laser" }
        };

        db.Tags.Add(tag);

        db.Mappings.Add(new()
        {
            Hash = hash,
            Tag = tag
        });

        await db.SaveChangesAsync();

        return hash;
    }

    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetAsync(_provider);

        var folders = _provider.GetRequiredService<SubfolderManager>();

        folders.MakeSubfolders();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}