using System.Diagnostics.CodeAnalysis;

namespace Octans.Core.Thumbnails;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO")]
public record ThumbnailCreationRequest(byte[] Bytes, HashedBytes Hashed)
{
    public Guid Id { get; set; } = Guid.NewGuid();
}