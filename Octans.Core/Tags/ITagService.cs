namespace Octans.Core.Tags;

public interface ITagService
{
    Task<List<TagModel>> GetTagsForHashAsync(string hashHex);
}
