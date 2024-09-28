using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;
using Octans.Server;

namespace Octans.Core.Importing;

public class PostImporter
{
    private readonly DownloaderService _service;
    private readonly DatabaseWriter _databaseWriter;
    private readonly FilesystemWriter _filesystemWriter;
    private readonly ReimportChecker _reimportChecker;
    private readonly ImportFilterService _importFilterService;
    private readonly ChannelWriter<ThumbnailCreationRequest> _thumbnailChannel;
    private readonly ILogger<PostImporter> _logger;

    public PostImporter(DownloaderService service,
        DatabaseWriter databaseWriter,
        FilesystemWriter filesystemWriter,
        ReimportChecker reimportChecker,
        ImportFilterService importFilterService,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        ILogger<PostImporter> logger)
    {
        _service = service;
        _databaseWriter = databaseWriter;
        _filesystemWriter = filesystemWriter;
        _reimportChecker = reimportChecker;
        _importFilterService = importFilterService;
        _thumbnailChannel = thumbnailChannel;
        _logger = logger;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        foreach (var item in request.Items)
        {
            var bytes = await _service.Download(item.Source);

            var hashed = HashedBytes.FromUnhashed(bytes);

            var filter = await _importFilterService.ApplyFilters(request, bytes);

            if (filter != null)
            {
                throw new NotImplementedException();
            }

            var already = await _reimportChecker.CheckIfPreviouslyDeleted(hashed, request.AllowReimportDeleted);

            if (already is not null)
            {
                throw new NotImplementedException();
            }
            
            await _databaseWriter.AddItemToDatabase(item, hashed);
            await _filesystemWriter.CopyBytesToSubfolder(hashed, bytes);
            
            await _thumbnailChannel.WriteAsync(new()
            {
                Bytes = bytes,
                Hashed = hashed
            },
            cancellationToken);

            _logger.LogInformation("Import successful");

            var result = new ImportItemResult
            {
                Ok = true
            };

            throw new NotImplementedException();
        }

        throw new NotImplementedException();
    }
}