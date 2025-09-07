namespace Octans.Client.Components.Importing;

public enum MiscTagPurpose
{
    Filename,
    FirstDirectory,
    SecondDirectory,
    ThirdDirectory,
    ThirdLastDirectory,
    SecondLastDirectory,
    LastDirectory
}

public sealed class MiscTagOption
{
    public required MiscTagPurpose Purpose { get; set; }
    public bool IsEnabled { get; set; }
    public string Namespace { get; set; } = string.Empty;

    public string Label => Purpose switch
    {
        MiscTagPurpose.Filename => "add filename?",
        MiscTagPurpose.FirstDirectory => "add first directory?",
        MiscTagPurpose.SecondDirectory => "add second directory?",
        MiscTagPurpose.ThirdDirectory => "add third directory?",
        MiscTagPurpose.ThirdLastDirectory => "add third last directory?",
        MiscTagPurpose.SecondLastDirectory => "add second last directory?",
        MiscTagPurpose.LastDirectory => "add last directory?",
        _ => throw new InvalidOperationException($"Unknown purpose: {Purpose}")
    };
}

public sealed class MiscTagOptionsData
{
    public List<MiscTagOption> Options { get; } = [
        new() { Purpose = MiscTagPurpose.Filename },
        new() { Purpose = MiscTagPurpose.FirstDirectory },
        new() { Purpose = MiscTagPurpose.SecondDirectory },
        new() { Purpose = MiscTagPurpose.ThirdDirectory },
        new() { Purpose = MiscTagPurpose.ThirdLastDirectory },
        new() { Purpose = MiscTagPurpose.SecondLastDirectory },
        new() { Purpose = MiscTagPurpose.LastDirectory }
    ];
}