using Octans.Core.Importing;
using Microsoft.Extensions.Logging;
using Octans.Core.Importing.RawByteProviders;

namespace Octans.Server;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public sealed class SimpleImporter(
    IHttpClientFactory clientFactory,
    ILogger<SimpleImporter> logger) : IRawByteProvider
{
    public async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var url = item.Source.AbsoluteUri;

        logger.LogInformation("Downloading remote file from {RemoteUrl}", url);

#pragma warning disable CA2000
        var client = clientFactory.CreateClient();
#pragma warning restore CA2000

        var bytes = await client.GetByteArrayAsync(item.Source);

        return bytes;
    }
}