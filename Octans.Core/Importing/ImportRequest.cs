namespace Octans.Core.Importing;

public enum ImportType
{
    File,
    RawUrl,
    Post,
    Gallery,
    Watchable
}

public class ImportRequest
{
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public required ImportType ImportType { get; init; }
    public required List<ImportItem> Items { get; init; }
    public required bool DeleteAfterImport { get; init; }
    public ImportFilterData? FilterData { get; init; }
    public bool AllowReimportDeleted { get; set; }
}

public class ImportFilterData
{
    public uint? MaxFileSize { get; init; }
    public uint? MinFileSize { get; init; }

    public ICollection<string>? AllowedFileTypes { get; init; }

    public uint? MaxHeight { get; init; }
    public uint? MinHeight { get; init; }
    public uint? MaxWidth { get; init; }
    public uint? MinWidth { get; init; }
}

public class ImportItem
{
    public required Uri Source { get; init; }
    public ICollection<TagModel>? Tags { get; init; }
}