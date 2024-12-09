namespace Octans.Client.Pages;

using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

internal sealed class ImageHandlerController : Controller
{
    private readonly ILogger<ImageHandlerController> _logger;

    public ImageHandlerController(ILogger<ImageHandlerController> logger)
    {
        _logger = logger;
    }

    [HttpGet("GetImage")]
    [Route("[controller]/GetImage")]
    public IActionResult GetImage(string path)
    {
        try
        {
            // TODO: this should just be pulling the already-created thumbnail from the filesystem.

            var decodedPath = Uri.UnescapeDataString(path);

            using var image = Image.Load(decodedPath);

            // Resize the image while maintaining aspect ratio
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(ImageGalleryModel.ThumbnailWidth, ImageGalleryModel.ThumbnailHeight),
                Mode = ResizeMode.Max
            }));

            // Convert to byte array and return
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms);
            return File(ms.ToArray(), "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image at path: {Path}", path);
            return NotFound();
        }
    }

    [HttpGet("GetFullImage")]
    [Route("[controller]/GetFullImage")]
    public IActionResult GetFullImage(string path)
    {
        try
        {
            var decodedPath = Uri.UnescapeDataString(path);

            using var image = Image.Load(decodedPath);

            // For full-size images, limit max dimensions while maintaining aspect ratio
            image.Mutate(x => x
                .Resize(new ResizeOptions
                {
                    Size = new Size(1920, 1080), // Max dimensions
                    Mode = ResizeMode.Max
                }));

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms);
            return File(ms.ToArray(), "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing full image at path: {Path}", path);
            return NotFound();
        }
    }
}