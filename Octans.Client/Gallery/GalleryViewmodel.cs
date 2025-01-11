using System.IO.Abstractions;
using Octans.Core;
using Octans.Core.Communication;

namespace Octans.Client.Components.Pages;

public class GalleryViewmodel(IOctansApi client, SubfolderManager manager)
{
    public List<string> ImagePaths { get; private set; } = new();
    public const int MaxImages = 10;
    public const int ThumbnailWidth = 300;
    public const int ThumbnailHeight = 200;

    public async Task GetAllImages()
    {
        var response = await client.GetAllFiles();

        var items = response.Content ?? throw new ArgumentNullException(nameof(response.Content));
        
        var hashed = items.Select(x => HashedBytes.FromHashed(x.Hash));

        var paths = hashed
            .Select(manager.GetFilepath)
            .OfType<IFileSystemInfo>()
            .Select(x => x.FullName)
            .ToList();

        ImagePaths = paths.Take(MaxImages).ToList();
    }
}