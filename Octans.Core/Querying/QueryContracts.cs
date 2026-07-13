using Octans.Data.Models;

namespace Octans.Core.Querying;

public sealed record FileQueryRequest(
    IReadOnlyList<string> Predicates,
    int Offset = 0,
    int Limit = 100);

public sealed record FileQueryServicePage(
    IReadOnlyList<HashItem> Items,
    int Total,
    int Offset,
    int Limit);
