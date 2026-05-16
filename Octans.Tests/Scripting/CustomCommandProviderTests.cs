using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Octans.Core;
using Octans.Core.Scripting;
using System.IO.Abstractions.TestingHelpers;

namespace Octans.Tests.Scripting;

public sealed class CustomCommandProviderTests
{
    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task GetCustomCommandsAsync_returns_empty_list_when_command_directory_is_missing()
    {
        var sut = CreateSut();

        var commands = await sut.GetCustomCommandsAsync();

        commands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCustomCommandsAsync_loads_metadata_from_lua_files()
    {
        _fileSystem.AddDirectory("/app/commands/image-commands");
        _fileSystem.AddFile("/app/commands/image-commands/open.lua", new MockFileData("""
            ImageCommand = {
                name = "Open images",
                description = "Open selected images externally",
                icon = "fa-solid fa-up-right-from-square"
            }

            function execute(imageUrls)
            end
            """));

        var sut = CreateSut();

        var commands = await sut.GetCustomCommandsAsync();

        commands.Should().ContainSingle();
        commands[0].Name.Should().Be("Open images");
        commands[0].Description.Should().Be("Open selected images externally");
        commands[0].Icon.Should().Be("fa-solid fa-up-right-from-square");
    }

    [Fact]
    public async Task GetCustomCommandsAsync_skips_lua_files_without_command_metadata()
    {
        _fileSystem.AddDirectory("/app/commands/image-commands");
        _fileSystem.AddFile("/app/commands/image-commands/no-metadata.lua", new MockFileData("""
            function execute(imageUrls)
            end
            """));

        var sut = CreateSut();

        var commands = await sut.GetCustomCommandsAsync();

        commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Command_execute_completes_for_loaded_script()
    {
        _fileSystem.AddDirectory("/app/commands/image-commands");
        _fileSystem.AddFile("/app/commands/image-commands/noop.lua", new MockFileData("""
            ImageCommand = {
                name = "No op",
                description = "Does nothing",
                icon = "fa-solid fa-code"
            }

            function execute(imageUrls)
            end
            """));

        var sut = CreateSut();
        var command = (await sut.GetCustomCommandsAsync()).Single();

        var act = async () => await command.Execute(["https://example.test/a.jpg"]);

        await act.Should().NotThrowAsync();
    }

    private CustomCommandProvider CreateSut() => new(
        _fileSystem,
        Options.Create(new GlobalSettings
        {
            AppRoot = "/app"
        }),
        NullLogger<CustomCommandProvider>.Instance);
}
