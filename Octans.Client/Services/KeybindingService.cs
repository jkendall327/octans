using Microsoft.AspNetCore.Components.Web;
using Octans.Client.Settings;

namespace Octans.Client.Services;

public interface IKeybindingService
{
    Task InitializeAsync();
    string? GetAction(KeyboardEventArgs e);
    IEnumerable<KeybindingSet> GetSets();
    KeybindingSet? GetActiveSet();
    Task SetActiveSetAsync(Guid id);
    Task AddSetAsync(KeybindingSet keybindingSet);
    Task UpdateSetAsync(KeybindingSet keybindingSet);
    Task DeleteSetAsync(Guid id);
}

public class KeybindingService(ISettingsService settingsService) : IKeybindingService
{
    private SettingsModel _settings = new();
    private bool _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        _settings = await settingsService.LoadAsync();

        if (!_settings.KeybindingSets.Any())
        {
            var defaultSet = new KeybindingSet
            {
                Id = Guid.NewGuid(),
                Name = "Default"
            };
            defaultSet.Keybindings.AddRange(
            [
                new Keybinding { Key = "ArrowRight", ActionId = KeybindingActions.Next },
                new Keybinding { Key = "ArrowLeft", ActionId = KeybindingActions.Previous },
                new Keybinding { Key = "Escape", ActionId = KeybindingActions.Close },
                new Keybinding { Key = "Delete", ActionId = KeybindingActions.Delete }
            ]);
            _settings.KeybindingSets.Add(defaultSet);
            _settings.ActiveKeybindingSetId = defaultSet.Id;
            await settingsService.SaveAsync(_settings);
        }
        else if (_settings.ActiveKeybindingSetId == null || _settings.KeybindingSets.All(s => s.Id != _settings.ActiveKeybindingSetId))
        {
            _settings.ActiveKeybindingSetId = _settings.KeybindingSets.First().Id;
            await settingsService.SaveAsync(_settings);
        }

        _initialized = true;
    }

    public string? GetAction(KeyboardEventArgs e)
    {
        if (!_initialized) return null;

        var activeSet = GetActiveSet();
        if (activeSet == null) return null;

        var binding = activeSet.Keybindings.FirstOrDefault(k =>
            string.Equals(k.Key, e.Key, StringComparison.OrdinalIgnoreCase) &&
            k.Ctrl == e.CtrlKey &&
            k.Shift == e.ShiftKey &&
            k.Alt == e.AltKey);

        return binding?.ActionId;
    }

    public IEnumerable<KeybindingSet> GetSets()
    {
        return _settings.KeybindingSets;
    }

    public KeybindingSet? GetActiveSet()
    {
        return _settings.KeybindingSets.FirstOrDefault(s => s.Id == _settings.ActiveKeybindingSetId);
    }

    public async Task SetActiveSetAsync(Guid id)
    {
        var set = _settings.KeybindingSets.FirstOrDefault(s => s.Id == id);
        if (set == null) return;

        _settings.ActiveKeybindingSetId = id;
        await settingsService.SaveAsync(_settings);
    }

    public async Task AddSetAsync(KeybindingSet keybindingSet)
    {
        _settings.KeybindingSets.Add(keybindingSet);
        await settingsService.SaveAsync(_settings);
    }

    public async Task UpdateSetAsync(KeybindingSet keybindingSet)
    {
        var index = _settings.KeybindingSets.FindIndex(s => s.Id == keybindingSet.Id);
        if (index == -1) return;

        _settings.KeybindingSets[index] = keybindingSet;
        await settingsService.SaveAsync(_settings);
    }

    public async Task DeleteSetAsync(Guid id)
    {
        var set = _settings.KeybindingSets.FirstOrDefault(s => s.Id == id);
        if (set == null) return;

        _settings.KeybindingSets.Remove(set);

        if (_settings.ActiveKeybindingSetId == id)
        {
            _settings.ActiveKeybindingSetId = _settings.KeybindingSets.FirstOrDefault()?.Id;
        }

        await settingsService.SaveAsync(_settings);
    }
}
