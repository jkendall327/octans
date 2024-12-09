using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using MimeDetective.InMemory;

namespace Octans.Core;

public class SubfolderManager
{
    private const string Hexadecimal = "0123456789abcdef";

    private readonly string _hashFolderPath;

    private readonly IOptions<GlobalSettings> _settings;
    private readonly IFileSystem _fileSystem;

    public SubfolderManager(IOptions<GlobalSettings> settings, IFileSystem fileSystem)
    {
        _settings = settings;
        _fileSystem = fileSystem;

        _hashFolderPath = _fileSystem.Path.Join(settings.Value.AppRoot, "db", "files");
    }

    public void MakeSubfolders()
    {
        // Perform a Cartesian join, i.e. get every permutation of chars in the string.
        var query =
            from a in Hexadecimal
            from b in Hexadecimal
            select string.Concat(a, b);

        var permutations = query.ToList();

        var root = _fileSystem.DirectoryInfo.New(_hashFolderPath);

        foreach (var permutation in permutations)
        {
            var fileDirectory = string.Concat("f", permutation);
            var thumbnailDirectory = string.Concat("t", permutation);

            root.CreateSubdirectory(fileDirectory);
            root.CreateSubdirectory(thumbnailDirectory);
        }

        var path = _fileSystem.Path.Join(_settings.Value.AppRoot, "downloaders");
        _fileSystem.Directory.CreateDirectory(path);
    }

    public string GetDestination(HashedBytes hashed, byte[] originalBytes)
    {
        var fileType = originalBytes.DetectMimeType();

        var fileName = string.Concat(hashed.Hexadecimal, '.', fileType.Extension);

        var subfolder = GetSubfolder(hashed);

        var destination = _fileSystem.Path.Join(subfolder.AbsolutePath, fileName);

        return destination;
    }

    public Uri GetSubfolder(HashedBytes hashed)
    {
        var path = _fileSystem.Path.Join(_hashFolderPath, hashed.ContentBucket);

        return new(path);
    }

    public IFileSystemInfo? GetFilepath(HashedBytes hashed)
    {
        var subfolder = GetSubfolder(hashed);

        return _fileSystem.DirectoryInfo.New(subfolder.AbsolutePath)
            .EnumerateFileSystemInfos()
            .FirstOrDefault(f =>
            {
                var name = _fileSystem.Path.GetFileNameWithoutExtension(f.Name);
                return name == hashed.Hexadecimal;
            });
    }

    public IFileSystemInfo? GetThumbnail(HashedBytes hashed)
    {
        var path = _fileSystem.Path.Join(_hashFolderPath, hashed.ThumbnailBucket);

        return _fileSystem.DirectoryInfo.New(path)
            .EnumerateFileSystemInfos()
            .FirstOrDefault(f =>
            {
                var name = _fileSystem.Path.GetFileNameWithoutExtension(f.Name);
                return name == hashed.Hexadecimal;
            });
    }
}