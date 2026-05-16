using Microsoft.EntityFrameworkCore;
using Octans.Core.Http.Models;
using Octans.Data.Models;

namespace Octans.Core.Http;

/// <summary>
/// Reads durable terminal results for callers that submitted a download job.
/// </summary>
public interface IDownloadJobResultService
{
    Task<DownloadJobResult?> GetResultAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DownloadJobResult?> GetResultAsync(DownloadJobHandle handle, CancellationToken cancellationToken = default);
}

/// <summary>
/// Entity Framework implementation of terminal download result lookup.
/// </summary>
public sealed class DownloadJobResultService(IDbContextFactory<ServerDbContext> contextFactory)
    : IDownloadJobResultService
{
    public async Task<DownloadJobResult?> GetResultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var status = await db.DownloadStatuses
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

        return status is null ? null : DownloadJobResults.FromStatus(status);
    }

    public Task<DownloadJobResult?> GetResultAsync(
        DownloadJobHandle handle,
        CancellationToken cancellationToken = default) =>
        GetResultAsync(handle.Id, cancellationToken);
}

/// <summary>
/// Converts persisted download status rows into public job-result models.
/// </summary>
public static class DownloadJobResults
{
    public static DownloadJobResult? FromStatus(DownloadStatus status)
    {
        if (!IsTerminal(status.State))
        {
            return null;
        }

        return new()
        {
            DownloadId = status.Id,
            Outcome = GetOutcome(status),
            Url = status.Url,
            DestinationPath = status.DestinationPath,
            DisplayName = status.DisplayName,
            SourceType = status.SourceType,
            SourceId = status.SourceId,
            BytesDownloaded = status.BytesDownloaded,
            TotalBytes = status.TotalBytes,
            HttpStatusCode = status.HttpStatusCode,
            ResponseContentType = status.ResponseContentType,
            ResponseETag = status.ResponseETag,
            ResponseLastModified = status.ResponseLastModified,
            FailureCategory = status.FailureCategory,
            ErrorMessage = status.ErrorMessage,
            ValidationMessage = status.ValidationMessage,
            CreatedAt = status.CreatedAt,
            StartedAt = status.StartedAt,
            CompletedAt = status.CompletedAt
        };
    }

    public static bool IsTerminal(DownloadState state) =>
        state is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled;

    private static DownloadTerminalOutcome GetOutcome(DownloadStatus status)
    {
        if (status.TerminalOutcome is { } outcome)
        {
            return outcome;
        }

        return status.State switch
        {
            DownloadState.Completed => DownloadTerminalOutcome.Completed,
            DownloadState.Canceled => DownloadTerminalOutcome.Canceled,
            _ => DownloadTerminalOutcome.Failed
        };
    }
}
