namespace Octans.Core;

public record DeleteRequest(IEnumerable<int> Items);
public record DeleteResult(int Id, bool Success, string? Error);
public record DeleteResponse(List<DeleteResult> Results);