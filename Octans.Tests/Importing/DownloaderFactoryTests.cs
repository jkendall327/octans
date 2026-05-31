using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Octans.Core;
using Octans.Core.Downloaders;
using Octans.Core.Http;

namespace Octans.Tests.Importing;

public class DownloaderFactoryTests
{
    private readonly DownloaderFactory _sut;
    private readonly MockFileSystem _fileSystem = new();
    private readonly IDirectoryInfo _downloaders;

    public DownloaderFactoryTests()
    {
        var globalSettings = new GlobalSettings { AppRoot = "C:/App" };

        _downloaders = _fileSystem.Directory.CreateDirectory("C:/App/downloaders");

        var opts = Substitute.For<IOptions<GlobalSettings>>();
        opts.Value.Returns(globalSettings);

        _sut = new(_fileSystem, opts, NullLogger<DownloaderFactory>.Instance);
    }

    private readonly MockFileData _classifier = new("""
                                                    function match_url(url) return true end;
                                                    function classify_url(url) return 'post' end;
                                                    """);

    private readonly MockFileData _parser = new("function parse_html(content) return { 'https://example.com/image.jpg' } end");
    private readonly MockFileData _invalid = new("This is not valid Lua code");

    [Fact]
    public async Task ShouldReturnCorrectNumberOfDownloaders()
    {
        var first = _downloaders.CreateSubdirectory("first");
        var second = _downloaders.CreateSubdirectory("second");

        AddFileToSubdir(first, "classifier", _classifier);
        AddFileToSubdir(first, "parser", _parser);
        AddFileToSubdir(second, "classifier", _classifier);
        AddFileToSubdir(second, "parser", _parser);

        var downloaders = await _sut.GetDownloaders();

        downloaders.Should().HaveCount(2, "because two downloader directories were created");
    }

    [Fact]
    public async Task GetDownloaders_ShouldCreateDownloadersWithCorrectFunctions()
    {
        var subdir = _downloaders.CreateSubdirectory("first");

        AddFileToSubdir(subdir, "classifier", _classifier);
        AddFileToSubdir(subdir, "parser", _parser);

        var downloaders = await _sut.GetDownloaders();

        downloaders.Single().Invoking(d => d.MatchesUrl(new("https://example.com"))).Should().NotThrow();
    }

