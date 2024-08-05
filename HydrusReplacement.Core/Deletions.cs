namespace HydrusReplacement.Core;

public record DeleteRequest(Guid DeleteId, IEnumerable<DeleteItem> Items);
public record DeleteItem(int Id);
public record DeleteResult(int Id, bool Success, string? Error);
public record DeleteResponse(Guid DeleteId, IEnumerable<DeleteResult> Results);