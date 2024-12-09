using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Querying;

namespace Octans.Tests;

public class HashSearcherTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServerDbContext _db = null!;
    private HashSearcher _sut = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new(optionsBuilder);

        await _db.Database.EnsureCreatedAsync();

        _sut = new(_db);
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

        var all = await _db.Hashes.ToListAsync();

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
            TagsToInclude = [new() { Namespace = "character", Subtag = "samus aran" }]
        };

        var results = await _sut.Search(request);

        results.Single().Should().Be(item, "it is the only item with this namespace/tag pairing");
    }

    [Fact(Skip = "Not implemented yet")]
    public void ReturnsOnlyNHashes_WhenALimitOfNIsSpecified()
    {
        throw new NotImplementedException();
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
        var all = await _db.Hashes.ToListAsync();
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
            DeletedAt = random.Next(2) == 0 ? null : DateTime.UtcNow.AddDays(-random.Next(1, 365))
        };
    }

    private static byte[] GenerateRandomHash()
    {
        var hash = new byte[32];
        Random.Shared.NextBytes(hash);
        return hash;
    }
}