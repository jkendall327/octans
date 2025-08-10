namespace Octans.Core.Importing;

public class ImportFolderOptions
{
    public const string ConfigurationSectionName = "ImportFolder";

    public bool Enabled { get; init; }
    public TimeSpan Period { get; init; }
    public required List<string> Directories { get; init; } = [];
    public bool DeleteAfterImport { get; init; }
}