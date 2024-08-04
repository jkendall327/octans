namespace HydrusReplacement.Core.Importing;

public class ImportRequest
{
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public required List<ImportItem> Items { get; set; }
    public required bool DeleteAfterImport { get; init; }

    public ImportFilterData? FilterData { get; set; }
    public bool AllowReimportDeleted { get; set; }
}

public class ImportFilterData
{
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
    public TagModel[]? Tags { get; set; }   
}