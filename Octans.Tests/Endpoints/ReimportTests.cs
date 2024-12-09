using FluentAssertions;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace Octans.Tests;

public class ReimportTests(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldNotReimportByDefault()
    {
        var hash = await SetupDeletedImage();

        var request = BuildRequest();

        request.AllowReimportDeleted = false;

        var result = await Api.ProcessImport(request);

        result.Content.Should().NotBeNull();
        result.Content!.Results.Single().Ok.Should().BeFalse("we tried to reimport a deleted file when that wasn't allowed");

        var dbHash = await Context.Hashes.FindAsync(hash.Id);

        dbHash.Should().NotBeNull("hashes for deleted files remain in the DB to prevent reimports");

        await Context.Entry(dbHash!).ReloadAsync();

        dbHash!.DeletedAt.Should().NotBeNull("reimporting wasn't allowed, so it should still be marked as deleted");
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldReimportWhenAllowed()
    {
        var hash = await SetupDeletedImage();

        var request = BuildRequest();

        request.AllowReimportDeleted = true;

        var result = await Api.ProcessImport(request);

        result.Content.Should().NotBeNull();
        result.Content!.Results.Single().Ok.Should().BeTrue("reimporting the deleted hash was specifically requested");

        var dbHash = await Context.Hashes.FindAsync(hash.Id);

        dbHash.Should().NotBeNull();

        // Make sure we don't use the one in the change tracker, as that won't reflect the changes from the API.
        await Context.Entry(dbHash!).ReloadAsync();

        dbHash!.DeletedAt.Should().BeNull("reimporting was allowed, so its soft-deletion mark should be gone");
    }

    private async Task<HashItem> SetupDeletedImage()
    {
        var hash = new HashItem
        {
            Hash = HashedBytes.FromUnhashed(TestingConstants.MinimalJpeg).Bytes,
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };

        Context.Hashes.Add(hash);
        await Context.SaveChangesAsync();

        return hash;
    }

    private ImportRequest BuildRequest()
    {
        FileSystem.AddFile("C:/myfile.jpeg", new(TestingConstants.MinimalJpeg));

        var item = new ImportItem
        {
            Source = new("C:/myfile.jpeg"),
            Tags = [new() { Namespace = "test", Subtag = "reimport" }]
        };

        var request = new ImportRequest
        {
            Items = [item],
            ImportType = ImportType.File,
            DeleteAfterImport = false,
            AllowReimportDeleted = false
        };

        return request;
    }
}