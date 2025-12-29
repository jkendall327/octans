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
    // TODO: make this and other DTO types into records.
    // Removing the setter on this broke a test,
    // presumably because it relied on the ImportId being set
    // when deserialized across a HTTP boundary.
    public Guid ImportId { get; set; } = Guid.NewGuid();
    public required ImportType ImportType { get; init; }
    public required List<ImportItem> Items { get; init; }
    public required bool DeleteAfterImport { get; init; }
    public ImportFilterData? FilterData { get; init; }
    public bool AllowReimportDeleted { get; set; }
    public bool AutoArchive { get; init; }
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
    public Uri? Url { get; init; }
    public string? Filepath { get; init; }
    public ICollection<TagModel>? Tags { get; init; }
}

/// <summary>
/// Represents the success of the import for an individual item/file.
/// </summary>
public class ImportItemResult
{
    public required bool Ok { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Represents the success of an overall import, which could include multiple items.
/// </summary>
public record ImportResult(Guid ImportId, List<ImportItemResult> Results);
