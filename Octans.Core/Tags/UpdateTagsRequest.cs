namespace Octans.Core.Tags;

public record UpdateTagsRequest(int HashId, IEnumerable<TagModel> TagsToAdd, IEnumerable<TagModel> TagsToRemove);