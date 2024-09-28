namespace Octans.Core;

public record DeleteResult(int Id, bool Success, string? Error);
public record DeleteResponse(List<DeleteResult> Results);