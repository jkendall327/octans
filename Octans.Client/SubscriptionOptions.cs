namespace Octans.Client;

public class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";
    
    public List<SubscriptionItem> Items { get; init; } = [];
}

public class SubscriptionItem
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; }
    public string Query { get; set; } = string.Empty;
}
