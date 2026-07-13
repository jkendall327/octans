using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;
using Octans.Core.Http;
using Octans.Core.Http.Models;
using Octans.Data.Models;

namespace Octans.Core.Importing.RawByteProviders;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
internal sealed class SimpleImporter(
    IDownloadService downloadService,
    IRobustFileWriter fileWriter,
    IFileSystem fileSystem,
    ILogger<SimpleImporter> logger) : IRawByteProvider
{
    public Task<byte[]> GetRawBytes(ImportItem item) => GetRawBytes(
        new ImportRequest
        {
            ImportType = ImportType.RawUrl,
            Items = [item],
            DeleteAfterImport = false
        },
        item);

    public async Task<byte[]> GetRawBytes(ImportRequest request, ImportItem item)
    {
        var url = item.Url ??
                  throw new ArgumentException(
                      "Import item had a null URL, despite having an Import Type of RawUrl.",
                      nameof(item));

        if (item.DownloadId is { } existingDownloadId)
        {
            logger.LogInformation(
                "Waiting for subscription download {DownloadId} for {RemoteUrl}",
                existingDownloadId,
                url);
            var existingResult = await downloadService.WaitForCompletionAsync(existingDownloadId);
            if (existingResult.Outcome is not DownloadTerminalOutcome.Completed)
            {
                throw new InvalidOperationException(GetFailureMessage(existingResult));
            }

            var bytes = await fileSystem.File.ReadAllBytesAsync(existingResult.DestinationPath);
            try
            {
                fileSystem.File.Delete(existingResult.DestinationPath);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not remove completed subscription download file {DestinationPath}", existingResult.DestinationPath);
            }

            return bytes;
        }

        using var destination = fileWriter.CreateTemporaryFile(
            fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "octans-imports"),
            GetFileName(url));

        logger.LogInformation("Downloading remote file from {RemoteUrl} to {DestinationPath}", url, destination.Path);

        var result = await downloadService.QueueDownloadAndWaitAsync(new()
        {
            Url = url,
            DestinationPath = destination.Path,
            SourceType = item.SourceType ?? nameof(ImportType.RawUrl),
            SourceId = item.SourceId ?? url.ToString()
        });

        if (result.Outcome is not DownloadTerminalOutcome.Completed)
        {
            throw new InvalidOperationException(GetFailureMessage(result));
        }

        return await fileSystem.File.ReadAllBytesAsync(destination.Path);
    }

    private string GetFileName(Uri url)
    {
        var fileName = fileSystem.Path.GetFileName(url.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        return fileName;
    }

    private static string GetFailureMessage(DownloadJobResult result)
    {
        var details = result.ValidationMessage ?? result.ErrorMessage;
        if (!string.IsNullOrWhiteSpace(details))
        {
            return $"Raw URL download failed with outcome {result.Outcome}: {details}";
        }

        return $"Raw URL download failed with outcome {result.Outcome}.";
    }
}