    [Fact]
    public async Task ShouldThrowWhenNoDownloadersDirectory()
    {
        _fileSystem.Directory.Delete("C:/App/downloaders");

        await _sut.Invoking(s => s.GetDownloaders()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ShouldIgnoreInvalidLuaFiles()
    {
        var first = _downloaders.CreateSubdirectory("first");
        var second = _downloaders.CreateSubdirectory("second");

        AddFileToSubdir(first, "classifier", _invalid);

        AddFileToSubdir(second, "classifier", _classifier);
        AddFileToSubdir(second, "parser", _parser);

        var downloaders = await _sut.GetDownloaders();

        downloaders.Single().Invoking(d => d.MatchesUrl(new("https://example.com"))).Should().NotThrow();
    }

    [Fact]
    public async Task ShouldHideDangerousGlobalsFromDownloaderScripts()
    {
        var subdir = _downloaders.CreateSubdirectory("first");

        AddFileToSubdir(subdir, "classifier", new("""
                                                   function match_url(url)
                                                       return io ~= nil or os ~= nil or package ~= nil or debug ~= nil or luanet ~= nil or import ~= nil
                                                   end
                                                   function classify_url(url) return 'post' end
                                                   """));
        AddFileToSubdir(subdir, "parser", _parser);

        var downloaders = await _sut.GetDownloaders();

        downloaders.Single().MatchesUrl(new("https://example.com")).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldIgnoreScriptsThatExceedInstructionBudgetDuringLoad()
    {
        var first = _downloaders.CreateSubdirectory("first");
        var second = _downloaders.CreateSubdirectory("second");

        AddFileToSubdir(first, "classifier", new("while true do end"));

        AddFileToSubdir(second, "classifier", _classifier);
        AddFileToSubdir(second, "parser", _parser);

        var downloaders = await _sut.GetDownloaders();

        downloaders.Should().HaveCount(1);
    }

    [Fact]
    public async Task ShouldStopDownloaderFunctionsThatExceedInstructionBudget()
    {
        var subdir = _downloaders.CreateSubdirectory("first");

        AddFileToSubdir(subdir, "classifier", new("""
                                                   function match_url(url)
                                                       while true do end
                                                   end
                                                   function classify_url(url) return 'post' end
                                                   """));
        AddFileToSubdir(subdir, "parser", _parser);

        var downloaders = await _sut.GetDownloaders();

        downloaders.Single()
            .Invoking(d => d.MatchesUrl(new("https://example.com")))
            .Should()
            .Throw<DownloaderContractException>();
    }

    [Fact]
    public async Task ShouldRejectParserResultsThatAreNotStringUrls()
    {
        var subdir = _downloaders.CreateSubdirectory("first");

        AddFileToSubdir(subdir, "classifier", _classifier);
        AddFileToSubdir(subdir, "parser", new("function parse_html(content) return { 'https://example.com/image.jpg', 42 } end"));

        var downloaders = await _sut.GetDownloaders();

        downloaders.Single()
            .Invoking(d => d.ParseHtml("<html></html>"))
            .Should()
            .Throw<DownloaderContractException>();
    }

    [Fact]
    public async Task ConcurrentDownloaderFunctionCalls_ShouldSerializeSharedLuaContextExecution()
    {
        var subdir = _downloaders.CreateSubdirectory("first");

        AddFileToSubdir(subdir, "classifier", new("""
                                                   local active_calls = 0

                                                   function match_url(url)
                                                       active_calls = active_calls + 1

                                                       if active_calls ~= 1 then
                                                           error('shared Lua context was entered concurrently')
                                                       end

                                                       local total = 0
                                                       for i = 1, 20000 do
                                                           total = total + i
                                                       end

                                                       active_calls = active_calls - 1
                                                       return true
                                                   end

                                                   function classify_url(url) return 'post' end
                                                   """));
        AddFileToSubdir(subdir, "parser", _parser);

        var downloader = (await _sut.GetDownloaders()).Single();
        var tasks = Enumerable
            .Range(0, 100)
            .Select(i => Task.Run(() => downloader.MatchesUrl(new($"https://example.com/{i}"))));

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r);
    }

    [Fact]
    public async Task ResolveAsync_ShouldSkipDownloaderWhenParserReturnsNonHttpUrls()
    {
        var subdir = _downloaders.CreateSubdirectory("first");
        AddFileToSubdir(subdir, "classifier", _classifier);
        AddFileToSubdir(subdir, "parser", new("function parse_html(content) return { 'file:///tmp/image.jpg' } end"));

        var clientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("<html></html>"));
        clientFactory.CreateClient("DownloadClient").Returns(httpClient);

        var headerProvider = Substitute.For<IDownloadRequestHeaderProvider>();
        var service = CreateService(clientFactory, headerProvider);

        var urls = await service.ResolveAsync(new("https://example.com/post/1"));

        urls.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldSkipDownloaderWhenMatchUrlFailsAtRuntime()
    {
        var broken = _downloaders.CreateSubdirectory("broken");
        AddFileToSubdir(broken, "metadata", new("""
                                                Downloader = {
                                                    name = "broken downloader",
                                                    creator = "Octans tests",
                                                    version = "1.0"
                                                }
                                                """));
        AddFileToSubdir(broken, "classifier", new("""
                                                   function match_url(url)
                                                       error('runtime match failure')
                                                   end
                                                   function classify_url(url) return 'post' end
                                                   """));
        AddFileToSubdir(broken, "parser", _parser);

        var working = _downloaders.CreateSubdirectory("working");
        AddFileToSubdir(working, "metadata", new("""
                                                 Downloader = {
                                                     name = "working downloader",
                                                     creator = "Octans tests",
                                                     version = "1.0"
                                                 }
                                                 """));
        AddFileToSubdir(working, "classifier", _classifier);
        AddFileToSubdir(working, "parser", _parser);

        var clientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("<html></html>"));
        clientFactory.CreateClient("DownloadClient").Returns(httpClient);

        var headerProvider = Substitute.For<IDownloadRequestHeaderProvider>();
        var service = CreateService(clientFactory, headerProvider);

        var urls = await service.ResolveAsync(new("https://example.com/post/1"));

        urls.Should().Equal(new Uri("https://example.com/image.jpg"));
    }

    [Fact]
    public async Task ResolveAsync_ShouldRejectResponsesLargerThanConfiguredLimit()
    {
        var subdir = _downloaders.CreateSubdirectory("first");
        AddFileToSubdir(subdir, "classifier", _classifier);
        AddFileToSubdir(subdir, "parser", _parser);

        var clientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("too large"));
        clientFactory.CreateClient("DownloadClient").Returns(httpClient);

        var headerProvider = Substitute.For<IDownloadRequestHeaderProvider>();
        var service = CreateService(clientFactory, headerProvider, new() { MaxResponseBytes = 4 });

        var urls = await service.ResolveAsync(new("https://example.com/post/1"));

        urls.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldHonorCancellationTokenDuringFetch()
    {
        var subdir = _downloaders.CreateSubdirectory("first");
        AddFileToSubdir(subdir, "classifier", _classifier);
        AddFileToSubdir(subdir, "parser", _parser);

        var handler = new CancellableHttpMessageHandler();
        var clientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(handler);
        clientFactory.CreateClient("DownloadClient").Returns(httpClient);

        var headerProvider = Substitute.For<IDownloadRequestHeaderProvider>();
        var service = CreateService(clientFactory, headerProvider);
        using var cts = new CancellationTokenSource();

        var resolveTask = service.ResolveAsync(new("https://example.com/post/1"), cts.Token);
        await handler.WaitUntilStarted();

        await cts.CancelAsync();

        var act = async () => await resolveTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private void AddFileToSubdir(IDirectoryInfo dir, string filename, MockFileData data)
    {
        _fileSystem.AddFile(dir.FullName + $"/{filename}.lua", data);
    }

    private DownloaderService CreateService(
        IHttpClientFactory clientFactory,
        IDownloadRequestHeaderProvider headerProvider,
        DownloaderResolverOptions? resolverOptions = null) =>
        new(
            clientFactory,
            _sut,
            headerProvider,
            Options.Create(resolverOptions ?? new DownloaderResolverOptions()),
            NullLogger<DownloaderService>.Instance);

}
