using System.Security.Cryptography;
using HydrusReplacement.Server.Models;

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

    public async Task ImportFile(Uri filepath)
    {
        var bytes = await File.ReadAllBytesAsync(filepath.AbsolutePath);
        var hashed = SHA256.HashData(bytes);

        var subfolder = _subfolderManager.GetSubfolder(hashed);
        
        Directory.CreateDirectory(SubfolderManager.HashFolderPath);
        
        // TODO determine the file's MIME and use it here to determine the extension (don't trust the original).
        
        var fileName = Convert.ToHexString(hashed) + Path.GetExtension(filepath.AbsolutePath);
        var destination = Path.Join(subfolder.AbsolutePath, fileName);
        File.Copy(filepath.AbsolutePath, destination);
        
        var record = new HashItem { Hash = hashed };

        _context.Hashes.Add(record);
        await _context.SaveChangesAsync();
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