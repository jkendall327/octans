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
public sealed class SimpleImporter(
    IDownloadService downloadService,
    IRobustFileWriter fileWriter,
    IFileSystem fileSystem,
    ILogger<SimpleImporter> logger) : IRawByteProvider
{
    public async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var url = item.Url ??
                  throw new ArgumentException(
                      "Import item had a null URL, despite having an Import Type of RawUrl.",
                      nameof(item));
        using var destination = fileWriter.CreateTemporaryFile(
            fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "octans-imports"),
            GetFileName(url));

        logger.LogInformation("Downloading remote file from {RemoteUrl} to {DestinationPath}", url, destination.Path);

        var result = await downloadService.QueueDownloadAndWaitAsync(new()
        {
            Url = url,
            DestinationPath = destination.Path,
            SourceType = nameof(ImportType.RawUrl),
            SourceId = url.ToString()
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
