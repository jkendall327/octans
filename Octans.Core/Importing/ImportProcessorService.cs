using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Data.Models;
using DataImportItemStatus = Octans.Data.Models.Importing.ImportItemStatus;
using DataImportJobPhase = Octans.Data.Models.Importing.ImportJobPhase;
using DataImportJobStatus = Octans.Data.Models.Importing.ImportJobStatus;

namespace Octans.Core.Importing;

public class ImportProcessorService(
    IServiceProvider serviceProvider,
    ILogger<ImportProcessorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Import processor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedJob(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
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

    public async Task<bool> ProcessQueuedJob(CancellationToken stoppingToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var itemProcessor = scope.ServiceProvider.GetRequiredService<ImportItemProcessor>();
        var jobService = scope.ServiceProvider.GetRequiredService<ImportJobService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IImportJobNotifier>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var job = await context.ImportJobs
            .Include(j => j.Items)
            .Where(j => j.Status == DataImportJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(stoppingToken);

        if (job is null)
        {
            return false;
        }

        logger.LogInformation("Processing import job {JobId}", job.Id);

        var now = Now(timeProvider);
        job.Status = DataImportJobStatus.Running;
        job.Phase = DataImportJobPhase.Importing;
        job.StartedAt ??= now;
        job.UpdatedAt = now;
        await context.SaveChangesAsync(stoppingToken);
        await notifier.JobChanged(job.Id, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await context.Entry(job).ReloadAsync(stoppingToken);

            if (job.Status is DataImportJobStatus.PauseRequested)
            {
                job.Status = DataImportJobStatus.Paused;
                job.CurrentItem = null;
                job.UpdatedAt = Now(timeProvider);
                await context.SaveChangesAsync(stoppingToken);
                await notifier.JobChanged(job.Id, stoppingToken);
                return true;
            }

            if (job.Status is DataImportJobStatus.CancelRequested)
            {
                await CancelRemaining(context, job.Id, timeProvider, stoppingToken);
                await notifier.JobChanged(job.Id, stoppingToken);
                return true;
            }

            var item = await context.ImportItems
                .Where(i => i.ImportJobId == job.Id && i.Status == DataImportItemStatus.Pending)
                .OrderBy(i => i.CreatedAt)
                .ThenBy(i => i.Id)
                .FirstOrDefaultAsync(stoppingToken);

            if (item is null)
            {
                await CompleteJob(context, job.Id, timeProvider, stoppingToken);
                await notifier.JobChanged(job.Id, stoppingToken);
                return true;
            }

            now = Now(timeProvider);
            item.Status = DataImportItemStatus.Running;
            item.Attempts++;
            item.StartedAt = now;
            item.UpdatedAt = now;
            job.CurrentItem = item.Source;
            job.UpdatedAt = now;
            await context.SaveChangesAsync(stoppingToken);
            await notifier.JobChanged(job.Id, stoppingToken);

            ImportItemResult result;
            try
            {
                var request = jobService.BuildImportRequest(job, item);
                result = await itemProcessor.Process(request, request.Items.Single());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process import item {ItemId} in job {JobId}", item.Id, job.Id);
                result = new()
                {
                    Ok = false,
                    Message = ex.Message
                };
            }

            now = Now(timeProvider);
            item.Status = result.Ok ? DataImportItemStatus.Completed : DataImportItemStatus.Failed;
            item.Error = result.Ok ? null : result.Message;
            item.CompletedAt = now;
            item.UpdatedAt = now;

            job.ProcessedItems++;
            if (!result.Ok)
            {
                job.FailedItems++;
            }

            job.CurrentItem = null;
            job.UpdatedAt = now;
            await context.SaveChangesAsync(stoppingToken);
            await notifier.JobChanged(job.Id, stoppingToken);
        }

        return true;
    }

    private static async Task CancelRemaining(
        ServerDbContext context,
        Guid jobId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs
            .Include(j => j.Items)
            .SingleAsync(j => j.Id == jobId, cancellationToken);
        var now = Now(timeProvider);

        foreach (var item in job.Items.Where(i => i.Status is DataImportItemStatus.Pending))
        {
            item.Status = DataImportItemStatus.Cancelled;
            item.CompletedAt = now;
            item.UpdatedAt = now;
        }

        job.Status = DataImportJobStatus.Cancelled;
        job.CurrentItem = null;
        job.CompletedAt = now;
        job.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task CompleteJob(
        ServerDbContext context,
        Guid jobId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        var now = Now(timeProvider);
        job.Status = DataImportJobStatus.Completed;
        job.CurrentItem = null;
        job.CompletedAt = now;
        job.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static DateTime Now(TimeProvider timeProvider) => timeProvider.GetUtcNow().UtcDateTime;
}
