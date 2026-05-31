using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;

namespace Octans.Core.Importing;

internal sealed class FilesystemWriter(
    ImageStorage imageStorage,
    IRobustFileWriter fileWriter,
    ILogger<FilesystemWriter> logger)
{
    public async Task WriteOriginal(ContentHash hash, ImageMetadata metadata, byte[] bytes)
    {
        var destination = imageStorage.GetOriginalDestination(hash, metadata);

        logger.LogDebug("Persisting file to {Destination}", destination);

        await fileWriter.WriteAllBytesAsync(destination, bytes);
    }
}
