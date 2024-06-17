using System.Security.Cryptography;
using HydrusReplacement.Server.Models;
using HydrusReplacement.Server.Models.Tagging;

namespace HydrusReplacement.Server;

public class FileService
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;

    public FileService(SubfolderManager subfolderManager, ServerDbContext context)
    {
        _subfolderManager = subfolderManager;
        _context = context;
    }

    public async Task ImportFile(ImportRequest request)
    {
        // Generate hash of the file for unique identification.
        var filepath = request.SourceLocation;
        var bytes = await File.ReadAllBytesAsync(filepath.AbsolutePath);
        var hashed = SHA256.HashData(bytes);

        CopyPhysicalFile(hashed, filepath);

        // Add the hash to the database.
        var record = new HashItem { Hash = hashed };
        _context.Hashes.Add(record);

        AddTags(request);

        await _context.SaveChangesAsync();
    }

    private void CopyPhysicalFile(byte[] hashed, Uri filepath)
    {
        var subfolder = _subfolderManager.GetSubfolder(hashed);
        
        Directory.CreateDirectory(SubfolderManager.HashFolderPath);
        
        // TODO determine the file's MIME and use it here to determine the extension (don't trust the original).
        
        var fileName = Convert.ToHexString(hashed) + Path.GetExtension(filepath.AbsolutePath);
        var destination = Path.Join(subfolder.AbsolutePath, fileName);
        File.Copy(filepath.AbsolutePath, destination);
    }

    private void AddTags(ImportRequest request)
    {
        var tags = request.Tags;

        // TODO: does this work when a namespace/subtag already exists?
        // Upserts in EF Core?
        
        foreach (var tag in tags)
        {
            var split = tag.Split(':');
            
            var @namespace = split.FirstOrDefault();
            var subtag = split.Last();
            
            var namespaceDto = new Namespace
            {
                // Tags without namespaces are implemented merely as tags with an empty namespace.
                Value = @namespace ?? string.Empty
            };

            var subtagDto = new Subtag
            {
                Value = subtag
            };

            var tagDto = new Tag
            {
                Namespace = namespaceDto,
                Subtag = subtagDto
            };

            _context.Tags.Add(tagDto);
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
}