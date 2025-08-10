using System.Text.Json.Serialization;

namespace Octans.Core.Communication;

public class HomeStats
{
    [JsonPropertyName("totalImages")]
    public int TotalImages { get; init; }

    [JsonPropertyName("inboxCount")]
    public int InboxCount { get; init; }

    [JsonPropertyName("tagCount")]
    public int TagCount { get; init; }

    [JsonPropertyName("storageUsed")]
    public string StorageUsed { get; init; } = string.Empty;
}
