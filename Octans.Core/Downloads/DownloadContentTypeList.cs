using System.Text.Json;

namespace Octans.Core.Downloads;

internal static class DownloadContentTypeList
{
    public static string? Serialize(IEnumerable<string>? contentTypes)
    {
        if (contentTypes is null)
        {
            return null;
        }

        var values = Normalize(contentTypes);
        return values.Length == 0 ? null : JsonSerializer.Serialize(values);
    }

    public static string[] Deserialize(string? contentTypes)
    {
        if (string.IsNullOrWhiteSpace(contentTypes))
        {
            return [];
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<string[]>(contentTypes) ?? []);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string[] Normalize(IEnumerable<string> contentTypes)
    {
        return contentTypes
            .Select(contentType => contentType.Trim())
            .Where(contentType => contentType.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
