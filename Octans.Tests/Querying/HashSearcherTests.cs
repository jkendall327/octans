using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Octans.Core.Querying;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Tagging;
using Octans.Tests.Helpers;

namespace Octans.Tests.Querying;

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

        var items = await GetItems(3);

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

        var items = await GetItems(1);

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
    public async Task FindsHashes_WithEveryExactMatch_WhenMultipleExactTagsUsed()
    {
        await SeedData();

        var items = await GetItems(3);
        var expected = items[0];

        await AddMappings("character", "samus", items[0], items[2]);
        await AddMappings("series", "metroid", items[0], items[1]);

        var request = new DecomposedQuery
        {
            TagsToInclude = [new("character", "samus"), new("series", "metroid")]
        };

        var results = await _sut.Search(request);

        results.Should().BeEquivalentTo([expected], "every positive tag predicate must match");
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount_WhenExactTagUsed()
    {
        await SeedData();

        var items = await GetItems(2);

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
        var item = CreateHashItem(1);
        item.RepositoryId = (int)RepositoryType.Trash;
        _db.Hashes.Add(item);
        await _db.SaveChangesAsync();

        var request = new DecomposedQuery();
        var results = await _sut.Search(request);

        results.Should().NotContain(i => i.Id == item.Id);
    }

    [Fact]
    public async Task ExcludesDeletedHashes()
    {
        var item = CreateHashItem(1);
        item.DeletedAt = TestClock.UtcNow;
        _db.Hashes.Add(item);
        await _db.SaveChangesAsync();

        var request = new DecomposedQuery();
        var results = await _sut.Search(request);

        results.Should().NotContain(i => i.Id == item.Id);
    }

    [Fact]
    public async Task IncludesTrash_WhenTrashFilterSpecified()
    {
        var item = CreateHashItem(1);
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
        var inboxItem = CreateHashItem(1);
        inboxItem.RepositoryId = (int)RepositoryType.Inbox;

        var archiveItem = CreateHashItem(2);
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

    [Fact]
    public async Task OnlyIncludesInbox_WhenInboxSystemPredicateSpecified()
    {
        var inboxItem = CreateHashItem(1);
        inboxItem.RepositoryId = (int)RepositoryType.Inbox;

        var archiveItem = CreateHashItem(2);
        archiveItem.RepositoryId = (int)RepositoryType.Archive;

        _db.Hashes.AddRange(inboxItem, archiveItem);
        await _db.SaveChangesAsync();

        var request = new DecomposedQuery
        {
            SystemPredicates = [new RepositoryPredicate { Repository = RepositoryType.Inbox }],
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
            CreateHashItem(1),
            CreateHashItem(2),
            CreateHashItem(3),
            CreateHashItem(4),
            CreateHashItem(5),
        };

        _db.AddRange(all);

        await _db.SaveChangesAsync();
    }

    private async Task<List<HashItem>> GetItems(int count)
    {
        // Must exclude trash to match default search behavior
        return await _db.Hashes
            .Where(h => h.RepositoryId != (int)RepositoryType.Trash)
            .OrderBy(h => h.Id)
            .Take(count)
            .ToListAsync();
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

    private static HashItem CreateHashItem(int marker)
    {
        return new()
        {
            Hash = CreateHash(marker),
            RepositoryId = (int)RepositoryType.Inbox
        };
    }

    private static byte[] CreateHash(int marker)
    {
        var hash = new byte[32];
        var markerBytes = BitConverter.GetBytes(marker);
        Array.Copy(markerBytes, 0, hash, hash.Length - markerBytes.Length, markerBytes.Length);

        return hash;
    }
}
