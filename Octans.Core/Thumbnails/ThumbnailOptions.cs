namespace Octans.Client.Options;

public sealed class ThumbnailOptions
{
    public const string ConfigurationSectionName = "Thumbnails";
    public int Width { get; set; } = 240;
    public int Height { get; set; } = 240;
}
