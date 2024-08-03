using Microsoft.Extensions.Configuration;

namespace HydrusReplacement.Core;

public class SubfolderManager
{
    private const string Hexadecimal = "0123456789abcdef";

    private string HashFolderPath => Path.Join(_config.GetValue<string>("DatabaseRoot"), "db", "files");

    private readonly IConfiguration _config;

    public SubfolderManager(IConfiguration config)
    {
        _config = config;
    }

    public void MakeSubfolders()
    {
        // Perform a Cartesian join, i.e. get every permutation of chars in the string.
        var query = 
            from a in Hexadecimal
            from b in Hexadecimal
            select string.Concat(a, b);

        var permutations = query.ToList();

        var root = new DirectoryInfo(HashFolderPath);
        
        foreach (var permutation in permutations)
        {
            var fileDirectory = string.Concat("f", permutation);
            var thumbnailDirectory = string.Concat("t", permutation);
            
            root.CreateSubdirectory(fileDirectory);
            root.CreateSubdirectory(thumbnailDirectory);
        }
    }

    public Uri GetSubfolder(byte[] hashed)
    {
        var hex = Convert.ToHexString(hashed);
        var tag = hex[..2].ToLowerInvariant();
        var bucket = string.Concat("f", tag);
        
        // TODO: do not assume it's a file (might be thumbnail)
        
        var path = Path.Join(HashFolderPath, bucket);
        
        return new(path);
    }

    public FileInfo? GetFilepath(byte[] hashed)
    {
        var subfolder = GetSubfolder(hashed);
        
        var hex = Convert.ToHexString(hashed);

        return new DirectoryInfo(subfolder.AbsolutePath)
            .EnumerateFiles()
            .SingleOrDefault(f => f.Name.Replace(f.Extension, string.Empty) == hex);
    }
}