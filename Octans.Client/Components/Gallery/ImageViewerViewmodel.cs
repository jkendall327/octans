using Microsoft.AspNetCore.Components.Web;
using Octans.Client.Services;
using Octans.Client.Settings;
using Octans.Core.Tags;

namespace Octans.Client.Components.Gallery;

public sealed class ImageViewerViewmodel(
    ITagService tagService,
    IKeybindingService keybindingService) : ViewmodelBase
{
    private readonly Dictionary<string, ImageViewerFilterChoice> _choices = [];

    public List<string> ImageUrls { get; private set; } = [];
    public string? CurrentImage { get; private set; }
    public bool FilterMode { get; private set; }
    public List<TagModel>? Tags { get; private set; }
    public IReadOnlyDictionary<string, ImageViewerFilterChoice> Choices => _choices;

    public async Task InitializeAsync(List<string> imageUrls, string? startingImage, bool filterMode)
    {
        ImageUrls = imageUrls;
        FilterMode = filterMode;
        CurrentImage = startingImage ?? ImageUrls.FirstOrDefault();
        Tags = null;

        if (FilterMode)
        {
            _choices.Clear();
        }

        await keybindingService.InitializeAsync();
        await LoadTagsAsync();
    }

    public async Task<ImageViewerAction> HandleKeyDownAsync(KeyboardEventArgs e)
    {
        var action = keybindingService.GetAction(e);
        if (action is null)
        {
            return ImageViewerAction.None;
        }

        return action switch
        {
            KeybindingActions.Previous => await PreviousAsync(),
            KeybindingActions.Next => await NextAsync(),
            KeybindingActions.Close => FilterMode ? ImageViewerAction.PromptCommit() : ImageViewerAction.Close,
            KeybindingActions.Archive => await ArchiveAsync(),
            KeybindingActions.Delete => await DeleteAsync(),
            KeybindingActions.Inbox => ImageViewerAction.None,
            _ => ImageViewerAction.None
        };
    }

    public async Task<ImageViewerAction> PreviousAsync()
    {
        var currentImage = EnsureCurrentImage();

        var idx = ImageUrls.IndexOf(currentImage);
        var prev = idx - 1;

        CurrentImage = IsValidIndex(ImageUrls, prev) ? ImageUrls[prev] : ImageUrls.Last();

        await LoadTagsAsync();
        return ImageViewerAction.None;
    }

    public async Task<ImageViewerAction> NextAsync()
    {
        var currentImage = EnsureCurrentImage();

        var idx = ImageUrls.IndexOf(currentImage);
        var next = idx + 1;

        CurrentImage = IsValidIndex(ImageUrls, next) ? ImageUrls[next] : ImageUrls.First();

        await LoadTagsAsync();
        return ImageViewerAction.None;
    }

    public async Task<ImageViewerAction> ArchiveAsync()
    {
        if (!FilterMode)
        {
            return ImageViewerAction.None;
        }

        return await RecordChoiceAsync(ImageViewerFilterChoice.Archive);
    }

    public async Task<ImageViewerAction> DeleteAsync()
    {
        if (!FilterMode)
        {
            return ImageViewerAction.None;
        }

        return await RecordChoiceAsync(ImageViewerFilterChoice.Delete);
    }

    public async Task<ImageViewerAction> HandleImageMouseDownAsync(long button)
    {
        if (!FilterMode || CurrentImage is null)
        {
            return ImageViewerAction.None;
        }

        return button switch
        {
            0 => await ArchiveAsync(),
            1 => await PreviousAsync(),
            2 => await DeleteAsync(),
            _ => ImageViewerAction.None
        };
    }

    public ImageViewerFilterResult CreateFilterResult()
    {
        return new()
        {
            Choices = new(_choices)
        };
    }

    public async Task StayAfterPromptAsync(ImageViewerAction action)
    {
        if (action.UndoLastChoiceOnStay && CurrentImage is not null)
        {
            _choices.Remove(CurrentImage);
            await NotifyStateChanged();
        }
    }

    private async Task<ImageViewerAction> RecordChoiceAsync(ImageViewerFilterChoice choice)
    {
        var currentImage = EnsureCurrentImage();

        _choices[currentImage] = choice;

        var allImagesJudged = _choices.Count == ImageUrls.Count;
        var onLastImage = ImageUrls.IndexOf(currentImage) == ImageUrls.Count - 1;

        if (allImagesJudged || onLastImage)
        {
            return ImageViewerAction.PromptCommit(undoLastChoiceOnStay: true);
        }

        await NextAsync();
        return ImageViewerAction.None;
    }

    private async Task LoadTagsAsync()
    {
        if (CurrentImage is null)
        {
            Tags = [];
            await NotifyStateChanged();
            return;
        }

        Tags = null;
        await NotifyStateChanged();

        try
        {
            Tags = await tagService.GetTagsForHashAsync(GetHashFromMediaUrl(CurrentImage));
        }
        catch (Exception)
        {
            Tags = [];
        }

        await NotifyStateChanged();
    }

    private static string GetHashFromMediaUrl(string url)
    {
        var path = url.Split('?')[0];
        var segment = path.Split('/').Last();
        return segment.Contains('.', StringComparison.Ordinal) ? segment[..segment.LastIndexOf('.')] : segment;
    }

    private string EnsureCurrentImage()
    {
        if (CurrentImage is null)
        {
            throw new InvalidOperationException("Current image was null after parameters being set");
        }

        return CurrentImage;
    }

    private static bool IsValidIndex<T>(List<T> list, int index) => index >= 0 && index < list.Count;
}

public enum ImageViewerFilterChoice
{
    Archive,
    Delete
}

public sealed class ImageViewerFilterResult
{
    public required Dictionary<string, ImageViewerFilterChoice> Choices { get; init; }
}

public sealed record ImageViewerAction(bool CloseRequested, bool CommitPromptRequested, bool UndoLastChoiceOnStay)
{
    public static ImageViewerAction None { get; } = new(false, false, false);
    public static ImageViewerAction Close { get; } = new(true, false, false);
    public static ImageViewerAction PromptCommit(bool undoLastChoiceOnStay = false) => new(false, true, undoLastChoiceOnStay);
}
