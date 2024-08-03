namespace HydrusReplacement.Core.Importing;

public class ImportRequest
{
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public required List<ImportItem> Items { get; set; }
    public required bool DeleteAfterImport { get; init; }
    
    public uint? MaxFileSize { get; init; }
    public uint? MinFileSize { get; init; }

    public string[]? AllowedFileTypes { get; set; }

    public uint? MaxHeight { get; set; }
    public uint? MinHeight { get; set; }
    public uint? MaxWidth { get; set; }
    public uint? MinWidth { get; set; }
}

public class ImportItem
{
    public required Uri Source { get; set; }
    public required TagModel[] Tags { get; set; }   
}