using System.Security.Cryptography;

namespace HydrusReplacement.Core;

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

    public ItemType Type { get; }

    /// <summary>
    /// A code determining where in the filesystem this item would be stored.
    /// </summary>
    /// <remarks>This is composed of a tag for the item's type, either 'f' for file or 't' for thumbnail.
    /// We then append the first two characters of the hexadecimal string: fa2, t4b, etc,</remarks>
    public string Bucket { get; }

    public HashedBytes(byte[] source, ItemType type)
    {
        Type = type;
        Bytes = SHA256.HashData(source);
        Hexadecimal = Convert.ToHexString(Bytes);

        var discriminator = Type is ItemType.File ? "f" : "t";
        var tag = Hexadecimal[..2].ToLowerInvariant();
        
        Bucket = string.Concat(discriminator, tag);
    }
}

public enum ItemType
{
    File,
    Thumbnail
}