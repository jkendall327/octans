namespace Octans.Core;

public record UpdateTagsRequest(int HashId, IEnumerable<TagModel> TagsToAdd, IEnumerable<TagModel> TagsToRemove);