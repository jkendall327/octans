using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Core.Importing.RawByteProviders;
using Octans.Server;

namespace Octans.Core.Importing;

public class FileImporter(IFileSystem fileSystem, ILogger<FileImporter> logger) : IRawByteProvider
{
    public async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var filepath = item.Filepath ??
                       throw new ArgumentException(
                           "Import item had a null filepath, despite having an Import Type of File.",
                           nameof(item));

        logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await fileSystem.File.ReadAllBytesAsync(filepath);

        return bytes;
    }

    public Task OnImportComplete(ImportRequest request, ImportItem item)
    {
        if (request.DeleteAfterImport)
        {
            var filepath = item.Filepath ??
                           throw new ArgumentException(
                               "Import item had a null filepath, despite having an Import Type of File.",
                               nameof(item));

            logger.LogInformation("Deleting original local file");
            fileSystem.File.Delete(filepath);
        }

        return Task.CompletedTask;
    }
}