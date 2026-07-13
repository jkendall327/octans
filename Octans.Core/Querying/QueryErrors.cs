namespace Octans.Core.Querying;

public sealed record QueryError(
    string Code,
    string Message,
    int PredicateIndex,
    int Start,
    int Length);

public sealed class QueryValidationException : Exception
{
    public QueryValidationException()
    {
    }

    public QueryValidationException(string message) : base(message)
    {
    }

    public QueryValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public QueryValidationException(IReadOnlyList<QueryError> errors) : base(errors.Count > 0 ? errors[0].Message : null)
    {
        Errors = errors;
    }

    public IReadOnlyList<QueryError> Errors { get; } = [];
}
