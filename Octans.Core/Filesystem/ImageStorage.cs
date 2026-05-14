using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using MimeDetective.InMemory;

namespace Octans.Core.Filesystem;

public sealed record ImageMetadata(string Extension, string ContentType);

public class ImageStorage(IOptions<GlobalSettings> settings, IFileSystem fileSystem)
{
    private const string Hexadecimal = "0123456789abcdef";

    private readonly string _filesRoot = fileSystem.Path.Join(settings.Value.AppRoot, "db", "files");

    public void EnsureStorage()
    {
        var root = fileSystem.DirectoryInfo.New(_filesRoot);

        foreach (var bucket in GetBuckets())
        {
            root.CreateSubdirectory("f" + bucket);
            root.CreateSubdirectory("t" + bucket);
        }

        var downloadersPath = fileSystem.Path.Join(settings.Value.AppRoot, "downloaders");
        fileSystem.Directory.CreateDirectory(downloadersPath);
    }

    public ImageMetadata GetMetadata(byte[] bytes)
    {
        var fileType = bytes.DetectMimeType();
        var extension = NormalizeExtension(fileType.Extension);

        return new(extension, fileType.Mime);
    }

    public string GetOriginalDestination(ContentHash hash, ImageMetadata metadata)
    {
        return fileSystem.Path.Join(GetOriginalBucketPath(hash), GetOriginalFileName(hash, metadata.Extension));
    }

    public string GetThumbnailDestination(ContentHash hash)
    {
        return fileSystem.Path.Join(GetThumbnailBucketPath(hash), hash.Hex + ".jpeg");
    }

    public IFileInfo? FindOriginal(ContentHash hash, string? extension = null)
    {
        if (!string.IsNullOrWhiteSpace(extension))
        {
            var path = fileSystem.Path.Join(GetOriginalBucketPath(hash), GetOriginalFileName(hash, extension));
            var file = fileSystem.FileInfo.New(path);

            if (file.Exists)
            {
                return file;
            }
        }

        return FindByHash(GetOriginalBucketPath(hash), hash);
    }

    public IFileInfo? FindThumbnail(ContentHash hash)
    {
        return FindByHash(GetThumbnailBucketPath(hash), hash);
    }

    public void DeleteOriginal(ContentHash hash, string? extension = null)
    {
        var file = FindOriginal(hash, extension);

        if (file?.Exists == true)
        {
            file.Delete();
        }
    }

    public void DeleteThumbnail(ContentHash hash)
    {
        var file = FindThumbnail(hash);

        if (file?.Exists == true)
        {
            file.Delete();
        }
    }

    private string GetOriginalBucketPath(ContentHash hash)
    {
        return fileSystem.Path.Join(_filesRoot, hash.ContentBucket);
    }

    private string GetThumbnailBucketPath(ContentHash hash)
    {
        return fileSystem.Path.Join(_filesRoot, hash.ThumbnailBucket);
    }

    private IFileInfo? FindByHash(string bucketPath, ContentHash hash)
    {
        if (!fileSystem.Directory.Exists(bucketPath))
        {
            return null;
        }

        return fileSystem.DirectoryInfo.New(bucketPath)
            .EnumerateFiles()
            .FirstOrDefault(f =>
            {
                var name = fileSystem.Path.GetFileNameWithoutExtension(f.Name);
                return string.Equals(name, hash.Hex, StringComparison.OrdinalIgnoreCase);
            });
    }

    private string GetOriginalFileName(ContentHash hash, string extension)
    {
        return hash.Hex + "." + NormalizeExtension(extension);
    }

    private static string NormalizeExtension(string extension)
    {
        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static IEnumerable<string> GetBuckets()
    {
        return
            from a in Hexadecimal
            from b in Hexadecimal
            select string.Concat(a, b);
    }
}
