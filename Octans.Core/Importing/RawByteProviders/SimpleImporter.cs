using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Octans.Core.Http;
using Octans.Core.Http.Models;
using Octans.Data.Models;

namespace Octans.Core.Importing.RawByteProviders;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public sealed class SimpleImporter(
    IDownloadService downloadService,
    IFileSystem fileSystem,
    ILogger<SimpleImporter> logger) : IRawByteProvider
{
    public async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var url = item.Url ??
                  throw new ArgumentException(
                      "Import item had a null URL, despite having an Import Type of RawUrl.",
                      nameof(item));
        var destination = BuildTempDestination(url);

        logger.LogInformation("Downloading remote file from {RemoteUrl} to {DestinationPath}", url, destination);

        try
        {
            var result = await downloadService.QueueDownloadAndWaitAsync(new()
            {
                Url = url,
                DestinationPath = destination,
                SourceType = nameof(ImportType.RawUrl),
                SourceId = url.ToString()
            });

            if (result.Outcome is not DownloadTerminalOutcome.Completed)
            {
                throw new InvalidOperationException(GetFailureMessage(result));
            }

            return await fileSystem.File.ReadAllBytesAsync(destination);
        }
        finally
        {
            DeleteTempFileBestEffort(destination);
        }
    }

    private string BuildTempDestination(Uri url)
    {
        var fileName = fileSystem.Path.GetFileName(url.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        return fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "octans-imports",
            $"{Guid.NewGuid():N}-{fileName}");
    }

    private void DeleteTempFileBestEffort(string path)
    {
        try
        {
            if (fileSystem.File.Exists(path))
            {
                fileSystem.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temporary raw URL download {DestinationPath}", path);
        }
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
