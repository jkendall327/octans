namespace Octans.Client.Pages;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

internal class ImageDetailModel : PageModel
{
    public string? ImagePath { get; private set; }
    public string? FileName { get; private set; }

    public IActionResult OnGet(string path)
    {
        path = Uri.UnescapeDataString(path);

        if (string.IsNullOrEmpty(path))
        {
            return NotFound();
        }

        ImagePath = path;
        FileName = Path.GetFileName(path);
        return Page();
    }
}