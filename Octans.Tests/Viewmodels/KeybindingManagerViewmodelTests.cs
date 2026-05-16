using MudBlazor;
using NSubstitute;
using Octans.Client.Components.Settings;
using Octans.Client.Services;
using Octans.Client.Settings;

namespace Octans.Tests.Viewmodels;

public sealed class KeybindingManagerViewmodelTests
{
    private readonly IKeybindingService _keybindingService = Substitute.For<IKeybindingService>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly KeybindingManagerViewmodel _sut;

    public KeybindingManagerViewmodelTests()
    {
        _sut = new(_keybindingService, _snackbar);
    }

    [Fact]
    public async Task InitializeAsync_loads_sets_and_active_set()
    {
        var active = new KeybindingSet { Name = "Default" };
        var inactive = new KeybindingSet { Name = "Alt" };
        _keybindingService.GetSets().Returns([active, inactive]);
        _keybindingService.GetActiveSet().Returns(active);

        await _sut.InitializeAsync();

        Assert.Equal([active, inactive], _sut.Sets);
        Assert.Equal(active.Id, _sut.ActiveSetId);
    }

    [Fact]
    public async Task AddKeybinding_updates_selected_set_and_resets_inputs()
    {
        var set = new KeybindingSet { Name = "Default" };
        await _sut.SelectSet(set);
        _sut.NewKey = "ArrowRight";
        _sut.NewCtrl = true;
        _sut.NewAction = KeybindingActions.Next;

        await _sut.AddKeybinding();

        var binding = Assert.Single(set.Keybindings);
        Assert.Equal("ArrowRight", binding.Key);
        Assert.True(binding.Ctrl);
        Assert.Equal(KeybindingActions.Next, binding.ActionId);
        Assert.Empty(_sut.NewKey);
        Assert.False(_sut.NewCtrl);
        await _keybindingService.Received(1).UpdateSetAsync(set);
    }

    [Fact]
    public async Task AddKeybinding_rejects_duplicate_shortcut()
    {
        var set = new KeybindingSet { Name = "Default" };
        set.Keybindings.Add(new Keybinding { Key = "A", Ctrl = true, ActionId = KeybindingActions.Next });
        await _sut.SelectSet(set);
        _sut.NewKey = "a";
        _sut.NewCtrl = true;
        _sut.NewAction = KeybindingActions.Previous;

        await _sut.AddKeybinding();

        Assert.Single(set.Keybindings);
        await _keybindingService.DidNotReceive().UpdateSetAsync(Arg.Any<KeybindingSet>());
    }

    [Fact]
    public async Task DeleteSelectedSet_does_not_delete_last_set()
    {
        var set = new KeybindingSet { Name = "Default" };
        _keybindingService.GetSets().Returns([set]);
        _keybindingService.GetActiveSet().Returns(set);
        await _sut.InitializeAsync();
        await _sut.SelectSet(set);

        await _sut.DeleteSelectedSet();

        await _keybindingService.DidNotReceive().DeleteSetAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task RemoveKeybinding_updates_selected_set()
    {
        var binding = new Keybinding { Key = "A", ActionId = KeybindingActions.Next };
        var set = new KeybindingSet { Name = "Default" };
        set.Keybindings.Add(binding);
        await _sut.SelectSet(set);

        await _sut.RemoveKeybinding(binding);

        Assert.Empty(set.Keybindings);
        await _keybindingService.Received(1).UpdateSetAsync(set);
    }

    [Fact]
    public void GetModifiersString_formats_enabled_modifiers()
    {
        var binding = new Keybinding { Ctrl = true, Shift = true };

        var result = KeybindingManagerViewmodel.GetModifiersString(binding);

        Assert.Equal("Ctrl + Shift", result);
    }
}
