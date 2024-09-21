using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Octans.Core;
using Octans.Core.Downloaders;

namespace Octans.Tests;

public class DownloaderFactoryTests
{
    private readonly DownloaderFactory _sut;
    private readonly MockFileSystem _fileSystem = new();
    private readonly IDirectoryInfo _downloaders;
    
    public DownloaderFactoryTests()
    {
        var globalSettings = new GlobalSettings { AppRoot = "C:/App" };
        
        _downloaders = _fileSystem.Directory.CreateDirectory("C:/App/downloaders");
        
        _sut = new(_fileSystem, globalSettings);
    }
    
    [Fact]
    public async Task ShouldReturnCorrectNumberOfDownloaders()
    {
        var first = _downloaders.CreateSubdirectory("first");
        var second = _downloaders.CreateSubdirectory("second");
        
        _fileSystem.AddFile(first.FullName + "/classifier.lua", new("function match_url(url) return true end"));
        _fileSystem.AddFile(second.FullName + "/classifier.lua", new("function match_url(url) return false end"));
        
        var downloaders = await _sut.GetDownloaders();

        downloaders.Should().HaveCount(2, "because two downloader directories were created");
    }

    [Fact]
    public async Task GetDownloaders_ShouldCreateDownloadersWithCorrectFunctions()
    {
        var subdir = _downloaders.CreateSubdirectory("first");
        
        _fileSystem.AddFile(subdir.FullName + "/classifier.lua", new("function match_url(url) return true end"));
        _fileSystem.AddFile(subdir.FullName + "/parser.lua", new("function parse(content) return content end"));

        var downloaders = await _sut.GetDownloaders();

        downloaders.Single().Invoking(d => d.MatchesUrl("https://example.com")).Should().NotThrow();
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
        
        _fileSystem.AddFile(first.FullName + "/classifier.lua", new("This is not valid Lua code"));
        _fileSystem.AddFile(second.FullName + "/classifier.lua", new("function match_url(url) return true end"));
        
        var downloaders = await _sut.GetDownloaders();

        downloaders.Single().Invoking(d => d.MatchesUrl("https://example.com")).Should().NotThrow();
    }
}