using System.IO.Abstractions;
using Octans.Core;

namespace Octans.Client.Components.Pages;

public class GalleryViewmodel(ServerClient client, SubfolderManager manager)
{
    public List<string> ImagePaths { get; private set; } = new();
    public const int MaxImages = 10;
    public const int ThumbnailWidth = 300;
    public const int ThumbnailHeight = 200;

    public async Task GetAllImages()
    {
        var response = await client.GetAll();

        var hashed = response.Select(x => HashedBytes.FromHashed(x.Hash));

        var paths = hashed
            .Select(hash => manager.GetFilepath(hash))
            .OfType<IFileSystemInfo>()
            .Select(x => x.FullName)
            .ToList();

        ImagePaths = paths.Take(MaxImages).ToList();
    }
}