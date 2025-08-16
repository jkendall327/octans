using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using MimeDetective.InMemory;

namespace Octans.Core;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO")]
public class HashedBytes
{
    /// <summary>
    /// The underlying bytes, which have been hashed via SHA256.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// A hexadecimal representation of the hashed bytes.
    /// </summary>
    public string Hexadecimal { get; }

    /// <summary>
    /// A code determining where in the filesystem this item would be stored.
    /// </summary>
    /// <remarks>This is composed of a tag for the item's type, either 'f' for file or 't' for thumbnail.
    /// We then append the first two characters of the hexadecimal string: fa2, t4b, etc,</remarks>
    public string Bucket { get; }
    public string ContentBucket => "f" + Bucket;
    public string ThumbnailBucket => "t" + Bucket;

    public FileType MimeType { get; }

    private HashedBytes(byte[] source)
    {
        Bytes = source;
        Hexadecimal = Convert.ToHexString(Bytes);
        Bucket = Hexadecimal[..2].ToUpperInvariant();
        MimeType = source.DetectMimeType();
    }

    public static HashedBytes FromUnhashed(byte[] source) => new(SHA256.HashData(source));
    public static HashedBytes FromHashed(byte[] source) => new(source);
}