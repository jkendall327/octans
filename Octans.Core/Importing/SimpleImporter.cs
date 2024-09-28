using System.IO.Abstractions;
using System.Threading.Channels;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeDetective.InMemory;

namespace Octans.Server;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public sealed class SimpleImporter
{
    private readonly DatabaseWriter _databaseWriter;
    private readonly FilesystemWriter _filesystemWriter;
    private readonly ReimportChecker _reimportChecker;
    private readonly ImportFilterService _importFilterService;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ChannelWriter<ThumbnailCreationRequest> _thumbnailChannel;
    private readonly ILogger<SimpleImporter> _logger;

    public SimpleImporter(DatabaseWriter databaseWriter,
        FilesystemWriter filesystemWriter,
        ReimportChecker reimportChecker,
        ImportFilterService importFilterService,
        IHttpClientFactory clientFactory,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        ILogger<SimpleImporter> logger)
    {
        _databaseWriter = databaseWriter;
        _filesystemWriter = filesystemWriter;
        _reimportChecker = reimportChecker;
        _importFilterService = importFilterService;
        _clientFactory = clientFactory;
        _thumbnailChannel = thumbnailChannel;
        _logger = logger;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ImportId"] = request.ImportId
        });

        _logger.LogInformation("Processing import with {ImportCount} items", request.Items.Count);

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

        var filterResult = await _importFilterService.ApplyFilters(request, bytes);

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

        await _thumbnailChannel.WriteAsync(new()
        {
            Bytes = bytes,
            Hashed = hashed
        });

        _logger.LogInformation("Import successful");

        var result = new ImportItemResult
        {
            Ok = true
        };

        return result;
    }

    private async Task<byte[]> GetRawBytes(ImportItem item)
    {
        var url = item.Source.AbsoluteUri;

        _logger.LogInformation("Downloading remote file from {RemoteUrl}", url);

        var client = _clientFactory.CreateClient();

        var bytes = await client.GetByteArrayAsync(url);

        return bytes;
    }
}