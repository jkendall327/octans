using Microsoft.Extensions.Logging;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Importing;

public class DatabaseWriter(ServerDbContext context, ILogger<DatabaseWriter> logger)
{
    public async Task AddItemToDatabase(ImportItem item, HashedBytes hashed)
    {
        var hashItem = new HashItem { Hash = hashed.Bytes };

        context.Hashes.Add(hashItem);

        AddTags(item, hashItem);

        logger.LogInformation("Persisting item to database");

        await context.SaveChangesAsync();
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

            context.Tags.Add(tagDto);
            context.Mappings.Add(new() { Tag = tagDto, Hash = hashItem });
        }
    }
}