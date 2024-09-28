using Octans.Server;

namespace Octans.Core.Importing;

public class ImportRouter
{
    private readonly SimpleImporter _simpleImporter;

    public ImportRouter(SimpleImporter simpleImporter)
    {
        _simpleImporter = simpleImporter;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        return await _simpleImporter.ProcessImport(request, cancellationToken);
    }
}