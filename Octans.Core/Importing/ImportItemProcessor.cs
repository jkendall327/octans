using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;
using Octans.Core.Importing.Filters;
using Octans.Core.Importing.RawByteProviders;
using Octans.Core.Thumbnails;

namespace Octans.Core.Importing;

internal sealed class ImportItemProcessor(
    ImportFilterService filterService,
    ReimportChecker reimportChecker,
    DatabaseWriter databaseWriter,
    FilesystemWriter filesystemWriter,
    ImageStorage imageStorage,
    ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
    FileImporter file,
    PostImporter post,
    SimpleImporter simple,
    ILogger<ImportItemProcessor> logger)
{
    public async Task<ImportItemResult> Process(ImportRequest request, ImportItem item)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ItemImportId"] = Guid.NewGuid(),
        });

        if (item.Url is not null && item.Filepath is not null)
        {
            throw new ArgumentException("Import item had both a URL and a filepath specified.", nameof(item));
        }

        if (request.ImportType is ImportType.Post)
        {
            return await post.Import(item);
        }

        var task = request.ImportType switch
        {
            ImportType.File => file.GetRawBytes(item),
            ImportType.RawUrl => simple.GetRawBytes(item),
            _ => throw new InvalidOperationException("Import type not supported")
        };

        var bytes = await task;

        logger.LogDebug("Total size: {SizeInBytes}", bytes.Length);

        var filterResult = await filterService.ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            return filterResult;
        }

        var hash = ContentHash.FromContent(bytes);
        var metadata = imageStorage.GetMetadata(bytes);

        logger.LogDebug("Created hash: {@HashDetails}",
            new
            {
                hash.Hex,
                hash.Bucket,
                metadata.ContentType
            });

        var existing = await reimportChecker.CheckIfPreviouslyDeleted(hash, metadata, request.AllowReimportDeleted, bytes);

        if (existing is not null)
        {
            logger.LogInformation("File already exists; exiting");
            return existing;
        }

        await filesystemWriter.WriteOriginal(hash, metadata, bytes);

        await databaseWriter.AddItemToDatabase(item, hash, metadata, request.AutoArchive);

        logger.LogInformation("Sending thumbnail creation request");

        await thumbnailChannel.WriteAsync(new(bytes, hash));

        logger.LogInformation("Import successful");

        if (request.ImportType is ImportType.File)
        {
            await file.OnImportComplete(request, item);
        }

        return new()
        {
            Ok = true
        };
    }
}
