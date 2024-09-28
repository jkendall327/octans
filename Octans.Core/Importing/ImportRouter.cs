using Octans.Server;

namespace Octans.Core.Importing;

public class ImportRouter
{
    private readonly SimpleImporter _simpleImporter;
    private readonly FileImporter _fileImporter;
    private readonly PostImporter _postImporter;

    public ImportRouter(SimpleImporter simpleImporter, FileImporter fileImporter, PostImporter postImporter)
    {
        _simpleImporter = simpleImporter;
        _fileImporter = fileImporter;
        _postImporter = postImporter;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        return request.ImportType switch
        {
            ImportType.File => await _fileImporter.ProcessImport(request, cancellationToken),
            ImportType.Post => await _postImporter.ProcessImport(request, cancellationToken),
            ImportType.RawUrl => await _simpleImporter.ProcessImport(request, cancellationToken),
            var _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown import type.")
        };
    }
}