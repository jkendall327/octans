using System.Threading.Channels;
using Octans.Core.Importing;
using Microsoft.Extensions.Logging;

namespace Octans.Server;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public sealed class SimpleImporter : Importer
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<SimpleImporter> _logger;

    public SimpleImporter(ImportFilterService filterService,
        IHttpClientFactory clientFactory,
        ReimportChecker reimportChecker,
        DatabaseWriter databaseWriter,
        FilesystemWriter filesystemWriter,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        ILogger<SimpleImporter> logger) : base(filterService,
        reimportChecker,
        databaseWriter,
        filesystemWriter,
        thumbnailChannel,
        logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    protected override async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var url = item.Source.AbsoluteUri;

        _logger.LogInformation("Downloading remote file from {RemoteUrl}", url);

#pragma warning disable CA2000
        var client = _clientFactory.CreateClient();
#pragma warning restore CA2000

        var bytes = await client.GetByteArrayAsync(item.Source);

        return bytes;
    }
}