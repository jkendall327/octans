using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Octans.Core;

public class SubfolderManager
{
    private const string Hexadecimal = "0123456789abcdef";

    private readonly string _hashFolderPath;

    private readonly IDirectoryInfoFactory _directory;
    private readonly IFileInfoFactory _fileInfoFactory;
    private readonly IPath _path;

    public SubfolderManager(IOptions<GlobalSettings> settings, IDirectoryInfoFactory directory, IPath path, IFileInfoFactory fileInfoFactory)
    {
        _directory = directory;
        _path = path;
        _fileInfoFactory = fileInfoFactory;

        _hashFolderPath = _path.Join(settings.Value.AppRoot, "db", "files");
    }

    public void MakeSubfolders()
    {
        // Perform a Cartesian join, i.e. get every permutation of chars in the string.
        var query = 
            from a in Hexadecimal
            from b in Hexadecimal
            select string.Concat(a, b);

        var permutations = query.ToList();

        var root = _directory.New(_hashFolderPath);
        
        foreach (var permutation in permutations)
        {
            var fileDirectory = string.Concat("f", permutation);
            var thumbnailDirectory = string.Concat("t", permutation);
            
            root.CreateSubdirectory(fileDirectory);
            root.CreateSubdirectory(thumbnailDirectory);
        }
    }
    
    public Uri GetSubfolder(HashedBytes hashed)
    {
        var path = _path.Join(_hashFolderPath, hashed.Bucket);
        
        return new(path);
    }

    public IFileInfo? GetFilepath(HashedBytes hashed)
    {
        var path = _path.Join(_hashFolderPath, hashed.ContentLocation);

        return _fileInfoFactory.New(path);
    }
}