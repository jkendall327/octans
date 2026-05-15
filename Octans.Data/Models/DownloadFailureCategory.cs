namespace Octans.Data.Models;

public enum DownloadFailureCategory
{
    Unknown,
    Http,
    Network,
    Validation,
    Filesystem,
    SizeLimit,
    Authentication
}
