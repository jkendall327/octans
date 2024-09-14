using System.IO.Abstractions;
using System.Threading.Channels;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;
using MimeDetective.InMemory;

namespace Octans.Server;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public class Importer
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ChannelWriter<ThumbnailCreationRequest> _thumbnailChannel;
    private readonly IFile _file;
    private readonly IPath _path;
    private readonly ILogger<Importer> _logger;

    public Importer(SubfolderManager subfolderManager,
        ServerDbContext context,
        IHttpClientFactory clientFactory,
        IFile file,
        IPath path,
        ChannelWriter<ThumbnailCreationRequest> thumbnailChannel,
        ILogger<Importer> logger)
    {
        _subfolderManager = subfolderManager;
        _context = context;
        _clientFactory = clientFactory;
        _logger = logger;
        _thumbnailChannel = thumbnailChannel;
        _file = file;
        _path = path;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ImportId"] = request.ImportId
        });
        
        _logger.LogInformation("Processing import with {ImportCount} items", request.Items.Count);

        var results = new List<ImportItemResult>();
        
        foreach (var item in request.Items)
        {
            if (item.Source.IsFile)
            {
                var result = await ImportLocalFile(request, item);
                results.Add(result);
            }

            if (item.Source.IsWebUrl())
            {
                var result = await ImportRemoteFile(request, item);
                results.Add(result);
            }
        }

        return new(request.ImportId, results);
    }

    private async Task<ImportItemResult> ImportRemoteFile(ImportRequest request, ImportItem item)
    {
        var url = item.Source.AbsoluteUri;
        
        _logger.LogInformation("Downloading remote file from {RemoteUrl}", url);
        
        var client = _clientFactory.CreateClient();

        var bytes = await client.GetByteArrayAsync(url);

        _logger.LogDebug("Downloaded {SizeInBytes} total bytes", bytes.Length);

        var filterResult = await ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            _logger.LogInformation("File rejected by import filters");
            return filterResult;
        }

        var hashed = HashedBytes.FromUnhashed(bytes);

        var existing = await CheckIfPreviouslyDeleted(hashed, request.AllowReimportDeleted);

        if (existing is not null)
        {
            _logger.LogInformation("File already exists; exiting");
            return existing;
        }
        
        await AddItemToDatabase(item, hashed);

        var destination = GetDestination(hashed, bytes);
        
        await _file.WriteAllBytesAsync(destination, bytes);

        await _thumbnailChannel.WriteAsync(new()
        {
            Bytes = bytes,
            Hashed = hashed
        });
        
        return new()
        {
            Ok = true
        };
    }

    private async Task<ImportItemResult> ImportLocalFile(ImportRequest request, ImportItem item)
    {
        var filepath = item.Source;
     
        _logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await _file.ReadAllBytesAsync(filepath.AbsolutePath);
        
        _logger.LogDebug("Total file size: {SizeInBytes}", bytes.Length);
        
        var filterResult = await ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            _logger.LogInformation("File rejected by import filters");
            return filterResult;
        }
        
        var hashed = HashedBytes.FromUnhashed(bytes);

        var existing = await CheckIfPreviouslyDeleted(hashed, request.AllowReimportDeleted);

        if (existing is not null)
        {
            _logger.LogInformation("File already exists; exiting");
            return existing;
        }
        
        var destination = GetDestination(hashed, bytes);
        
        _file.Copy(filepath.AbsolutePath, destination, true);

        await AddItemToDatabase(item, hashed);

        if (request.DeleteAfterImport)
        {
            _logger.LogInformation("Deleting original local file");
            _file.Delete(item.Source.AbsolutePath);
        }
        
        await _thumbnailChannel.WriteAsync(new()
        {
            Bytes = bytes,
            Hashed = hashed
        });
        
        return new()
        {
            Ok = true,
            Message = "Image imported"
        };
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

        // Reactivate the previously deleted hash
        existingHash.DeletedAt = null;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Reactivated previously deleted hash: {HashId}", existingHash.Id);

        // Would still need to copy the actual content back in if it's not there.
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

        var destination = _path.Join(subfolder.AbsolutePath, fileName);
        
        _logger.LogInformation("Import item will be persisted to subfolder {Subfolder}", subfolder.AbsolutePath);

        return destination;
    }
    
    private async Task AddItemToDatabase(ImportItem item, HashedBytes hashed)
    {
        var hashItem = new HashItem { Hash = hashed.Bytes };
        
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

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