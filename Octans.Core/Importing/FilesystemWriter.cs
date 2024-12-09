using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Importing;

public class FilesystemWriter
{
    private readonly SubfolderManager _subfolderManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FilesystemWriter> _logger;

    public FilesystemWriter(SubfolderManager subfolderManager, IFileSystem fileSystem, ILogger<FilesystemWriter> logger)
    {
        _subfolderManager = subfolderManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task CopyBytesToSubfolder(HashedBytes hashed, byte[] bytes)
    {
        var destination = _subfolderManager.GetDestination(hashed, bytes);

        _logger.LogDebug("Persisting file to {Destination}", destination);

        await _fileSystem.File.WriteAllBytesAsync(destination, bytes);
    }
}