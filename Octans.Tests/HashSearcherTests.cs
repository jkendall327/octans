using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Octans.Core;
using Octans.Core.Models;

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
        var all = new List<HashItem>
        {
            GenerateRandomHashItem(),
            GenerateRandomHashItem(),
            GenerateRandomHashItem()
        };
        
        _db.AddRange(all);

        await _db.SaveChangesAsync();
        
        var result = await _sut.Search(new List<Predicate>());

        result.Should().BeEquivalentTo(all);
    }

    /// <summary>
    /// Finds all hashes with a tag that has namespace "character" when we use the wildcard predicate "character:*"
    /// </summary>
    [Fact]
    public void FindsHashes_WithMappingsForNamespace_WhenWildcardNamespaceUsed()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Finds all hashes with tag "character:samus aran" when the predicate is precisely "character:samus aran"
    /// </summary>
    [Fact]
    public void FindsHashes_WithExactMatchForTag_WhenExactTagUsed()
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void ReturnsOnlyNHashes_WhenALimitOfNIsSpecified()
    {
        throw new NotImplementedException();
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