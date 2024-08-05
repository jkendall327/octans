using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace HydrusReplacement.Server.Services;

public class TagUpdater
{
    private readonly ServerDbContext _db;

    public TagUpdater(ServerDbContext db)
    {
        _db = db;
    }

    public async Task<bool> UpdateTags(UpdateTagsRequest request)
    {
        var hash = await _db.Hashes.FindAsync(request.HashId);
        
        if (hash is null)
        {
            return false;
        }

        var existingMappings = await _db.Mappings
            .Where(m => m.Hash.Id == request.HashId)
            .Include(m => m.Tag)
            .ThenInclude(t => t.Namespace)
            .Include(m => m.Tag)
            .ThenInclude(t => t.Subtag)
            .ToListAsync();

        RemoveTags(request.TagsToRemove, existingMappings);
        await AddTags(request.TagsToAdd, existingMappings, hash);

        await _db.SaveChangesAsync();

        return true;
    }

    private void RemoveTags(IEnumerable<TagModel> tagsToRemove, List<Mapping> existingMappings)
    {
        var tagsToRemoveSet = tagsToRemove.ToHashSet();
        var mappingsToRemove = existingMappings.Where(m => tagsToRemoveSet.Any(t => 
            t.Namespace == m.Tag.Namespace.Value && t.Subtag == m.Tag.Subtag.Value)).ToList();

        if (mappingsToRemove.Any())
        {
            _db.Mappings.RemoveRange(mappingsToRemove);
        }
    }

    private async Task AddTags(IEnumerable<TagModel> tagsToAdd, List<Mapping> existingMappings, HashItem hash)
    {
        var tagsToAddSet = tagsToAdd.ToHashSet();
        var tagsToCreate = tagsToAddSet.Except(existingMappings.Select(m => new TagModel 
        { 
            Namespace = m.Tag.Namespace.Value, 
            Subtag = m.Tag.Subtag.Value 
        }));

        if (tagsToCreate.Any())
        {
            // ?????
            var allNamespaces = await _db.Namespaces.ToDictionaryAsync(n => n.Value, n => n);
            var allSubtags = await _db.Subtags.ToDictionaryAsync(s => s.Value, s => s);

            var newMappings = new List<Mapping>();

            foreach (var tag in tagsToCreate)
            {
                var (namespaceEntity, subtagEntity) = GetOrCreateNamespaceAndSubtag(tag, allNamespaces, allSubtags);
                var tagEntity = await GetOrCreateTag(namespaceEntity, subtagEntity);

                newMappings.Add(new Mapping { Tag = tagEntity, Hash = hash });
            }

            _db.Mappings.AddRange(newMappings);
        }
    }

    private (Namespace, Subtag) GetOrCreateNamespaceAndSubtag(
        TagModel tag, 
        Dictionary<string, Namespace> allNamespaces, 
        Dictionary<string, Subtag> allSubtags)
    {
        if (!allNamespaces.TryGetValue(tag.Namespace ?? string.Empty, out var namespaceEntity))
        {
            namespaceEntity = new Namespace { Value = tag.Namespace ?? string.Empty };
            allNamespaces[namespaceEntity.Value] = namespaceEntity;
        }

        if (!allSubtags.TryGetValue(tag.Subtag, out var subtagEntity))
        {
            subtagEntity = new Subtag { Value = tag.Subtag };
            allSubtags[subtagEntity.Value] = subtagEntity;
        }

        return (namespaceEntity, subtagEntity);
    }

    private async Task<Tag> GetOrCreateTag(Namespace namespaceEntity, Subtag subtagEntity)
    {
        var tagEntity = await _db.Tags.FirstOrDefaultAsync(t => 
            t.Namespace.Value == namespaceEntity.Value && t.Subtag.Value == subtagEntity.Value);

        if (tagEntity == null)
        {
            tagEntity = new Tag { Namespace = namespaceEntity, Subtag = subtagEntity };
            _db.Tags.Add(tagEntity);
        }

        return tagEntity;
    }
}