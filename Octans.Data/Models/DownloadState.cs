namespace Octans.Data.Models;

public enum DownloadState
{
    Queued,
    WaitingForBandwidth,
    InProgress,
    Paused,
    Completed,
    Failed,
    Canceled
}