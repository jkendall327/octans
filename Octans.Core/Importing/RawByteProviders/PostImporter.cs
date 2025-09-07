using Octans.Core.Downloaders;
using Octans.Core.Importing.RawByteProviders;

namespace Octans.Core.Importing;

public class PostImporter(DownloaderService downloaderService) : IRawByteProvider
{
    public async Task<byte[]> GetRawBytes(ImportItem item)
    {
        return await downloaderService.Download(item.Url ?? throw new ArgumentException("Item had a null URL.", nameof(item)));
    }
}