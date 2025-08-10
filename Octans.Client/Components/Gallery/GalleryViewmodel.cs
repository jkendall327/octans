using System.IO.Abstractions;
using Octans.Core;
using Octans.Core.Querying;

namespace Octans.Client.Components.Pages;

public sealed class GalleryViewmodel(QueryService service, SubfolderManager manager) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    public List<string> ImagePaths { get; private set; } = [];
    public const int ThumbnailWidth = 300;
    public const int ThumbnailHeight = 200;

    public bool Searching { get; set; }

    public async Task OnQueryChanged(List<QueryParameter> arg)
    {
        Searching = true;

        try
        {

            var results = await service.Query(arg.Select(s => s.Raw));

            var paths = results
                .Select(s => HashedBytes.FromUnhashed(s.Hash))
                .Select(manager.GetFilepath)
                .OfType<IFileSystemInfo>()
                .Select(x => x.FullName)
                .ToList();

            ImagePaths = paths.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);

            throw;
        }
        finally
        {
            Searching = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}