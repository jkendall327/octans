using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;
using Octans.Server;

namespace Octans.Core.Importing;

public class PostImporter : Importer
{
    private readonly DownloaderService _downloaderService;

    public PostImporter(ImportFilterService filterService,
        ReimportChecker reimportChecker,
        DownloaderService downloaderService,
        DatabaseWriter databaseWriter,
        FilesystemWriter filesystemWriter,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        ILogger<PostImporter> logger) : base(filterService,
        reimportChecker,
        databaseWriter,
        filesystemWriter,
        thumbnailChannel,
        logger)
    {
        _downloaderService = downloaderService;
    }

    protected override async Task<byte[]> GetRawBytes(ImportItem item)
    {
        return await _downloaderService.Download(item.Source);
    }
}