using Octans.Core.Downloaders;

namespace Octans.Core.Importing;

public class PostImporter
{
    private readonly DownloaderService _service;
    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}