using Microsoft.JSInterop;

namespace Octans.Client.Services;

public interface IBrowserService
{
    Task OpenInNewTab(string url, CancellationToken ct = default);
}

public class BrowserService(IJSRuntime jsRuntime) : IBrowserService
{
    public async Task OpenInNewTab(string url, CancellationToken ct = default)
    {
        await jsRuntime.InvokeVoidAsync("open", ct, url, "_blank");
    }
}
