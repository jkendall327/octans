namespace Octans.Core.Downloaders;

public class DownloaderResolverOptions
{
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
