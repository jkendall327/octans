using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Octans.Core.Importing;

namespace Octans.Client.Pages;

public class Imports : PageModel
{
    private readonly IHttpClientFactory _clientFactory;

    public Imports(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    [BindProperty]
    [Required(ErrorMessage = "Please enter at least one URL to import.")]
    public string ImportUrls { get; set; } = string.Empty;

    public List<string>? ImportResults { get; set; }
    
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var urls = ImportUrls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var importItems = urls.Select(url => new ImportItem { Source = new Uri(url) }).ToList();

        var importRequest = new ImportRequest
        {
            Items = importItems,
            DeleteAfterImport = false
        };

        var client = _clientFactory.CreateClient();
        var response = await client.PostAsJsonAsync("http://localhost:5185/import", importRequest);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ImportResult>();
            ImportResults = result?.Results.Select(r => r.Ok ? "Success" : $"Failed: {false}").ToList();
        }
        else
        {
            ImportResults = new List<string> { "Failed to process import request." };
        }

        return Page();
    }
}