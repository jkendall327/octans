using Microsoft.EntityFrameworkCore;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

public class TagUpdater
{
    private readonly ServerDbContext _context;

    public TagUpdater(ServerDbContext context)
    {
        _context = context;
    }

    public async Task<bool> UpdateTags(UpdateTagsRequest request)
    {
        var hash = await _context.Hashes.FindAsync(request.HashId);

        if (hash == null)
        {
            return false;
        }

        await RemoveTags(request.TagsToRemove, hash);
        await AddTags(request.TagsToAdd, hash);

        await _context.SaveChangesAsync();

        return true;
    }

    private async Task RemoveTags(IEnumerable<TagModel> tagsToRemove, HashItem hash)
    {
        var all = _context.Mappings
            .Include(m => m.Tag)
            .ThenInclude(t => t.Namespace)
            .Include(m => m.Tag)
            .ThenInclude(t => t.Subtag);

        // TODO: this is probably bad when we have lots of mappings for a given hash.
        // But trying to do it against an IQueryable makes EF Core explode as it can't translate it.
        var forThisHash = await all.Where(m => m.Hash.Id == hash.Id).ToListAsync();

        var mappingsToRemove = forThisHash.Where(m =>
        {
            return tagsToRemove.Any(t =>
            {
                var namespacesMatch =
                    (t.Namespace == null && string.IsNullOrEmpty(m.Tag.Namespace.Value)) ||
                    (t.Namespace != null && m.Tag.Namespace.Value == t.Namespace);

                return namespacesMatch && m.Tag.Subtag.Value == t.Subtag;
            });
        });

        _context.Mappings.RemoveRange(mappingsToRemove);
    }

    private async Task AddTags(IEnumerable<TagModel> tagsToAdd, HashItem hash)
    {
        foreach (var tagModel in tagsToAdd)
        {
            var @namespace = await _context.Namespaces
                .FirstOrDefaultAsync(n => n.Value == (tagModel.Namespace ?? ""))
                ?? new Namespace { Value = tagModel.Namespace ?? "" };

            var subtag = await _context.Subtags
                .FirstOrDefaultAsync(s => s.Value == tagModel.Subtag)
                ?? new Subtag { Value = tagModel.Subtag };

            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.Namespace == @namespace && t.Subtag == subtag);

            if (tag == null)
            {
                tag = new()
                {
                    Namespace = @namespace,
                    Subtag = subtag
                };

                _context.Tags.Add(tag);
            }

            var exists = await _context.Mappings.AnyAsync(m => m.Hash == hash && m.Tag == tag);

            if (!exists)
            {
                _context.Mappings.Add(new()
                {
                    Hash = hash,
                    Tag = tag
                });
            }
        }
    }
}