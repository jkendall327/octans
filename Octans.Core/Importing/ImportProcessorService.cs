using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Core.Models;
using Octans.Core.Progress;
using Octans.Core.Importing.Jobs;

namespace Octans.Core.Importing;

public class ImportProcessorService(
    IServiceProvider serviceProvider,
    ILogger<ImportProcessorService> logger,
    IBackgroundProgressReporter progress) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Import processor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                var importer = scope.ServiceProvider.GetRequiredService<IImporter>();

                var job = await context.ImportJobs
                    .Include(j => j.Items)
                    .FirstOrDefaultAsync(j => j.Status == ImportJobStatus.Queued, stoppingToken);

                if (job != null)
                {
                    logger.LogInformation("Processing import job {JobId}", job.Id);
                    job.Status = ImportJobStatus.InProgress;
                    await context.SaveChangesAsync(stoppingToken);

                    var request = JsonSerializer.Deserialize<ImportRequest>(job.SerializedRequest);
                    if (request is null)
                    {
                        logger.LogError("Failed to deserialize import request for job {JobId}", job.Id);
                        job.Status = ImportJobStatus.Failed;
                        await context.SaveChangesAsync(stoppingToken);
                    }
                    else
                    {
                        var handle = await progress.Start("Import", request.Items.Count);
                        var result = await importer.ProcessImport(request, handle.Id, stoppingToken);

                        for (var i = 0; i < job.Items.Count && i < result.Results.Count; i++)
                        {
                            var item = job.Items[i];
                            var itemResult = result.Results[i];
                            item.Status = itemResult.Ok ? ImportItemStatus.Completed : ImportItemStatus.Failed;
                            item.Error = itemResult.Ok ? null : itemResult.Message;
                        }

                        job.Status = ImportJobStatus.Completed;
                        await progress.Complete(handle.Id);
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while processing import job");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
