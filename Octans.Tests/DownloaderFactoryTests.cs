using System.IO.Abstractions.TestingHelpers;
using Octans.Core;
using Octans.Core.Downloaders;

namespace Octans.Tests;

public class DownloaderFactoryTests
{
    [Fact]
    public async Task GetDownloaders_ReturnsCorrectNumberOfDownloaders()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { @"C:\App\downloaders\downloader1\metadata.lua", new MockFileData("") },
            { @"C:\App\downloaders\downloader1\classifier.lua", new MockFileData("function match_url(url) return true end") },
            { @"C:\App\downloaders\downloader2\classifier.lua", new MockFileData("function match_url(url) return false end") },
        });

        var globalSettings = new GlobalSettings { AppRoot = @"C:\App" };
        var factory = new DownloaderFactory(mockFileSystem, globalSettings);

        // Act
        var downloaders = await factory.GetDownloaders();

        // Assert
        Assert.Equal(2, downloaders.Count);
    }

    [Fact]
    public async Task GetDownloaders_CreatesDownloadersWithCorrectFunctions()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { @"C:\App\downloaders\downloader1\classifier.lua", new MockFileData("function match_url(url) return true end") },
            { @"C:\App\downloaders\downloader1\parser.lua", new MockFileData("function parse(content) return content end") },
        });

        var globalSettings = new GlobalSettings { AppRoot = @"C:\App" };
        var factory = new DownloaderFactory(mockFileSystem, globalSettings);

        // Act
        var downloaders = await factory.GetDownloaders();

        // Assert
        Assert.Single(downloaders);
        var downloader = downloaders[0];
        Assert.True(downloader.MatchesUrl("https://example.com"));
    }
}