using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Server;

namespace Octans.Core.Importing;

public interface IImporter
{
    Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default);
}

public class Importer(
    ImportFilterService filterService,
    ReimportChecker reimportChecker,
    DatabaseWriter databaseWriter,
    FilesystemWriter filesystemWriter,
    ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
    FileImporter file,
    PostImporter post,
    SimpleImporter simple,
    ILogger<Importer> logger) : IImporter
{
    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        var results = new List<ImportItemResult>();

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await ImportIndividualItem(request, item);
                results.Add(result);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Exception during file import");

                results.Add(new()
                {
                    Ok = false,
                    Message = e.Message
                });
            }
        }

        return new(request.ImportId, results);
    }

    private async Task<ImportItemResult> ImportIndividualItem(ImportRequest request, ImportItem item)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ItemImportId"] = Guid.NewGuid(),
        });

        if (item.Url is not null && item.Filepath is not null)
        {
            throw new ArgumentException("Import item had both a URL and a filepath specified.", nameof(item));
        }

        var task = request.ImportType switch
        {
            ImportType.File => file.GetRawBytes(item),
            ImportType.RawUrl => simple.GetRawBytes(item),
            ImportType.Post => post.GetRawBytes(item),
            ImportType.Gallery => throw new NotImplementedException(),
            ImportType.Watchable => throw new NotImplementedException(),
            _ => throw new ArgumentOutOfRangeException()
        };

        var bytes = await task;

        logger.LogDebug("Total size: {SizeInBytes}", bytes.Length);

        var filterResult = await filterService.ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            return filterResult;
        }

        var hashed = HashedBytes.FromUnhashed(bytes);

        logger.LogDebug("Created hash: {@HashDetails}",
            new
            {
                hashed.Hexadecimal,
                hashed.Bucket,
                hashed.MimeType
            });

        var existing = await reimportChecker.CheckIfPreviouslyDeleted(hashed, request.AllowReimportDeleted);

        if (existing is not null)
        {
            logger.LogInformation("File already exists; exiting");
            return existing;
        }

        await filesystemWriter.CopyBytesToSubfolder(hashed, bytes);

        await databaseWriter.AddItemToDatabase(item, hashed);

        logger.LogInformation("Sending thumbnail creation request");

        await thumbnailChannel.WriteAsync(new(bytes, hashed));

        logger.LogInformation("Import successful");

        if (request.ImportType is ImportType.File)
        {
            await file.OnImportComplete(request, item);
        }

        var result = new ImportItemResult
        {
            Ok = true
        };

        return result;
    }
}