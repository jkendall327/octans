using System.Diagnostics.CodeAnalysis;

namespace Octans.Core.Repositories;

public sealed record RepositoryChangeRequest([SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")] string Hash, RepositoryType Destination);
