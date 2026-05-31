using Octans.Core.Http;

namespace Octans.Tests.Infrastructure;

internal sealed class FakeDownloadDiskSpaceGuard : IDownloadDiskSpaceGuard
{
    public long? FailWhenBytesNeededAtLeast { get; set; }
    public bool FailNextCheck { get; set; }
    public int CheckCount { get; private set; }

    public void EnsureSufficientSpace(string destinationPath, long bytesNeeded)
    {
        CheckCount++;

        if (FailNextCheck)
        {
            FailNextCheck = false;
            throw new DownloadDiskSpaceException("Insufficient free space for download.");
        }

        if (FailWhenBytesNeededAtLeast is { } threshold && bytesNeeded >= threshold)
        {
            throw new DownloadDiskSpaceException("Insufficient free space for download.");
        }
    }
}
