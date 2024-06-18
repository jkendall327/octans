using HydrusReplacement.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydrusReplacement.Client.Pages;

public class IndexModel : PageModel
{
    public List<HashItem> Hashes { get; set; } = new();
    
    public async Task<IActionResult> OnGetAsync()
    {
        var client = new HttpClient();

        var response = await client.GetFromJsonAsync<List<HashItem>>("http://localhost:5185/getAll");

        if (response is null)
        {
            return NotFound();
        }
        
        Hashes = response;

        return Page();
    }
}