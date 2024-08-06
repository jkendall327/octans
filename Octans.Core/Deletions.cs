namespace Octans.Core;

public record DeleteRequest(IEnumerable<DeleteItem> Items);
public record DeleteItem(int Id);
public record DeleteResult(int Id, bool Success, string? Error);
public record DeleteResponse(List<DeleteResult> Results);