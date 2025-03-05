using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using Octans.Core;

namespace Octans.Server.Services;

public class StorageService
{
    private readonly IFileSystem _fileSystem;
    private readonly string _appRoot;

    public StorageService(
        IFileSystem fileSystem,
        IOptions<GlobalSettings> settings)
    {
        _fileSystem = fileSystem;
        _appRoot = settings.Value.AppRoot;
    }

    public string GetStorageUsed()
    {
        try
        {
            if (!_fileSystem.Directory.Exists(_appRoot))
            {
                return "0 B";
            }

            var directoryInfo = _fileSystem.DirectoryInfo.New(_appRoot);
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

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int counter = 0;
        decimal number = bytes;
        
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        
        return $"{number:n1} {suffixes[counter]}";
    }
}
