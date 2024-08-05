namespace HydrusReplacement.Core;

public class UpdateTagsRequest
{
    public required int HashId { get; init; }
    public IEnumerable<TagModel> TagsToAdd { get; init; } = Enumerable.Empty<TagModel>();
    public IEnumerable<TagModel> TagsToRemove { get; init; } = Enumerable.Empty<TagModel>();
}