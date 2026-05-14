using Microsoft.Extensions.Logging.Abstractions;
using Octans.Core.Downloads;

namespace Octans.Tests.Downloads;

public class ActiveDownloadRegistryTests
{
    private readonly ActiveDownloadRegistry _sut = new(NullLogger<ActiveDownloadRegistry>.Instance);

    [Fact]
    public void GetToken_ShouldReturnReusableCancellationToken()
    {
        var id = Guid.NewGuid();

        var token = _sut.GetToken(id);
        var reusedToken = _sut.GetToken(id);

        Assert.False(token == CancellationToken.None);
        Assert.Equal(token, reusedToken);
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_ShouldCancelDownloadToken()
    {
        var id = Guid.NewGuid();
        var token = _sut.GetToken(id);

        _sut.Cancel(id);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Release_ShouldRemoveTokenWithoutCancelingIt()
    {
        var id = Guid.NewGuid();
        var token = _sut.GetToken(id);

        _sut.Release(id);

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelAllAsync_ShouldCancelAllActiveTokens()
    {
        var first = _sut.GetToken(Guid.NewGuid());
        var second = _sut.GetToken(Guid.NewGuid());

        await _sut.CancelAllAsync();

        Assert.True(first.IsCancellationRequested);
        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void DisposingTheRegistry_ShouldCancelAllActiveDownloads()
    {
        var token = _sut.GetToken(Guid.NewGuid());

        _sut.Dispose();

        Assert.True(token.IsCancellationRequested);
    }
}
