using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Querying;
using Octans.Core.Repositories;
using Octans.Core.Tags;

namespace Octans.Tests;

public class HashSearcherTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServerDbContext _db = null!;
    private HashSearcher _sut = null!;
    private TagParentService _tagParentService = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new(optionsBuilder);

        await _db.Database.EnsureCreatedAsync();

        _tagParentService = new TagParentService(_db);
        _sut = new(_db, _tagParentService);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsEverythingWhenPredicateIsEmpty()
    {
        await SeedData();

        var all = await _db.Hashes.Where(h => h.RepositoryId != (int)RepositoryType.Trash).ToListAsync();

        var result = await _sut.Search(new());

        result.Should().BeEquivalentTo(all);
    }

    /// <summary>
    /// Finds all hashes with a tag that has namespace "character" when we use the wildcard predicate "character:*"
    /// </summary>
    [Fact]
    public async Task FindsHashes_WithMappingsForNamespace_WhenWildcardNamespaceUsed()
    {
        await SeedData();

        var items = await GetRandomItems(3);

        var firstSubtag = items.Take(2).ToArray();
        var secondSubtag = items.Except(firstSubtag).ToArray();

        firstSubtag.Should().NotBeEmpty();
        secondSubtag.Should().NotBeEmpty();

        await AddMappings("character", "samus aran", firstSubtag);
        await AddMappings("character", "bayonetta", secondSubtag);

        var request = new DecomposedQuery
        {
            WildcardNamespacesToInclude = ["character"]
        };

        var results = await _sut.Search(request);

        results.Should().BeEquivalentTo(items, "the items all have the character subtag");
    }

    /// <summary>
    /// Finds all hashes with tag "character:samus aran" when the predicate is precisely "character:samus aran"
    /// </summary>
    [Fact]
    public async Task FindsHashes_WithExactMatchForTag_WhenExactTagUsed()
    {
        await SeedData();

        var items = await GetRandomItems(1);

        var item = items.Single();

        await AddMappings("character", "samus aran", item);

        var request = new DecomposedQuery()
        {
            TagsToInclude = [new("character", "samus aran")]
        };

        var results = await _sut.Search(request);

        results.Single().Should().Be(item, "it is the only item with this namespace/tag pairing");
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount_WhenExactTagUsed()
    {
        await SeedData();

        var items = await GetRandomItems(2);

        await AddMappings("character", "mario", items.ToArray());

        var request = new DecomposedQuery()
        {
            TagsToInclude = [new("character", "mario")]
        };

        var count = await _sut.CountAsync(request);

        count.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_ReturnsZero_WhenNoMatch()
    {
        await SeedData();

        var request = new DecomposedQuery()
        {
            TagsToInclude = [new("character", "luigi")]
        };

        var count = await _sut.CountAsync(request);

        count.Should().Be(0);
    }

    [Fact]
    public async Task CountAsync_ReturnsAll_WhenEmptyQuery()
    {
        await SeedData();
        var total = await _db.Hashes.Where(h => h.RepositoryId != (int)RepositoryType.Trash).CountAsync();

        var request = new DecomposedQuery();
        var count = await _sut.CountAsync(request);

        count.Should().Be(total);
    }

    [Fact]
    public async Task ReturnsOnlyNHashes_WhenALimitOfNIsSpecified()
    {
        await SeedData();
        var request = new DecomposedQuery
        {
            Limit = 2
        };

        var results = await _sut.Search(request);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SkipsNHashes_WhenAnOffsetOfNIsSpecified()
    {
        await SeedData();

        // Get all items ordered by ID to simulate the default sort order in Search
        // NOTE: We must filter out Trash because the searcher does it by default now
        var allItems = await _db.Hashes
            .Where(h => h.RepositoryId != (int)RepositoryType.Trash)
            .OrderBy(h => h.Id)
            .ToListAsync();

        var expected = allItems.Skip(2).Take(1).Single();

        var request = new DecomposedQuery
        {
            Offset = 2,
            Limit = 1
        };

        var results = await _sut.Search(request);

        results.Single().Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task ExcludesTrash_ByDefault()
    {
        var item = GenerateRandomHashItem();
        item.RepositoryId = (int)RepositoryType.Trash;
        _db.Hashes.Add(item);
        await _db.SaveChangesAsync();

        var request = new DecomposedQuery();
        var results = await _sut.Search(request);

        results.Should().NotContain(i => i.Id == item.Id);
    }

    [Fact]
    public async Task IncludesTrash_WhenTrashFilterSpecified()
    {
        var item = GenerateRandomHashItem();
        item.RepositoryId = (int)RepositoryType.Trash;
        _db.Hashes.Add(item);
        await _db.SaveChangesAsync();

        var request = new DecomposedQuery
        {
            RepositoryFilters = [RepositoryType.Trash]
        };
        var results = await _sut.Search(request);

        results.Should().Contain(i => i.Id == item.Id);
    }

    [Fact]
    public async Task OnlyIncludesInbox_WhenInboxFilterSpecified()
    {
        var inboxItem = GenerateRandomHashItem();
        inboxItem.RepositoryId = (int)RepositoryType.Inbox;

        var archiveItem = GenerateRandomHashItem();
        archiveItem.RepositoryId = (int)RepositoryType.Archive;

        _db.Hashes.AddRange(inboxItem, archiveItem);
        await _db.SaveChangesAsync();

        var request = new DecomposedQuery
        {
            RepositoryFilters = [RepositoryType.Inbox]
        };
        var results = await _sut.Search(request);

        results.Should().Contain(i => i.Id == inboxItem.Id);
        results.Should().NotContain(i => i.Id == archiveItem.Id);
    }

    private async Task SeedData()
    {
        var all = new List<HashItem>
        {
            GenerateRandomHashItem(),
            GenerateRandomHashItem(),
            GenerateRandomHashItem(),
            GenerateRandomHashItem(),
            GenerateRandomHashItem(),
        };

        _db.AddRange(all);

        await _db.SaveChangesAsync();
    }

    private async Task<List<HashItem>> GetRandomItems(int count)
    {
        // Must exclude trash to match default search behavior
        var all = await _db.Hashes.Where(h => h.RepositoryId != (int)RepositoryType.Trash).ToListAsync();
        return all.OrderBy(i => Guid.NewGuid()).Take(count).ToList();
    }

    private async Task AddMappings(string @namespace, string subtag, params HashItem[] items)
    {
        var ns = new Namespace { Value = @namespace };
        var st = new Subtag { Value = subtag };
        var tag = new Tag { Namespace = ns, Subtag = st };

        _db.Tags.Add(tag);

        foreach (var item in items)
        {
            var mapping = new Mapping
            {
                Hash = item,
                Tag = tag
            };

            _db.Mappings.Add(mapping);
        }

        await _db.SaveChangesAsync();
    }

    private static HashItem GenerateRandomHashItem()
    {
        var random = Random.Shared;

        return new()
        {
            Hash = GenerateRandomHash(),
            DeletedAt = random.Next(2) == 0 ? null : DateTime.UtcNow.AddDays(-random.Next(1, 365)),
            RepositoryId = (int)RepositoryType.Inbox
        };
    }

    private static byte[] GenerateRandomHash()
    {
        var hash = new byte[32];
        Random.Shared.NextBytes(hash);
        return hash;
    }
}
