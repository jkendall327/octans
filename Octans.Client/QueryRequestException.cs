using Octans.Core.Querying;

namespace Octans.Client;

public sealed class QueryRequestException : Exception
{
    public QueryRequestException()
    {
    }

    public QueryRequestException(string message) : base(message)
    {
    }

    public QueryRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public QueryRequestException(IReadOnlyList<QueryError> errors)
        : base(errors.Count > 0 ? errors[0].Message : "The query is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyList<QueryError> Errors { get; } = [];
}
