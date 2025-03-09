namespace Octans.Core.Downloaders;

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