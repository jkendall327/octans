namespace HydrusReplacement.Server;

public class SubfolderManager
{
    private const string Hexadecimal = "0123456789abcdef";

    public static string HashFolderPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "db", "files");
    
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
}