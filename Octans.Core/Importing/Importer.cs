using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Octans.Server;

namespace Octans.Core.Importing;

public abstract class Importer
{
    private readonly ImportFilterService _filterService;
    private readonly ReimportChecker _reimportChecker;
    private readonly DatabaseWriter _databaseWriter;
    private readonly FilesystemWriter _filesystemWriter;
    private readonly ChannelWriter<ThumbnailCreationRequest> _thumbnailChannel;
    private readonly ILogger<Importer> _logger;

    protected Importer(ImportFilterService filterService,
        ReimportChecker reimportChecker,
        DatabaseWriter databaseWriter,
        FilesystemWriter filesystemWriter,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        ILogger<Importer> logger)
    {
        _filterService = filterService;
        _reimportChecker = reimportChecker;
        _databaseWriter = databaseWriter;
        _filesystemWriter = filesystemWriter;
        _thumbnailChannel = thumbnailChannel;
        _logger = logger;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        var results = new List<ImportItemResult>();

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await ImportIndividualItem(request, item);
                results.Add(result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception during file import");

                results.Add(new()
                {
                    Ok = false,
                    Message = e.Message
                });
            }
        }

        return new(request.ImportId, results);
    }

    private async Task<ImportItemResult> ImportIndividualItem(ImportRequest request, ImportItem item)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ItemImportId"] = Guid.NewGuid(),
        });

        var bytes = await GetRawBytes(item);

        _logger.LogDebug("Total size: {SizeInBytes}", bytes.Length);

        var filterResult = await _filterService.ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            return filterResult;
        }

        var hashed = HashedBytes.FromUnhashed(bytes);

        _logger.LogDebug("Created hash: {@HashDetails}",
            new
            {
                hashed.Hexadecimal,
                hashed.Bucket,
                hashed.MimeType
            });

        var existing = await _reimportChecker.CheckIfPreviouslyDeleted(hashed, request.AllowReimportDeleted);

        if (existing is not null)
        {
            _logger.LogInformation("File already exists; exiting");
            return existing;
        }

        await _filesystemWriter.CopyBytesToSubfolder(hashed, bytes);

        await _databaseWriter.AddItemToDatabase(item, hashed);

        _logger.LogInformation("Sending thumbnail creation request");

        await _thumbnailChannel.WriteAsync(new(bytes, hashed));

        _logger.LogInformation("Import successful");

        await OnImportComplete(request, item);

        var result = new ImportItemResult
        {
            Ok = true
        };

        return result;
    }

    protected abstract Task<byte[]> GetRawBytes(ImportItem item);

    protected virtual Task OnImportComplete(ImportRequest request, ImportItem item) => Task.CompletedTask;
}