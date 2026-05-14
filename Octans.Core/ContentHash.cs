using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Octans.Core;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO compatibility")]
public sealed class ContentHash : IEquatable<ContentHash>
{
    private readonly byte[] _bytes;

    private ContentHash(byte[] bytes)
    {
        _bytes = bytes.ToArray();
        Hex = Convert.ToHexString(_bytes);
        Bucket = Hex[..2].ToLowerInvariant();
    }

    public byte[] Bytes => _bytes.ToArray();
    public string Hex { get; }
    public string Bucket { get; }
    public string ContentBucket => "f" + Bucket;
    public string ThumbnailBucket => "t" + Bucket;

    public static ContentHash FromContent(byte[] source) => new(SHA256.HashData(source));
    public static ContentHash FromHashBytes(byte[] source) => new(source);
    public static ContentHash FromHex(string hex) => new(Convert.FromHexString(hex));

    public bool Equals(ContentHash? other)
    {
        return other is not null && _bytes.SequenceEqual(other._bytes);
    }

    public override bool Equals(object? obj)
    {
        return obj is ContentHash other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var b in _bytes)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return Hex;
    }
}
