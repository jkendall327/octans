namespace HydrusReplacement.Server;

public class SubfolderManager
{
    private const string Hexadecimal = "0123456789abcdef";

    public static string HashFolderPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "db", "files");
    
    public void MakeSubfolders()
    {
        var query = 
            from c in Hexadecimal
            from d in Hexadecimal
            select string.Concat(c, d);

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
        var initialBytes = hashed[..2];
        var tag = Convert.ToHexString(initialBytes);
        
        // TODO: do not assume it's a file (might be thumbnail)

        var path = Path.Join(HashFolderPath, "f", tag);
        
        return new(path);
    }
}