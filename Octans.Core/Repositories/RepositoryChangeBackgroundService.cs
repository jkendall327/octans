using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Core.Progress;
using Octans.Data.Models;

namespace Octans.Core.Repositories;

internal sealed class RepositoryChangeBackgroundService(
    ChannelReader<RepositoryChangeRequest> channel,
    RepositoryChangeProcessor processor) : BackgroundService
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<RepositoryChangeRequest>(BatchSize);

        await foreach (var change in channel.ReadAllAsync(stoppingToken))
        {
            buffer.Add(change);

            if (buffer.Count >= BatchSize)
            {
                await processor.ProcessBatch(buffer, stoppingToken);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await processor.ProcessBatch(buffer, stoppingToken);
        }
    }
}

internal sealed class RepositoryChangeProcessor(
    IDbContextFactory<ServerDbContext> contextFactory,
    IBackgroundProgressReporter progressReporter,
    ILogger<RepositoryChangeProcessor> logger)
{
    public async Task ProcessBatch(IReadOnlyCollection<RepositoryChangeRequest> batch, CancellationToken token = default)
    {
        var handle = await progressReporter.Start("Repository changes", batch.Count);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(token);

            var processed = 0;
            foreach (var req in batch)
            {
                var bytes = Convert.FromHexString(req.Hash);
                var hashItem = await db.Hashes.FirstOrDefaultAsync(h => h.Hash == bytes, token);
                if (hashItem is null)
                {
                    continue;
                }

                hashItem.RepositoryId = MapRepositoryId(req.Destination);
                processed++;
                await progressReporter.Report(handle.Id, processed);
            }

            await db.SaveChangesAsync(token);
            await progressReporter.Complete(handle.Id);
        }
        catch (OperationCanceledException)
        {
            await progressReporter.Complete(handle.Id);
            // Swallow
        }
        catch (Exception ex)
        {
            await progressReporter.Complete(handle.Id);
            logger.LogError(ex, "Error processing repository changes");
        }
    }

    private static int MapRepositoryId(RepositoryDestination destination) =>
        destination switch
        {
            RepositoryDestination.Inbox => (int)RepositoryType.Inbox,
            RepositoryDestination.Archive => (int)RepositoryType.Archive,
            RepositoryDestination.Trash => (int)RepositoryType.Trash,
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, "Unknown repository destination.")
        };
}
