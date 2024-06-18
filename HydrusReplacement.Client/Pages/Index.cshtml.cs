using HydrusReplacement.Server;
using HydrusReplacement.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydrusReplacement.Client.Pages;

public class IndexModel : PageModel
{
    public List<Uri> Uris { get; set; } = new();
    public List<byte[]> Images { get; set; } = new();

    private readonly SubfolderManager _subfolderManager;

    public IndexModel(SubfolderManager subfolderManager)
    {
        _subfolderManager = subfolderManager;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var client = new HttpClient();

        // TODO: don't hardcode this
        var response = await client.GetFromJsonAsync<List<HashItem>>("http://localhost:5185/getAll");

        if (response is null)
        {
            return NotFound();
        }

        var uris = response
            .Select(x => _subfolderManager.GetSubfolder(x.Hash))
            .ToList();

        Uris = uris;

        var tasks = uris.Select(x => System.IO.File.ReadAllBytesAsync(x.AbsolutePath)).ToArray();

        await Task.WhenAll(tasks);

        Images = tasks.Select(x => x.Result).ToList();
        
        return Page();
    }
}