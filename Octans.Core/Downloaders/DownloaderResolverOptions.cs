namespace Octans.Core.Downloaders;

public class DownloaderResolverOptions
{
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public long MaxResponseBytes { get; set; } = 5L * 1024 * 1024;
}
