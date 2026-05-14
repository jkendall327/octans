using FluentAssertions;
using Octans.Core;
using Octans.Tests.Helpers;

namespace Octans.Tests.Filesystem;

public class ContentHashTests
{
    [Fact]
    public void FromContent_HashesSourceBytes()
    {
        var hash = ContentHash.FromContent(TestingConstants.MinimalJpeg);

        hash
            .Hex
            .Should()
            .Be("61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040");

        hash
            .Bucket
            .Should()
            .Be("61");
    }

    [Fact]
    public void FromHashBytes_DoesNotHashAgain()
    {
        var hash = ContentHash.FromHashBytes([0xde, 0xad, 0xbe, 0xef]);

        hash
            .Hex
            .Should()
            .Be("DEADBEEF");
    }

    [Fact]
    public void Bytes_ReturnsCopy()
    {
        var hash = ContentHash.FromHashBytes([1, 2, 3]);

        var bytes = hash.Bytes;
        bytes[0] = 9;

        hash
            .Bytes
            .Should()
            .Equal(1, 2, 3);
    }
}
