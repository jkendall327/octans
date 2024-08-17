using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Tests;

public class UpdateTagsEndpointTests : EndpointTest
{
    public UpdateTagsEndpointTests(WebApplicationFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task UpdateTags_ValidRequest_ReturnsOk()
    {
        var client = _factory.CreateClient();
        
        var hash = await SetupInitialData();

        var request = new UpdateTagsRequest
        {
            HashId = hash.Id,
            TagsToAdd = new[] { new TagModel { Namespace = "character", Subtag = "samus aran" } },
            TagsToRemove = new[] { new TagModel { Namespace = "weapon", Subtag = "laser" } }
        };

        var response = await client.PutAsJsonAsync("/updateTags", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedTags = await _context.Mappings
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
        var client = _factory.CreateClient();

        var tag = new TagModel { Namespace = "new", Subtag = "tag" };
        
        var request = new UpdateTagsRequest
        {
            // Non-existent hash ID
            HashId = 999, 
            TagsToAdd = new[]
            {
                tag
            },
            TagsToRemove = Array.Empty<TagModel>()
        };

        var response = await client.PutAsJsonAsync("/updateTags", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HashItem> SetupInitialData()
    {
        var hash = new HashItem { Hash = [1, 2, 3, 4] };
        
        _context.Hashes.Add(hash);

        var tag = new Tag
        {
            Namespace = new() { Value = "weapon" },
            Subtag = new() { Value = "laser" }
        };
        
        _context.Tags.Add(tag);

        _context.Mappings.Add(new()
        {
            Hash = hash, 
            Tag = tag
        });

        await _context.SaveChangesAsync();

        return hash;
    }
}