namespace Octans.Core.Progress;

public readonly record struct ProgressHandle(Guid Id, string Operation, int TotalItems);
