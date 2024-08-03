using System.Security.Cryptography;
using HydrusReplacement.Server.Models;
using HydrusReplacement.Server.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace HydrusReplacement.Server;

public class FileService
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;
    private readonly IHttpClientFactory _clientFactory;

    public FileService(SubfolderManager subfolderManager, ServerDbContext context, IHttpClientFactory clientFactory)
    {
        _subfolderManager = subfolderManager;
        _context = context;
        _clientFactory = clientFactory;
    }

    public async Task ProcessImport(ImportRequest request)
    {
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
        var client = _clientFactory.CreateClient();

        var bytes = await client.GetByteArrayAsync(item.Source.AbsoluteUri);

        var hashed = SHA256.HashData(bytes);
        
        // Add the hash to the database.
        var hashItem = new HashItem { Hash = hashed };
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        await _context.SaveChangesAsync();
    }

    private async Task ImportLocalFile(ImportRequest request, ImportItem item)
    {
        // Generate hash of the file for unique identification.
        var filepath = item.Source;
            
        var bytes = await File.ReadAllBytesAsync(filepath.AbsolutePath);
        var hashed = SHA256.HashData(bytes);

        CopyPhysicalFile(hashed, filepath);

        // Add the hash to the database.
        var hashItem = new HashItem { Hash = hashed };
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        await _context.SaveChangesAsync();

        if (request.DeleteAfterImport)
        {
            File.Delete(item.Source.AbsolutePath);
        }
    }

    private void CopyPhysicalFile(byte[] hashed, Uri filepath)
    {
        var subfolder = _subfolderManager.GetSubfolder(hashed);
        
        // TODO determine the file's MIME and use it here to determine the extension (don't trust the original).
        
        var fileName = Convert.ToHexString(hashed) + Path.GetExtension(filepath.AbsolutePath);
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
            // Tags without namespaces are implemented merely as tags with an empty namespace.
            var split = tag.Split(':');
            var @namespace = split.Length is 2 ? split.First() : string.Empty;
            var subtag = split.Last();
            
            var tagDto = new Tag
            {
                Namespace = new Namespace { Value = @namespace },
                Subtag = new Subtag { Value = subtag }
            };

            _context.Tags.Add(tagDto);
            _context.Mappings.Add(new Mapping { Tag = tagDto, Hash = hashItem });
        }
    }

    public async Task<string?> GetFile(int id)
    {
        var hash = await _context.FindAsync<HashItem>(id);

        if (hash is null)
        {
            return null;
        }
                
        var hex = Convert.ToHexString(hash.Hash);
                
        var subfolder = _subfolderManager.GetSubfolder(hash.Hash);

        return Directory
            .EnumerateFiles(subfolder.AbsolutePath)
            .SingleOrDefault(x => x.Contains(hex));
    }

    public async Task<List<HashItem>?> GetFilesByTagQuery(IEnumerable<Tag> tags)
    {
        var found = _context.Tags
            .Where(t => 
                tags.Any(tag =>
                tag.Namespace.Value == t.Namespace.Value && tag.Subtag.Value == t.Namespace.Value));

        var query = 
            from mapping in _context.Mappings
            join tag in found on mapping.Tag equals tag
            select mapping.Hash;
        
        return await query.ToListAsync();
    }
}