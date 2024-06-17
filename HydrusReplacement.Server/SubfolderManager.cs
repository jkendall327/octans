namespace HydrusReplacement.Server;

public class SubfolderManager
{
    private const string Hexadecimal = "0123456789abcdef";

    public string HashFolderPath => Path.Join(AppDomain.CurrentDomain.BaseDirectory, "db", "files");
    
    public void MakeSubfolders()
    {
        var query = 
            from c in Hexadecimal
            from d in Hexadecimal
            select new string(c, d);

        var permutations = query.ToList();

        var root = HashFolderPath;
        
        foreach (var permutation in permutations)
        {
            var filePath = Path.Join(root, "f", permutation);
            var thumbnailPath = Path.Join(root, "t", permutation);

            Directory.CreateDirectory(filePath);
            Directory.CreateDirectory(thumbnailPath);
        }
    }
}