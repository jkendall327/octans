using Microsoft.Extensions.Logging;
using Octans.Core.Progress;

namespace Octans.Core.Importing;

public interface IImporter
{
    Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default);
    Task<ImportResult> ProcessImport(ImportRequest request, Guid progressId, CancellationToken cancellationToken = default);
}

internal sealed class Importer(
    ImportItemProcessor itemProcessor,
    IBackgroundProgressReporter progress,
    ILogger<Importer> logger) : IImporter
{
    public Task<ImportResult> ProcessImport(ImportRequest request, CancellationToken cancellationToken = default) =>
        ProcessImportInternal(request, null, cancellationToken);

    public Task<ImportResult> ProcessImport(ImportRequest request, Guid progressId, CancellationToken cancellationToken = default) =>
        ProcessImportInternal(request, progressId, cancellationToken);

    private async Task<ImportResult> ProcessImportInternal(ImportRequest request, Guid? progressId, CancellationToken cancellationToken)
    {
        var results = new List<ImportItemResult>();
        var processed = 0;

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await itemProcessor.Process(request, item);
                results.Add(result);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Exception during file import");

                results.Add(new()
                {
                    Ok = false,
                    Message = e.Message
                });
            }

            processed++;
            if (progressId.HasValue)
            {
                await progress.Report(progressId.Value, processed);
            }
        }

        return new(request.ImportId, results);
    }
}
