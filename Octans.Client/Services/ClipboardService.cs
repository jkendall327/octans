using Microsoft.JSInterop;

namespace Octans.Client.Services;

public interface IClipboardService
{
    Task CopyToClipboard(string content, CancellationToken ct = default);
    Task<string> GetFromClipboard(CancellationToken ct = default);
}

public class ClipboardService(IJSRuntime js) : IClipboardService
{
    public async Task CopyToClipboard(string content, CancellationToken ct = default)
    {
        await js.InvokeVoidAsync("navigator.clipboard.writeText", ct, content);
    }

    public async Task<string> GetFromClipboard(CancellationToken ct = default)
    {
        return await js.InvokeAsync<string>("navigator.clipboard.readText", ct);
    }
}
