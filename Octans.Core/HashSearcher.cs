using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core;

public class SearchRequest
{
    public List<int> SystemPredicates { get; set; }
    public List<int> OrPredicates { get; set; }
    public List<TagModel> TagsToInclude { get; set; }
    public List<int> TagsToExclude { get; set; }
    public List<int> NamespacesToInclude { get; set; }
    public List<int> NamespacesToExclude { get; set; }
    public List<int> WildcardsToInclude { get; set; }
    public List<int> WildcardsToExclude { get; set; }
}

public class HashSearcher
{
    private readonly ServerDbContext _context;

    public HashSearcher(ServerDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<HashItem>> Search(SearchRequest request, CancellationToken token = default)
    {
        
        return new();
    }

    private async Task<List<Namespace>> GetNamespacesFromWildcard(string wildcard, CancellationToken token = default)
    {
        if (wildcard is "*")
        {
            return await _context.Namespaces.ToListAsync(token);
        }

        if (wildcard.Contains("*"))
        {
            var clean = wildcard.Replace("*", "");
            return await _context.Namespaces.Where(n => n.Value.Contains(clean)).ToListAsync(token);
        }

        var exact = await _context.Namespaces.FirstOrDefaultAsync(n => n.Value == wildcard, token);

        return exact is null ? [] : [exact];
    }
}