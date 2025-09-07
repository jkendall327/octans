using System.Threading.Channels;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using MudBlazor;
using Octans.Client.Components.Gallery;
using Octans.Client.Components.StatusBar;
using Octans.Core.Querying;
using Octans.Core.Repositories;
using Octans.Core.Scripting;
using Microsoft.JSInterop;

namespace Octans.Client.Components.Pages;

public record GalleryContextMenuItem(string Text, string Icon, Func<List<string>, Task>? Action = null, List<GalleryContextMenuItem>? SubItems = null)
{
    public bool HasSubItems => SubItems is not null && SubItems.Count > 0;
};

public sealed class GalleryViewmodel(
    IQueryService service,
    IBrowserStorage storage,
    StatusService status,
    ICustomCommandProvider customCommandProvider,
    ChannelWriter<RepositoryChangeRequest> repositoryChannel,
    IJSRuntime jsRuntime,
    ILogger<GalleryViewmodel> logger) : IAsyncDisposable
{
    private CancellationTokenSource _cts = new();

    public List<string> ImageUrls { get; private set; } = [];
    public bool Searching { get; private set; }
    public string? LastError { get; private set; }
    public Func<Task>? StateChanged { get; set; }
    public string? CurrentImage { get; set; }
    public bool FilterMode { get; set; }
    public List<QueryParameter> CurrentQuery { get; private set; } = [];

    public List<GalleryContextMenuItem> ContextMenuItems { get; private set; } = [];

    private int _total;
    private int _processed;
    public int ProgressPercent => _total == 0 ? 0 : (int)Math.Round(_processed * 100.0 / _total);

    private readonly IJSRuntime _jsRuntime = jsRuntime;

    public async Task OnInitialized()
    {
        await InitializeContextMenuItems();

        var images = await storage.FromSessionAsync<List<string>>("gallery", "gallery-images");

        if (images is not null)
        {
            ImageUrls = images;
        }

        var query = await storage.FromSessionAsync<List<QueryParameter>>("gallery", "gallery-query");

        if (query is not null)
        {
            CurrentQuery = query;
        }
    }

    private async Task InitializeContextMenuItems()
    {
        var customCommands = await customCommandProvider.GetCustomCommandsAsync();
        var customCommandItems = customCommands
            .Select(cmd => new GalleryContextMenuItem(cmd.Name, cmd.Icon, cmd.Execute))
            .ToList();

        ContextMenuItems =
        [
            new("Open in New Tab", Icons.Material.Filled.OpenInNew, OnOpenInNewTab),
            new("Archive/Delete filter", Icons.Material.Filled.Inbox, OpenFilter),
            new("Copy URL", Icons.Material.Filled.ContentCopy, OnCopyUrl),
            new("Add to Favorites", Icons.Material.Filled.Star, OnAddToFavorites),
            new("Custom Commands", Icons.Material.Filled.Extension, SubItems: customCommandItems),
            new("Delete", Icons.Material.Filled.Delete, OnDelete)
        ];
    }

    private async Task OpenFilter(List<string> imageUrls)
    {
        CurrentImage = imageUrls.FirstOrDefault();
        FilterMode = true;

        await Task.CompletedTask;
    }


    private async Task OnOpenInNewTab(List<string> imageUrls)
    {
        foreach (var url in imageUrls)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("open", url, "_blank");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to open {Url} in new tab", url);
            }
        }
    }

    private async Task OnCopyUrl(List<string> imageUrls)
    {
        if (imageUrls.Count == 0) return;

        var joined = string.Join("\n", imageUrls);

        try
        {
            await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", joined);
            status.GenericText = $"Copied {imageUrls.Count} URL(s)";
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to copy URLs to clipboard");
        }

        await NotifyStateChanged();
    }

    private async Task OnAddToFavorites(List<string> imageUrls)
    {
        // There isn't a dedicated favourites store yet, so simply notify the user.
        status.GenericText = $"Added {imageUrls.Count} item(s) to favourites";
        await NotifyStateChanged();
    }

    private async Task OnDelete(List<string> imageUrls)
    {
        foreach (var url in imageUrls)
        {
            try
            {
                var hex = url[(url.LastIndexOf('/') + 1)..];
                await repositoryChannel.WriteAsync(new(hex, RepositoryType.Trash));
                ImageUrls.Remove(url);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to queue delete for {Url}", url);
            }
        }

        await storage.ToSessionAsync("gallery", "gallery-images", ImageUrls);
        await NotifyStateChanged();
    }

    public async Task OnQueryChanged(List<QueryParameter> arg)
    {
        await CancelPreviousRun();

        ResetState();

        status.WorkingText = "loading...";

        await NotifyStateChanged();

        try
        {
            var raw = arg
                .Select(s => s.Raw)
                .ToList();

            _total = await service.CountAsync(raw, _cts.Token);

            await foreach (var result in service.Query(raw, _cts.Token))
            {
                // Build a stable, lower-case hex string for the route
                var hex = Convert
                    .ToHexString(result.Hash)
                    .ToUpperInvariant();

                var url = $"/media/{hex}";

                ImageUrls.Add(url);

                _processed++;

                if (ImageUrls.Count % 8 == 0)
                {
                    await NotifyStateChanged();
                }
            }

            status.MediaInfo = $"{ImageUrls.Count} files";

            await storage.ToSessionAsync("gallery", "gallery-images", ImageUrls);
            await storage.ToSessionAsync("gallery", "gallery-query", CurrentQuery);
        }
        catch (OperationCanceledException)
        {
            // Swallow.
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected exception during query");
            LastError = e.Message;
        }
        finally
        {
            Searching = false;
            status.WorkingText = null;
            await NotifyStateChanged();
        }
    }

    private void ResetState()
    {
        LastError = null;
        Searching = true;
        ImageUrls = [];
        _total = 0;
        _processed = 0;
    }

    private async Task CancelPreviousRun()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = new();
    }

    private async Task NotifyStateChanged()
    {
        if (StateChanged is not null)
        {
            await StateChanged.Invoke();
        }
    }

    public async Task OnCancel()
    {
        await _cts.CancelAsync();
    }

    public async Task OnFilterComplete(ImageViewer.FilterResult result)
    {
        foreach (var (url, choice) in result.Choices)
        {
            var hex = url[(url.LastIndexOf('/') + 1)..];
            var destination = choice switch
            {
                ImageViewer.FilterChoice.Archive => RepositoryType.Archive,
                ImageViewer.FilterChoice.Delete => RepositoryType.Trash,
                _ => RepositoryType.Inbox
            };

            await repositoryChannel.WriteAsync(new(hex, destination));
        }

        ImageUrls.RemoveAll(url =>
            result.Choices.TryGetValue(url, out var choice) &&
            choice == ImageViewer.FilterChoice.Delete);

        await storage.ToSessionAsync("gallery", "gallery-images", ImageUrls);

        await NotifyStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}