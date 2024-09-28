using Microsoft.Extensions.Logging;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Importing;

public class DatabaseWriter
{
    private readonly ServerDbContext _context;
    private readonly ILogger<DatabaseWriter> _logger;

    public DatabaseWriter(ServerDbContext context, ILogger<DatabaseWriter> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task AddItemToDatabase(ImportItem item, HashedBytes hashed)
    {
        var hashItem = new HashItem { Hash = hashed.Bytes };
        
        _context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        _logger.LogInformation("Persisting item to database");
        
        await _context.SaveChangesAsync();
    }
    
    private void AddTags(ImportItem request, HashItem hashItem)
    {
        var tags = request.Tags;

        if (tags is null)
        {
            return;
        }
        
        // TODO: does this work when a namespace/subtag already exists?
        // Upserts in EF Core?
        
        foreach (var tag in tags)
        {
            var tagDto = new Tag
            {
                Namespace = new() { Value = tag.Namespace ?? string.Empty },
                Subtag = new() { Value = tag.Subtag }
            };

            _context.Tags.Add(tagDto);
            _context.Mappings.Add(new() { Tag = tagDto, Hash = hashItem });
        }
    }
}