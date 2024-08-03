namespace HydrusReplacement.Core.Importing;

public class ImportRequest
{
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public required List<ImportItem> Items { get; set; }
    public required bool DeleteAfterImport { get; init; }
    
    public uint? MaxSize { get; init; }
    public uint? MinSize { get; init; }
}

public class ImportItem
{
    public required Uri Source { get; set; }
    public required TagModel[] Tags { get; set; }   
}