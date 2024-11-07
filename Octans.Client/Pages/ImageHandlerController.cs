namespace Octans.Client.Pages;

using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

public class ImageHandlerController : Controller
{
    private readonly ILogger<ImageHandlerController> _logger;

    public ImageHandlerController(ILogger<ImageHandlerController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetImage(string path)
    {
        try
        {
            using var image = Image.Load(path);
            
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
}