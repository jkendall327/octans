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
        var filepath = item.Source;

        logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await fileSystem.File.ReadAllBytesAsync(filepath.AbsolutePath);

        return bytes;
    }

    public Task OnImportComplete(ImportRequest request, ImportItem item)
    {
        if (request.DeleteAfterImport)
        {
            logger.LogInformation("Deleting original local file");
            fileSystem.File.Delete(item.Source.AbsolutePath);
        }

        return Task.CompletedTask;
    }
}