using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Importing;

public class FilesystemWriter(
    SubfolderManager subfolderManager,
    IFileSystem fileSystem,
    ILogger<FilesystemWriter> logger)
{
    public async Task CopyBytesToSubfolder(HashedBytes hashed, byte[] bytes)
    {
        var destination = subfolderManager.GetDestination(hashed, bytes);

        logger.LogDebug("Persisting file to {Destination}", destination);

        await fileSystem.File.WriteAllBytesAsync(destination, bytes);
    }
}