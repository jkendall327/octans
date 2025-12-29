namespace Octans.Core.Importing;

public enum ImportType
{
    File,
    RawUrl,
    Post,
    Gallery,
    Watchable
}

public record ImportRequest
{
    public Guid ImportId { get; init; } = Guid.NewGuid();
    public required ImportType ImportType { get; init; }
    public required List<ImportItem> Items { get; init; }
    public required bool DeleteAfterImport { get; init; }
    public ImportFilterData? FilterData { get; init; }
    public bool AllowReimportDeleted { get; init; }
    public bool AutoArchive { get; init; }
}

public record ImportFilterData
{
    public uint? MaxFileSize { get; init; }
    public uint? MinFileSize { get; init; }

    public ICollection<string>? AllowedFileTypes { get; init; }

    public uint? MaxHeight { get; init; }
    public uint? MinHeight { get; init; }
    public uint? MaxWidth { get; init; }
    public uint? MinWidth { get; init; }
}

public record ImportItem
{
    public Uri? Url { get; init; }
    public string? Filepath { get; init; }
    public ICollection<TagModel>? Tags { get; init; }
}

/// <summary>
/// Represents the success of the import for an individual item/file.
/// </summary>
public record ImportItemResult
{
    public required bool Ok { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Represents the success of an overall import, which could include multiple items.
/// </summary>
public record ImportResult(Guid ImportId, List<ImportItemResult> Results);
