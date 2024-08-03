namespace HydrusReplacement.Server;

public class ImportRequest
{
    public required List<ImportItem> Items { get; set; }
    public required bool DeleteAfterImport { get; set; }
}

public class ImportItem
{
    public required Uri Source { get; set; }
    public required string[] Tags { get; set; }   
}