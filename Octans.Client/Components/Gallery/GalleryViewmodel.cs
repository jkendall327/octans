using Octans.Core;
using Octans.Core.Querying;

namespace Octans.Client.Components.Pages;

public sealed class GalleryViewmodel(QueryService service, SubfolderManager manager, ILogger<GalleryViewmodel> logger)
    : IAsyncDisposable
{
    private CancellationTokenSource _cts = new();

    public List<string> ImagePaths { get; private set; } = [];
    public bool Searching { get; private set; }
    public string? LastError { get; private set; }
    public Func<Task>? StateChanged { get; set; }

    public async Task OnQueryChanged(List<QueryParameter> arg)
    {
        LastError = null;
        Searching = true;
        ImagePaths = [];

        await NotifyStateChanged();

        try
        {
            var raw = arg.Select(s => s.Raw);

            var results = service.Query(raw, _cts.Token);

            var i = 0;

            await foreach (var result in results)
            {
                var info = manager.GetFilepath(HashedBytes.FromUnhashed(result.Hash));

                if (info is null)
                {
                    continue;
                }

                ImagePaths.Add(info.FullName);

                i++;

                if (i >= 5)
                {
                    await NotifyStateChanged();
                    i = 0;
                }
            }
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
        _cts = new();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}