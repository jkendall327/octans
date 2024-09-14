namespace Octans.Core;

public class QueryParser
{
    public Task<SearchRequest> Parse(IEnumerable<string> query)
    {
        var searchRequest = new SearchRequest();

        foreach (var se in query)
        {
            if (se is "whatever")
            {
                searchRequest.TagsToInclude.Add(se);
            }
        }

        return Task.FromResult(searchRequest);
    }
}