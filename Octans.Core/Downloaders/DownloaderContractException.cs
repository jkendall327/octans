namespace Octans.Core.Downloaders;

public class DownloaderContractException : Exception
{
    public DownloaderContractException()
    {
    }

    public DownloaderContractException(string message) : base(message)
    {
    }

    public DownloaderContractException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
