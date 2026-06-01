using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public sealed class MediaEndpointTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GetMedia_ShouldReturnStoredBytesForHash()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var stored = await factory.AddStoredImageAsync(TestingConstants.MinimalJpeg);

        var response = await client.GetAsync(new Uri($"/media/{stored.Hash.Hex}", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        response.Headers.CacheControl?.Public.Should().BeTrue();
        response.Headers.CacheControl?.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.ETag?.Tag.Should().Be($"\"{stored.Hash.Hex}\"");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(TestingConstants.MinimalJpeg);
    }

    [Fact]
    public async Task GetMedia_ShouldReturnNotFound_WhenStoredFileIsMissing()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        var hash = ContentHash.FromContent(TestingConstants.MinimalJpeg);

        await using (var scope = factory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Hashes.Add(new HashItem
            {
                Hash = hash.Bytes,
                Extension = "jpg",
                ContentType = "image/jpeg"
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(new Uri($"/media/{hash.Hex}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

}
