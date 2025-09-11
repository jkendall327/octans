using Microsoft.AspNetCore.Components;

namespace Octans.Client.Components.MainToolbar;

public class MainToolbarViewmodel(NavigationManager nav)
{
    public List<MenuItem> Menu { get; private set; } = [];

    public void OnInitialized()
    {
        Menu =
        [
            new()
            {
                Name = "import",
                Items =
                [
                    new()
                    {
                        Name = "local files",
                        Page = Page.LocalFiles
                    },
                    new()
                    {
                        Name = "web urls",
                        Page = Page.LocalFiles
                    },
                    new()
                    {
                        Name = "gallery",
                        Page = Page.LocalFiles
                    },
                    new()
                    {
                        Name = "watchable",
                        Page = Page.LocalFiles
                    },
                ]
            },
            new()
            {
                Name = "downloads",
                Items =
                [
                    new()
                    {
                        Name = "active",
                        Page = Page.Downloads
                    }
                ]
            },
            new()
            {
                Name = "database",
                Items = []
            },
            new()
            {
                Name = "tags",
                Items = []
            },
            new()
            {
                Name = "network",
                Items = []
            },
            new()
            {
                Name = "system",
                Items =
                [
                    new()
                    {
                        Name = "settings",
                        Page = Page.Settings
                    }
                ]
            }
        ];
    }

    public Task Navigate(Page page)
    {
        var url = page switch
        {
            Page.LocalFiles => "/imports",
            Page.WebUrls => "/imports",
            Page.Settings => "/settings",
            Page.Downloads => "/downloads",
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
        };

        nav.NavigateTo(url);

        return Task.CompletedTask;
    }
}

public enum Page
{
    LocalFiles,
    WebUrls,
    Settings,
    Downloads
}

public class MenuItem
{
    public required string Name { get; init; }
    public List<ToolbarItem> Items { get; init; } = [];
}

public class ToolbarItem
{
    public required string Name { get; init; }
    public Page Page { get; init; }
}