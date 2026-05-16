using System.IO.Abstractions;
using Octans.Core.Downloaders;
using Octans.Core.Http;

namespace Octans.Core.Importing.RawByteProviders;

public class PostImporter(
    DownloaderService downloaderService,
    IDownloadService downloadService,
    IFileSystem fileSystem)
{
    public async Task<ImportItemResult> Import(ImportItem item)
    {
        var uri = item.Url ?? throw new ArgumentException("Item had a null URL.", nameof(item));

        var urls = await downloaderService.ResolveAsync(uri);

        foreach (var direct in urls)
        {
            var destination = fileSystem.Path.Combine(
                fileSystem.Path.GetTempPath(),
                fileSystem.Path.GetFileName(direct.LocalPath));

            await downloadService.QueueDownloadAsync(new()
            {
                Url = direct,
                DestinationPath = destination,
                SourceType = nameof(ImportType.Post),
                SourceId = uri.ToString()
            });
        }

        return urls.Count > 0
            ? new ImportItemResult { Ok = true }
            : new ImportItemResult { Ok = false, Message = "No downloadable URLs found." };
    }
}
