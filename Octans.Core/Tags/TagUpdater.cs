using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Tags;

public enum TagUpdateResult
{
    HashNotFound,
    TagsUpdated
}

public class TagUpdater(ServerDbContext context)
{
    public async Task<TagUpdateResult> UpdateTags(UpdateTagsRequest request)
    {
        var hash = await context.Hashes.FindAsync(request.HashId);

        if (hash == null)
        {
            return TagUpdateResult.HashNotFound;
        }

        await RemoveTags(request.TagsToRemove, hash);
        await AddTags(request.TagsToAdd, hash);

        await context.SaveChangesAsync();

        return TagUpdateResult.TagsUpdated;
    }

    private async Task RemoveTags(IEnumerable<TagModel> tagsToRemove, HashItem hash)
    {
        var tagsList = tagsToRemove.ToList();
        if (tagsList.Count == 0)
        {
            return;
        }

        var parameter = Expression.Parameter(typeof(Mapping), "m");

        // m.Hash.Id == hash.Id
        var hashIdProperty = Expression.Property(Expression.Property(parameter, nameof(Mapping.Hash)), nameof(HashItem.Id));
        var hashIdValue = Expression.Constant(hash.Id);
        var hashCheck = Expression.Equal(hashIdProperty, hashIdValue);

        Expression? tagCheck = null;

        foreach (var tag in tagsList)
        {
            var nsValue = tag.Namespace ?? string.Empty;
            var subValue = tag.Subtag;

            // m.Tag.Namespace.Value == nsValue
            var tagProp = Expression.Property(parameter, nameof(Mapping.Tag));
            var nsProp = Expression.Property(Expression.Property(tagProp, nameof(Tag.Namespace)), nameof(Namespace.Value));
            var subProp = Expression.Property(Expression.Property(tagProp, nameof(Tag.Subtag)), nameof(Subtag.Value));

            var nsCheck = Expression.Equal(nsProp, Expression.Constant(nsValue));
            var subCheck = Expression.Equal(subProp, Expression.Constant(subValue));

            var combined = Expression.AndAlso(nsCheck, subCheck);

            tagCheck = tagCheck == null ? combined : Expression.OrElse(tagCheck, combined);
        }

        if (tagCheck == null)
        {
            return;
        }

        var finalBody = Expression.AndAlso(hashCheck, tagCheck);
        var lambda = Expression.Lambda<Func<Mapping, bool>>(finalBody, parameter);

        var mappingsToRemove = await context.Mappings
            .Where(lambda)
            .ToListAsync();

        context.Mappings.RemoveRange(mappingsToRemove);
    }

    private async Task AddTags(IEnumerable<TagModel> tagsToAdd, HashItem hash)
    {
        foreach (var tagModel in tagsToAdd)
        {
            var @namespace = await context.Namespaces
                                 .FirstOrDefaultAsync(n => n.Value == (tagModel.Namespace ?? ""))
                             ?? new Namespace { Value = tagModel.Namespace ?? "" };

            var subtag = await context.Subtags
                             .FirstOrDefaultAsync(s => s.Value == tagModel.Subtag)
                         ?? new Subtag { Value = tagModel.Subtag };

            var tag = await context.Tags
                .FirstOrDefaultAsync(t => t.Namespace == @namespace && t.Subtag == subtag);

            if (tag == null)
            {
                tag = new()
                {
                    Namespace = @namespace,
                    Subtag = subtag
                };

                context.Tags.Add(tag);
            }

            var exists = await context.Mappings.AnyAsync(m => m.Hash == hash && m.Tag == tag);

            if (!exists)
            {
                context.Mappings.Add(new()
                {
                    Hash = hash,
                    Tag = tag
                });
            }
        }
    }
}