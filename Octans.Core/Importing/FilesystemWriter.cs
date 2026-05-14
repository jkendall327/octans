using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;

namespace Octans.Core.Importing;

public class FilesystemWriter(
    ImageStorage imageStorage,
    IFileSystem fileSystem,
    ILogger<FilesystemWriter> logger)
{
    public async Task WriteOriginal(ContentHash hash, ImageMetadata metadata, byte[] bytes)
    {
        var destination = imageStorage.GetOriginalDestination(hash, metadata);

        logger.LogDebug("Persisting file to {Destination}", destination);

        await fileSystem.File.WriteAllBytesAsync(destination, bytes);
    }
}
