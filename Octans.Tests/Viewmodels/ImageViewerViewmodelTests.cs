using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Gallery;
using Octans.Client.Services;
using Octans.Client.Settings;
using Octans.Core.Notes;
using Octans.Core.Repositories;
using Octans.Core.Tags;
using Octans.Data.Models;

namespace Octans.Tests.Viewmodels;

public sealed class ImageViewerViewmodelTests
{
    private readonly IOctansClient _client = Substitute.For<IOctansClient>();
    private readonly IKeybindingService _keybindingService = Substitute.For<IKeybindingService>();
    private readonly ImageViewerViewmodel _sut;

    public ImageViewerViewmodelTests()
    {
        _client.GetMediaDetailsAsync(Arg.Any<string>()).Returns(Details("DEADBEEF", []));
        _sut = new(_client, _keybindingService);
    }

    [Fact]
    public async Task InitializeAsync_loads_tags_for_current_media_hash()
    {
        _client.GetMediaDetailsAsync("DEADBEEF").Returns(Details("DEADBEEF", [new("artist", "someone")]));

        await _sut.InitializeAsync(["/media/DEADBEEF.jpg?width=200"], null, filterMode: false);

        Assert.Equal("/media/DEADBEEF.jpg?width=200", _sut.CurrentImage);
        Assert.NotNull(_sut.Tags);
        var tag = Assert.Single(_sut.Tags);
        Assert.Equal("artist", tag.Namespace);
        Assert.Equal("someone", tag.Subtag);
    }

    [Fact]
    public async Task NextAsync_wraps_from_last_image_to_first()
    {
        await _sut.InitializeAsync(["/media/one", "/media/two"], "/media/two", filterMode: false);

        await _sut.NextAsync();

        Assert.Equal("/media/one", _sut.CurrentImage);
    }

    [Fact]
    public async Task ArchiveAsync_records_choice_and_advances_until_prompt_is_needed()
    {
        await _sut.InitializeAsync(["/media/one", "/media/two"], "/media/one", filterMode: true);

        var action = await _sut.ArchiveAsync();

        Assert.False(action.CommitPromptRequested);
        Assert.Equal("/media/two", _sut.CurrentImage);
        Assert.Equal(ImageViewerFilterChoice.Archive, _sut.Choices["/media/one"]);
    }

    [Fact]
    public async Task DeleteAsync_requests_prompt_on_last_image_and_stay_can_undo_choice()
    {
        await _sut.InitializeAsync(["/media/one"], "/media/one", filterMode: true);

        var action = await _sut.DeleteAsync();

        Assert.True(action.CommitPromptRequested);
        Assert.True(action.UndoLastChoiceOnStay);
        Assert.Equal(ImageViewerFilterChoice.Delete, _sut.Choices["/media/one"]);

        await _sut.StayAfterPromptAsync(action);

        Assert.Empty(_sut.Choices);
    }

    [Fact]
    public async Task HandleKeyDownAsync_uses_keybinding_service_action()
    {
        _keybindingService.GetAction(Arg.Any<KeyboardEventArgs>()).Returns(KeybindingActions.Close);
        await _sut.InitializeAsync(["/media/one"], "/media/one", filterMode: false);

        var action = await _sut.HandleKeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(action.CloseRequested);
    }

    private static MediaDetailsDto Details(string hash, IReadOnlyList<TagModel> tags) => new(
        1,
        hash,
        ".jpg",
        "image/jpeg",
        RepositoryType.Inbox,
        tags,
        Array.Empty<NoteDto>(),
        $"/media/{hash}");
}
