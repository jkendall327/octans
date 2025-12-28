namespace Octans.Client.Settings;

public class KeybindingSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<Keybinding> Keybindings { get; } = [];
}

public class Keybinding
{
    public string Key { get; set; } = string.Empty;
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public string ActionId { get; set; } = string.Empty;
}

public static class KeybindingActions
{
    public const string Next = "Next";
    public const string Previous = "Previous";
    public const string Archive = "Archive";
    public const string Delete = "Delete";
    public const string Inbox = "Inbox";
    public const string Close = "Close";
}
