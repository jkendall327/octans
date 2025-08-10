namespace Octans.Core.Importing;

public class ImportFolderOptions
{
    public const string ConfigurationSectionName = "ImportFolder";
    
    public bool Enabled { get; set; }
    public TimeSpan Period { get; set; }
    public List<string> Directories { get; set; } = [];
}