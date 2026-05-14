using System.Diagnostics.CodeAnalysis;
using Octans.Data.Models;

namespace Octans.Core.Repositories;

public sealed record RepositoryChangeRequest([SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")] string Hash, RepositoryType Destination);
