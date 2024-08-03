namespace HydrusReplacement.Core;

public class ImportRequest
{
    public required List<ImportItem> Items { get; set; }
    public required bool DeleteAfterImport { get; set; }
}

public class ImportItem
{
    public required Uri Source { get; set; }
    public required TagModel[] Tags { get; set; }   
}