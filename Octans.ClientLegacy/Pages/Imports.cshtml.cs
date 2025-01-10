using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Octans.Client.Pages;

internal sealed class Imports : PageModel
{
    private readonly ImportRequestSender _sender;

    public Imports(ImportRequestSender sender)
    {
        _sender = sender;
    }

    [BindProperty]
    public string ImportUrls { get; set; } = string.Empty;

    [BindProperty]
    public List<IFormFile> Files { get; set; } = new();

    public List<string>? ImportResults { get; set; }


    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var results = await _sender.SendImportRequest(ImportUrls, Files);

        if (results.Count is 1 && results.First() == "Nothing to import.")
        {
            ModelState.AddModelError(string.Empty, "Please provide at least one URL or file to import.");
        }
        else
        {
            ImportResults = results;
        }

        return Page();
    }
}