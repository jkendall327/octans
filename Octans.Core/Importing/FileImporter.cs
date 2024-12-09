using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Server;

namespace Octans.Core.Importing;

public class FileImporter : Importer
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FileImporter> _logger;

    public FileImporter(ImportFilterService filterService,
        ReimportChecker reimportChecker,
        DatabaseWriter databaseWriter,
        FilesystemWriter filesystemWriter,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        IFileSystem fileSystem,
        ILogger<FileImporter> logger) : base(filterService,
        reimportChecker,
        databaseWriter,
        filesystemWriter,
        thumbnailChannel,
        logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    protected override async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var filepath = item.Source;

        _logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await _fileSystem.File.ReadAllBytesAsync(filepath.AbsolutePath);

        return bytes;
    }

    protected override Task OnImportComplete(ImportRequest request, ImportItem item)
    {
        if (request.DeleteAfterImport)
        {
            _logger.LogInformation("Deleting original local file");
            _fileSystem.File.Delete(item.Source.AbsolutePath);
        }

        return Task.CompletedTask;
    }
}