using Octans.Core;
using Octans.Core.Querying;

namespace Octans.Client.Components.Pages;

public sealed class GalleryViewmodel(QueryService service, SubfolderManager manager, ILogger<GalleryViewmodel> logger)
    : IAsyncDisposable
{
    private CancellationTokenSource _cts = new();

    public List<string> ImageUrls { get; private set; } = [];
    public bool Searching { get; private set; }
    public string? LastError { get; private set; }
    public Func<Task>? StateChanged { get; set; }

    public async Task OnQueryChanged(List<QueryParameter> arg)
    {
        // Cancel previous run
        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = new();

        LastError = null;
        Searching = true;
        ImageUrls = [];

        await NotifyStateChanged();

        try
        {
            var raw = arg.Select(s => s.Raw);

            await foreach (var result in service.Query(raw, _cts.Token))
            {
                // Build a stable, lower-case hex string for the route
                var hex = Convert.ToHexString(result.Hash).ToLowerInvariant();

                var url = $"/media/{hex}";

                ImageUrls.Add(url);

                if (ImageUrls.Count % 8 == 0)
                {
                    await NotifyStateChanged();
                }
            }
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
            await NotifyStateChanged();
        }
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

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}