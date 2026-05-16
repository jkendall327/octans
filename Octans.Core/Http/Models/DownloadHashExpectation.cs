using System.Text.Json;

namespace Octans.Core.Http.Models;

/// <summary>
/// Expected cryptographic hash for validating a downloaded file before commit.
/// </summary>
public sealed record DownloadHashExpectation
{
    public required string Algorithm { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// Serialization helpers for persisting download hash expectations.
/// </summary>
public static class DownloadHashExpectations
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IEnumerable<DownloadHashExpectation> hashes)
    {
        var values = hashes.ToList();
        return values.Count == 0 ? null : JsonSerializer.Serialize(values, JsonOptions);
    }

    public static IReadOnlyList<DownloadHashExpectation> Deserialize(string? hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<DownloadHashExpectation>>(hashes, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Expected hash validators could not be read.", ex);
        }
    }
}
