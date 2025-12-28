namespace Octans.Client.Settings;

public class SettingsModel
{
    public string Theme { get; set; } = "light";
    public string AppRoot { get; set; } = string.Empty;
    public string LogLevel { get; set; } = "Information";
    public string AspNetCoreLogLevel { get; set; } = "Warning";
    public string ImportSource { get; set; } = string.Empty;
    public string TagColor { get; set; } = "#000000";
    public List<KeybindingSet> KeybindingSets { get; } = [];
    public Guid? ActiveKeybindingSetId { get; set; }
}
