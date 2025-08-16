namespace Octans.Client.Components.StatusBar;

public class StatusBarViewmodel(StatusService service)
{
    public string? MediaInfo => service.MediaInfo;
    public string? GenericText => service.GenericText;
    public string? WorkingText => service.WorkingText;
}

public class StatusService
{
    public string? MediaInfo { get; set; }
    public string? GenericText { get; set; }
    public string? WorkingText { get; set; }
}