using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Octans.Core.Http;
using Octans.Core.Http.Models;

namespace Octans.Tests.Downloads;

public sealed class DownloadDiskSpaceGuardTests
{
    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public void EnsureSufficientSpace_WhenAvailableSpaceIncludesHeadroom_DoesNotThrow()
    {
        _fileSystem.AddDrive("/", new()
        {
            AvailableFreeSpace = 1_500,
            TotalFreeSpace = 1_500,
            TotalSize = 10_000
        });
        var guard = CreateGuard(new()
        {
            DiskSpace = new()
            {
                RequiredFreeSpaceHeadroomBytes = 500
            }
        });

        guard.EnsureSufficientSpace("/downloads/file.bin", 1_000);
    }

    [Fact]
    public void EnsureSufficientSpace_WhenAvailableSpaceDoesNotIncludeHeadroom_Throws()
    {
        _fileSystem.AddDrive("/", new()
        {
            AvailableFreeSpace = 1_499,
            TotalFreeSpace = 1_499,
            TotalSize = 10_000
        });
        var guard = CreateGuard(new()
        {
            DiskSpace = new()
            {
                RequiredFreeSpaceHeadroomBytes = 500
            }
        });

        var ex = Assert.Throws<DownloadDiskSpaceException>(
            () => guard.EnsureSufficientSpace("/downloads/file.bin", 1_000));

        Assert.Contains("Insufficient free space", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1,500 bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1,499 bytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSufficientSpace_WhenDisabled_DoesNotThrow()
    {
        _fileSystem.AddDrive("/", new()
        {
            AvailableFreeSpace = 0,
            TotalFreeSpace = 0,
            TotalSize = 10_000
        });
        var guard = CreateGuard(new()
        {
            DiskSpace = new()
            {
                Enabled = false,
                RequiredFreeSpaceHeadroomBytes = 500
            }
        });

        guard.EnsureSufficientSpace("/downloads/file.bin", 1_000);
    }

    private DownloadDiskSpaceGuard CreateGuard(DownloadManagerOptions options)
    {
        return new(
            _fileSystem,
            Options.Create(options),
            NullLogger<DownloadDiskSpaceGuard>.Instance);
    }
}
