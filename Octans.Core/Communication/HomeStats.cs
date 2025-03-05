using System.Text.Json.Serialization;

namespace Octans.Core.Communication;

public class HomeStats
{
    [JsonPropertyName("totalImages")]
    public int TotalImages { get; set; }
    
    [JsonPropertyName("inboxCount")]
    public int InboxCount { get; set; }
    
    [JsonPropertyName("tagCount")]
    public int TagCount { get; set; }
    
    [JsonPropertyName("storageUsed")]
    public string StorageUsed { get; set; } = string.Empty;
}
