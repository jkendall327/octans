using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Server;

namespace Octans.Core.Importing;

public abstract class Importer(
    ImportFilterService filterService,
    ReimportChecker reimportChecker,
    DatabaseWriter databaseWriter,
    FilesystemWriter filesystemWriter,
    ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
    ILogger<Importer> logger)
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

        var bytes = await GetRawBytes(item);

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

        await OnImportComplete(request, item);

        var result = new ImportItemResult
        {
            Ok = true
        };

        return result;
    }

    protected abstract Task<byte[]> GetRawBytes(ImportItem item);

    protected virtual Task OnImportComplete(ImportRequest request, ImportItem item) => Task.CompletedTask;
}