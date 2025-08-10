using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;
using Octans.Server;

namespace Octans.Core.Importing;

public class PostImporter(
    ImportFilterService filterService,
    ReimportChecker reimportChecker,
    DownloaderService downloaderService,
    DatabaseWriter databaseWriter,
    FilesystemWriter filesystemWriter,
    ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
    ILogger<PostImporter> logger) : Importer(filterService,
    reimportChecker,
    databaseWriter,
    filesystemWriter,
    thumbnailChannel,
    logger)
{
    protected override async Task<byte[]> GetRawBytes(ImportItem item)
    {
        return await downloaderService.Download(item.Source);
    }
}