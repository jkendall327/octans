using Microsoft.JSInterop;

namespace Octans.Client;

public interface IClipboard
{
    ValueTask CopyToClipboardAsync(string text);
}

public class Clipboard(IJSRuntime js) :  IClipboard
{
    public async ValueTask CopyToClipboardAsync(string text)
    {
        await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
    }
}