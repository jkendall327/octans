namespace Octans.Core.Http.Models;

/// <summary>
/// Opaque handle returned to callers that need to poll for a download's terminal result.
/// </summary>
public readonly record struct DownloadJobHandle(Guid Id);
