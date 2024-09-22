using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        
        _sut = new(_fileSystem, globalSettings, NullLogger<DownloaderFactory>.Instance);
    }
    
    private readonly MockFileData _classifier = new ("function match_url(url) return true end");
    private readonly MockFileData _parser = new ("function parse(content) return content end");
    private readonly MockFileData _invalid = new ("This is not valid Lua code");
    
    [Fact]
    public async Task ShouldReturnCorrectNumberOfDownloaders()
    {
        var first = _downloaders.CreateSubdirectory("first");
        var second = _downloaders.CreateSubdirectory("second");
        
        AddFileToSubdir(first, "classifier", _classifier);
        AddFileToSubdir(second, "classifier", _classifier);
        
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
        
        AddFileToSubdir(first, "classifier", _invalid);
        AddFileToSubdir(second, "classifier", _classifier);
        
        var downloaders = await _sut.GetDownloaders();

        downloaders.Single().Invoking(d => d.MatchesUrl("https://example.com")).Should().NotThrow();
    }

    private void AddFileToSubdir(IDirectoryInfo dir, string filename, MockFileData data)
    {
        _fileSystem.AddFile(dir.FullName + $"/{filename}.lua", data);
    }
}