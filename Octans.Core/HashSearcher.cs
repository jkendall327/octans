using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;

namespace Octans.Core;

public class Predicate
{
}

public class HashSearcher
{
    private readonly ServerDbContext _context;

    public HashSearcher(ServerDbContext context)
    {
        _context = context;
    }

    public async Task<List<HashItem>> Search(IList<Predicate> predicates)
    {
        return await _context.Hashes.ToListAsync();
    }
}