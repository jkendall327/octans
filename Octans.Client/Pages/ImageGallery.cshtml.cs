using System.IO.Abstractions;
using Octans.Core;

namespace Octans.Client.Pages;

using Microsoft.AspNetCore.Mvc.RazorPages;

internal class ImageGalleryModel : PageModel
{
    private readonly ServerClient _client;
    private readonly SubfolderManager _subfolderManager;

    public ImageGalleryModel(ServerClient client, SubfolderManager subfolderManager)
    {
        _client = client;
        _subfolderManager = subfolderManager;
    }

    public List<string> ImagePaths { get; private set; } = new();
    public const int MaxImages = 10;
    public const int ThumbnailWidth = 300;
    public const int ThumbnailHeight = 200;

    public async Task OnGetAsync()
    {
        var response = await _client.GetAll();

        var hashed = response.Select(x => HashedBytes.FromHashed(x.Hash));

        var paths = hashed
            .Select(hash => _subfolderManager.GetFilepath(hash))
            .OfType<IFileSystemInfo>()
            .Select(x => x.FullName)
            .ToList();

        ImagePaths = paths.Take(MaxImages).ToList();
    }
}