namespace HydrusReplacement.Server;

public class ImportRequest
{
    public Uri SourceLocation { get; set; }
    public string[] Tags { get; set; }
}