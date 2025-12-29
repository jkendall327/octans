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
                Name = "gallery",
                Page = Page.Gallery
            },
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
                        Page = Page.WebUrls
                    },
                    new()
                    {
                        Name = "gallery",
                        Page = Page.ImportGallery
                    },
                    new()
                    {
                        Name = "watchable",
                        Page = Page.Watchable
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
                    },
                    new()
                    {
                        Name = "downloaders",
                        Page = Page.Downloaders
                    }
                ]
            },
            new()
            {
                Name = "database",
                Items =
                [
                    new()
                    {
                        Name = "duplicates",
                        Page = Page.Duplicates
                    }
                ]
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
            Page.LocalFiles => "/imports?tab=files",
            Page.WebUrls => "/imports?tab=url",
            Page.ImportGallery => "/imports?tab=gallery",
            Page.Gallery => "/gallery",
            Page.Watchable => "/imports?tab=watchable",
            Page.Downloaders => "/downloaders",
            Page.Settings => "/settings",
            Page.Downloads => "/downloads",
            Page.Duplicates => "/duplicates",
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
    ImportGallery,
    Gallery,
    Watchable,
    Settings,
    Downloads,
    Downloaders,
    Duplicates
}

public class MenuItem
{
    public required string Name { get; init; }
    public Page? Page { get; init; }
    public List<ToolbarItem> Items { get; init; } = [];
}

public class ToolbarItem
{
    public required string Name { get; init; }
    public Page Page { get; init; }
}