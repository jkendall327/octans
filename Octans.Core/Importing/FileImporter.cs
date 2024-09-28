using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeDetective.InMemory;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Server;

namespace Octans.Core.Importing;

public class FileImporter
{
    private readonly ILogger<FileImporter> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly ChannelWriter<ThumbnailCreationRequest> _thumbnailChannel;
    private readonly SubfolderManager _subfolderManager;
    private readonly DatabaseImporter _databaseImporter;
    private readonly ServerDbContext _context;
    private readonly ImportFilterService _filterService;
    private readonly ReimportChecker _reimportChecker;

    public FileImporter(ILogger<FileImporter> logger,
        IFileSystem fileSystem,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        SubfolderManager subfolderManager,
        ServerDbContext context,
        ReimportChecker reimportChecker,
        DatabaseImporter databaseImporter,
        ImportFilterService filterService)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _thumbnailChannel = thumbnailChannel;
        _subfolderManager = subfolderManager;
        _context = context;
        _reimportChecker = reimportChecker;
        _databaseImporter = databaseImporter;
        _filterService = filterService;
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
            _logger.LogInformation("File rejected by import filters");
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

        var destination = _subfolderManager.GetDestination(hashed, bytes);

        _logger.LogDebug("File will be persisted to {Destination}", destination);

        _logger.LogInformation("Writing bytes to disk");
        
        await _fileSystem.File.WriteAllBytesAsync(destination, bytes);

        await _databaseImporter.AddItemToDatabase(item, hashed);

        if (request.DeleteAfterImport)
        {
            _logger.LogInformation("Deleting original local file");
            _fileSystem.File.Delete(item.Source.AbsolutePath);
        }

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
        var filepath = item.Source;

        _logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await _fileSystem.File.ReadAllBytesAsync(filepath.AbsolutePath);

        return bytes;
    }
}