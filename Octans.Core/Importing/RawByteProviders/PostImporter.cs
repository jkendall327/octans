using System.IO;
using Octans.Core.Downloaders;
using Octans.Core.Downloads;

namespace Octans.Core.Importing;

public class PostImporter(
    DownloaderService downloaderService,
    IDownloadService downloadService)
{
    public async Task<ImportItemResult> Import(ImportItem item)
    {
        var uri = item.Url ?? throw new ArgumentException("Item had a null URL.", nameof(item));

        var urls = await downloaderService.ResolveAsync(uri);

        foreach (var direct in urls)
        {
            var destination = Path.Combine(Path.GetTempPath(), Path.GetFileName(direct.LocalPath));

            await downloadService.QueueDownloadAsync(new()
            {
                Url = direct,
                DestinationPath = destination
            });
        }

        return urls.Count > 0
            ? new ImportItemResult { Ok = true }
            : new ImportItemResult { Ok = false, Message = "No downloadable URLs found." };
    }
}

