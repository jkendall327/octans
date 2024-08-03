namespace HydrusReplacement.Core.Importing;

/// <summary>
/// Represents the success of the import for an individual item/file.
/// </summary>
public class ImportItemResult
{
    public required bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Represents the success of an overall import, which could include multiple items.
/// </summary>
public record ImportResult(Guid ImportId, List<ImportItemResult> Results);