using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Server;

namespace Octans.Core.Importing;

public class FileImporter(
    ImportFilterService filterService,
    ReimportChecker reimportChecker,
    DatabaseWriter databaseWriter,
    FilesystemWriter filesystemWriter,
    ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
    IFileSystem fileSystem,
    ILogger<FileImporter> logger) : Importer(filterService,
    reimportChecker,
    databaseWriter,
    filesystemWriter,
    thumbnailChannel,
    logger)
{
    protected override async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var filepath = item.Source;

        logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await fileSystem.File.ReadAllBytesAsync(filepath.AbsolutePath);

        return bytes;
    }

    protected override Task OnImportComplete(ImportRequest request, ImportItem item)
    {
        if (request.DeleteAfterImport)
        {
            logger.LogInformation("Deleting original local file");
            fileSystem.File.Delete(item.Source.AbsolutePath);
        }

        return Task.CompletedTask;
    }
}