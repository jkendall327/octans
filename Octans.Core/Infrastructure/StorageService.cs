using System.IO.Abstractions;
using Microsoft.Extensions.Options;

namespace Octans.Core.Infrastructure;

public class StorageService(IFileSystem fileSystem, IOptions<GlobalSettings> settings)
{
    private readonly string _appRoot = settings.Value.AppRoot;

    public string GetStorageUsed()
    {
        try
        {
            if (!fileSystem.Directory.Exists(_appRoot))
            {
                return "0 B";
            }

            var directoryInfo = fileSystem.DirectoryInfo.New(_appRoot);
            var size = GetDirectorySize(directoryInfo);
            
            return FormatSize(size);
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }

    private long GetDirectorySize(IDirectoryInfo directory)
    {
        long size = 0;
        
        // Add size of all files
        foreach (var file in directory.GetFiles())
        {
            size += file.Length;
        }
        
        // Add size of all subdirectories
        foreach (var subdir in directory.GetDirectories())
        {
            size += GetDirectorySize(subdir);
        }
        
        return size;
    }

    private string FormatSize(long bytes)
    {
        return FormatFileSize(bytes);
    }
    
    public string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        var counter = 0;
        decimal number = bytes;
        
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        
        return $"{number:n1} {suffixes[counter]}";
    }
}
