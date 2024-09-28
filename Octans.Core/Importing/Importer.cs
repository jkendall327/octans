using System.IO.Abstractions;
using System.Threading.Channels;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeDetective.InMemory;
using Octans.Core.Downloaders;

namespace Octans.Server;

public enum SourceType
{
    LocalFile,
    WebUrl
}

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public sealed class Importer
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;
    private readonly DownloaderService _downloader;
    private readonly ChannelWriter<ThumbnailCreationRequest> _thumbnailChannel;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<Importer> _logger;

    public Importer(SubfolderManager subfolderManager,
        ServerDbContext context,
        DownloaderService downloader,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        IFileSystem fileSystem,
        ILogger<Importer> logger)
    {
        _subfolderManager = subfolderManager;
        _context = context;
        _logger = logger;
        _downloader = downloader;
        _thumbnailChannel = thumbnailChannel;
        _fileSystem = fileSystem;
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
        
        var sourceType = GetSourceType(item);
        
        _logger.LogDebug("Source {RawSource} determined to be {SourceType}", item.Source, sourceType);
        
        var bytes = await GetRawBytes(item, sourceType);
        
        _logger.LogDebug("Total size: {SizeInBytes}", bytes.Length);

        var filterResult = await ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            _logger.LogInformation("File rejected by import filters");
            return filterResult;
        }
        
        var hashed = HashedBytes.FromUnhashed(bytes);

        _logger.LogDebug("Created hash: {@HashDetails}", new { hashed.Hexadecimal, hashed.Bucket, hashed.MimeType });
        
        var existing = await CheckIfPreviouslyDeleted(hashed, request.AllowReimportDeleted);

        if (existing is not null)
        {
            _logger.LogInformation("File already exists; exiting");
            return existing;
        }

        var destination = GetDestination(hashed, bytes);
        
        _logger.LogInformation("Writing bytes to disk");
        await _fileSystem.File.WriteAllBytesAsync(destination, bytes);
            
        await AddItemToDatabase(item, hashed);

        if (request.DeleteAfterImport && sourceType is SourceType.LocalFile)
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

    private SourceType GetSourceType(ImportItem item)
    {
        if (item.Source.IsFile)
        {
            return SourceType.LocalFile;
        }

        if (item.Source.IsWebUrl())
        {
            return SourceType.WebUrl;
        }
        
        throw new InvalidOperationException("File source not recognised");
    }

    private async Task<byte[]> GetRawBytes(ImportItem item, SourceType sourceType)
    {
        if (sourceType is SourceType.LocalFile)
        {
            var filepath = item.Source;
     
            _logger.LogInformation("Importing local file from {LocalUri}", filepath);

            var bytes = await _fileSystem.File.ReadAllBytesAsync(filepath.AbsolutePath);

            return bytes;
        }

        if (sourceType is SourceType.WebUrl)
        {
            return await _downloader.Download(item.Source);
        }

        throw new InvalidOperationException("File source not recognised");
    }

    private async Task<ImportItemResult?> CheckIfPreviouslyDeleted(HashedBytes hashed, bool allowReimportDeleted)
    {
        var existingHash = await _context.Hashes
            .FirstOrDefaultAsync(h => h.Hash == hashed.Bytes);

        if (existingHash == null) return null;
        
        if (existingHash.IsDeleted() && !allowReimportDeleted)
        {
            return new()
            {
                Ok = false,
                Message = "Image was previously deleted and reimport is not allowed"
            };
        }

        existingHash.DeletedAt = null;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Reactivated previously deleted hash: {HashId}", existingHash.Id);

        // TODO: still need to copy the actual content back in if it's not there.
        return new()
        {
            Ok = true,
            Message = "Previously deleted image has been reimported"
        };
    }
    
    private async Task<ImportItemResult?> ApplyFilters(ImportRequest request, byte[] bytes)
    {
        if (request.FilterData is null)
        {
            return null;
        }
        
        var filters = new List<IImportFilter>
        {
            new FilesizeFilter(),
            new FiletypeFilter(),
            new ResolutionFilter()
        };

        foreach (var filter in filters)
        {
            var result = await filter.PassesFilter(request.FilterData, bytes);

            _logger.LogDebug("{FilterName} result: {FilterResult}", filter.GetType().Name, result);
            
            if (!result)
            {
                return new()
                {
                    Ok = false,
                    Message = $"Failed {filter.GetType().Name} filter"
                };
            }
        }

        return null;
    }

    private string GetDestination(HashedBytes hashed, byte[] originalBytes)
    {
        var fileType = originalBytes.DetectMimeType();
        
        var fileName = string.Concat(hashed.Hexadecimal, '.', fileType.Extension);

        var subfolder = _subfolderManager.GetSubfolder(hashed);

        var destination = _fileSystem.Path.Join(subfolder.AbsolutePath, fileName);
        
        _logger.LogDebug("File will be persisted to {Destination}", destination);
        
        return destination;
    }
    
    private async Task AddItemToDatabase(ImportItem item, HashedBytes hashed)
    {
        var hashItem = new HashItem { Hash = hashed.Bytes };
        
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        _logger.LogInformation("Persisting item to database");
        
        await _context.SaveChangesAsync();
    }

    private void AddTags(ImportItem request, HashItem hashItem)
    {
        var tags = request.Tags;

        if (tags is null)
        {
            return;
        }
        
        // TODO: does this work when a namespace/subtag already exists?
        // Upserts in EF Core?
        
        foreach (var tag in tags)
        {
            var tagDto = new Tag
            {
                Namespace = new() { Value = tag.Namespace ?? string.Empty },
                Subtag = new() { Value = tag.Subtag }
            };

            _context.Tags.Add(tagDto);
            _context.Mappings.Add(new() { Tag = tagDto, Hash = hashItem });
        }
    }
}