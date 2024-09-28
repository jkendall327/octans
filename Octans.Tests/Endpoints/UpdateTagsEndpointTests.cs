using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Xunit.Abstractions;

namespace Octans.Tests;

public class UpdateTagsEndpointTests(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task UpdateTags_ValidRequest_ReturnsOk()
    {
        var hash = await SetupInitialData();

        var request = new UpdateTagsRequest
        {
            HashId = hash.Id,
            TagsToAdd = new[] { new TagModel { Namespace = "character", Subtag = "samus aran" } },
            TagsToRemove = new[] { new TagModel { Namespace = "weapon", Subtag = "laser" } }
        };

        var response = await _api.UpdateTags(request);

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

        var response = await _api.UpdateTags(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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