using System.Security.Cryptography;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Core.Models.Tagging;

namespace HydrusReplacement.Server;

/// <summary>
/// Handles the importing of resources from local and remote sources.
/// </summary>
public class Importer
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<Importer> _logger;

    public Importer(SubfolderManager subfolderManager, ServerDbContext context, IHttpClientFactory clientFactory, ILogger<Importer> logger)
    {
        _subfolderManager = subfolderManager;
        _context = context;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task ProcessImport(ImportRequest request)
    {
        _logger.LogInformation("Processing import with {ImportCount} items", request.Items.Count);
        
        foreach (var item in request.Items)
        {
            if (item.Source.IsFile)
            {
                await ImportLocalFile(request, item);
            }

            if (item.Source.IsWebUrl())
            {
                await ImportRemoteFile(item);
            }
        }
    }

    private async Task ImportRemoteFile(ImportItem item)
    {
        var url = item.Source.AbsoluteUri;
        
        _logger.LogInformation("Downloading remote file from {RemoteUrl}", url);
        
        var client = _clientFactory.CreateClient();

        var bytes = await client.GetByteArrayAsync(url);

        var hashed = SHA256.HashData(bytes);
        
        // Add the hash to the database.
        var hashItem = new HashItem { Hash = hashed };
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        await _context.SaveChangesAsync();
    }

    private async Task ImportLocalFile(ImportRequest request, ImportItem item)
    {
        _logger.LogInformation("Importing local file with URI {LocalUri}", item.Source);
        
        // Generate hash of the file for unique identification.
        var filepath = item.Source;
            
        var bytes = await File.ReadAllBytesAsync(filepath.AbsolutePath);
        
        var hashed = new HashedBytes(bytes, ItemType.File);

        CopyPhysicalFile(hashed, filepath);

        // Add the hash to the database.
        var hashItem = new HashItem { Hash = hashed.Bytes };
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        await _context.SaveChangesAsync();

        if (request.DeleteAfterImport)
        {
            _logger.LogInformation("Deleting original local file");
            File.Delete(item.Source.AbsolutePath);
        }
    }

    private void CopyPhysicalFile(HashedBytes hashed, Uri filepath)
    {
        var subfolder = _subfolderManager.GetSubfolder(hashed);
        
        _logger.LogInformation("Import item will be persisted to subfolder {Subfolder}", subfolder.AbsolutePath);
        
        // TODO determine the file's MIME and use it here to determine the extension (don't trust the original).
        
        var fileName = hashed.Hexadecimal + Path.GetExtension(filepath.AbsolutePath);
        var destination = Path.Join(subfolder.AbsolutePath, fileName);
        
        // TODO: handle what to do when a file already exists.
        // On filesystem but not in DB, in DB but not in filesystem, etc.
        File.Copy(filepath.AbsolutePath, destination, true);
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