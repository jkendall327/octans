namespace Octans.Core;

public class UpdateTagsRequest
{
    public required int HashId { get; init; }
    public IEnumerable<TagModel> TagsToAdd { get; init; } = [];
    public IEnumerable<TagModel> TagsToRemove { get; init; } = [];
}