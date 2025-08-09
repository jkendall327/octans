using Octans.Server;

namespace Octans.Core.Importing;

public class ImportRouter(SimpleImporter simpleImporter, FileImporter fileImporter, PostImporter postImporter)
{
    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        return request.ImportType switch
        {
            ImportType.File => await fileImporter.ProcessImport(request, cancellationToken),
            ImportType.Post => await postImporter.ProcessImport(request, cancellationToken),
            ImportType.RawUrl => await simpleImporter.ProcessImport(request, cancellationToken),
            var _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown import type.")
        };
    }
}