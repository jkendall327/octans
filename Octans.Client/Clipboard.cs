using Microsoft.JSInterop;

namespace Octans.Client;

public interface IClipboard
{
    ValueTask CopyToClipboardAsync(string text);
    ValueTask<string> ReadFromClipboardAsync();
}

public class Clipboard(IJSRuntime js) :  IClipboard
{
    public async ValueTask CopyToClipboardAsync(string text)
    {
        await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
    }

    public async ValueTask<string> ReadFromClipboardAsync()
    {
        return await js.InvokeAsync<string>("navigator.clipboard.readText");
    }
}
