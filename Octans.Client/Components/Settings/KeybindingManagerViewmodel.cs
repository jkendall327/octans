using MudBlazor;
using Octans.Client.Services;
using Octans.Client.Settings;

namespace Octans.Client.Components.Settings;

public sealed class KeybindingManagerViewmodel(
    IKeybindingService keybindingService,
    ISnackbar snackbar) : ViewmodelBase
{
    public List<KeybindingSet> Sets { get; private set; } = [];
    public KeybindingSet? SelectedSet { get; private set; }
    public Guid? ActiveSetId { get; private set; }
    public string NewKey { get; set; } = string.Empty;
    public bool NewCtrl { get; set; }
    public bool NewShift { get; set; }
    public bool NewAlt { get; set; }
    public string NewAction { get; set; } = KeybindingActions.Next;

    public async Task InitializeAsync()
    {
        await keybindingService.InitializeAsync();
        await RefreshStateAsync();
    }

    public async Task SelectSet(KeybindingSet? set)
    {
        SelectedSet = set;
        await NotifyStateChanged();
    }

    public async Task CreateSet()
    {
        var newSet = new KeybindingSet { Name = "New Set" };
        await keybindingService.AddSetAsync(newSet);
        LoadState();
        SelectedSet = newSet;
        await NotifyStateChanged();
    }

    public async Task ActivateSelectedSet()
    {
        if (SelectedSet is null)
        {
            return;
        }

        await keybindingService.SetActiveSetAsync(SelectedSet.Id);
        LoadState();
        snackbar.Add($"Activated set: {SelectedSet.Name}", Severity.Success);
        await NotifyStateChanged();
    }

    public async Task DeleteSelectedSet()
    {
        if (SelectedSet is null)
        {
            return;
        }

        if (Sets.Count <= 1)
        {
            snackbar.Add("Cannot delete the last set.", Severity.Warning);
            return;
        }

        await keybindingService.DeleteSetAsync(SelectedSet.Id);
        SelectedSet = null;
        await RefreshStateAsync();
    }

    public async Task SaveSelectedSet()
    {
        if (SelectedSet is not null)
        {
            await keybindingService.UpdateSetAsync(SelectedSet);
        }
    }

    public async Task AddKeybinding()
    {
        if (SelectedSet is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NewKey))
        {
            snackbar.Add("Key is required.", Severity.Error);
            return;
        }

        if (SelectedSet.Keybindings.Any(k =>
            string.Equals(k.Key, NewKey, StringComparison.OrdinalIgnoreCase) &&
            k.Ctrl == NewCtrl &&
            k.Shift == NewShift &&
            k.Alt == NewAlt))
        {
            snackbar.Add("Keybinding already exists in this set.", Severity.Error);
            return;
        }

        SelectedSet.Keybindings.Add(new Keybinding
        {
            Key = NewKey,
            Ctrl = NewCtrl,
            Shift = NewShift,
            Alt = NewAlt,
            ActionId = NewAction
        });

        await keybindingService.UpdateSetAsync(SelectedSet);

        NewKey = string.Empty;
        NewCtrl = false;
        NewShift = false;
        NewAlt = false;
        await NotifyStateChanged();
    }

    public async Task RemoveKeybinding(Keybinding binding)
    {
        if (SelectedSet is null)
        {
            return;
        }

        SelectedSet.Keybindings.Remove(binding);
        await keybindingService.UpdateSetAsync(SelectedSet);
        await NotifyStateChanged();
    }

    public static string GetModifiersString(Keybinding k)
    {
        var mods = new List<string>();
        if (k.Ctrl)
        {
            mods.Add("Ctrl");
        }

        if (k.Shift)
        {
            mods.Add("Shift");
        }

        if (k.Alt)
        {
            mods.Add("Alt");
        }

        return string.Join(" + ", mods);
    }

    private async Task RefreshStateAsync()
    {
        LoadState();
        await NotifyStateChanged();
    }

    private void LoadState()
    {
        Sets = keybindingService.GetSets().ToList();
        var active = keybindingService.GetActiveSet();
        ActiveSetId = active?.Id;
    }
}
