using Microsoft.AspNetCore.Components;

namespace Octans.Client.Components.Settings;

public class SettingsContext
{
    private readonly List<SettingsPageDescriptor> _pages = new();
    private readonly List<SettingDescriptor> _settings = new();

    public IReadOnlyList<SettingsPageDescriptor> Pages => _pages;
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void RegisterPage(SettingsPageDescriptor descriptor) => _pages.Add(descriptor);

    public void UnregisterPage(SettingsPageDescriptor descriptor) => _pages.Remove(descriptor);

    public void RegisterSetting(SettingDescriptor descriptor) => _settings.Add(descriptor);

    public void UnregisterSetting(SettingDescriptor descriptor) => _settings.Remove(descriptor);

    public IEnumerable<SettingDescriptor> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SettingDescriptor>();
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        return _settings.Where(s => s.Name.Contains(query, comparison) ||
                                    s.Tags.Any(t => t.Contains(query, comparison)));
    }
}

public record SettingsPageDescriptor(string Title, string? Icon, RenderFragment PageContent);

public record SettingDescriptor(string Name, SettingsPageDescriptor Page, IEnumerable<string> Tags, Func<Task> Focus);

