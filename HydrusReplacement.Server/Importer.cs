using System.IO.Abstractions;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Importing;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Core.Models.Tagging;
using MimeDetective.InMemory;

namespace HydrusReplacement.Server;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public class Importer
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;
    private readonly IHttpClientFactory _clientFactory;
    private readonly IFile _file;
    private readonly IPath _path;
    private readonly ILogger<Importer> _logger;

    public Importer(SubfolderManager subfolderManager,
        ServerDbContext context,
        IHttpClientFactory clientFactory,
        IFile file,
        IPath path,
        ILogger<Importer> logger)
    {
        _subfolderManager = subfolderManager;
        _context = context;
        _clientFactory = clientFactory;
        _logger = logger;
        _file = file;
        _path = path;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request)
    {
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

        var filterResult = await ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            return filterResult;
        }

        var hashed = new HashedBytes(bytes, ItemType.File);

        await AddItemToDatabase(item, hashed);

        var destination = GetDestination(hashed, bytes);
        
        await _file.WriteAllBytesAsync(destination, bytes);

        return new()
        {
            Ok = true
        };
    }

    private async Task<ImportItemResult?> ApplyFilters(ImportRequest request, byte[] bytes)
    {
        var filters = new List<IImportFilter>
        {
            new FilesizeFilter(),
            new FiletypeFilter(),
            new ResolutionFilter()
        };

        foreach (var filter in filters)
        {
            var result = await filter.PassesFilter(request, bytes);

            if (!result)
            {
                return new()
                {
                    Ok = false,
                    Error = $"Failed {filter.GetType().Name} filter"
                };
            }
        }

        return null;
    }

    private async Task<ImportItemResult> ImportLocalFile(ImportRequest request, ImportItem item)
    {
        var filepath = item.Source;
     
        _logger.LogInformation("Importing local file from {LocalUri}", filepath);

        var bytes = await _file.ReadAllBytesAsync(filepath.AbsolutePath);
        
        var filterResult = await ApplyFilters(request, bytes);

        if (filterResult is not null)
        {
            return filterResult;
        }
        
        var hashed = new HashedBytes(bytes, ItemType.File);

        var destination = GetDestination(hashed, bytes);
        
        _file.Copy(filepath.AbsolutePath, destination, true);

        await AddItemToDatabase(item, hashed);

        if (request.DeleteAfterImport)
        {
            _logger.LogInformation("Deleting original local file");
            _file.Delete(item.Source.AbsolutePath);
        }
        
        return new()
        {
            Ok = true
        };
    }

    private string GetDestination(HashedBytes hashed, byte[] originalBytes)
    {
        var fileType = originalBytes.DetectMimeType();
        
        var fileName = string.Join(hashed.Hexadecimal, '.', fileType.Extension);

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