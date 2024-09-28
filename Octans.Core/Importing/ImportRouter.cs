using Octans.Server;

namespace Octans.Core.Importing;

public class ImportRouter
{
    private readonly SimpleImporter _simpleImporter;
    private readonly FileImporter _fileImporter;

    public ImportRouter(SimpleImporter simpleImporter, FileImporter fileImporter)
    {
        _simpleImporter = simpleImporter;
        _fileImporter = fileImporter;
    }

    public async Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ImportType is ImportType.File)
        {
            return await _fileImporter.ProcessImport(request, cancellationToken);
        }
        
        return await _simpleImporter.ProcessImport(request, cancellationToken);
    }
}