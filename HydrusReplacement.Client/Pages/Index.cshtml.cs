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

        foreach (var hash in response)
        {
            var subfolder = _subfolderManager.GetSubfolder(hash.Hash);

            Uris.Add(subfolder);

            var hex = Convert.ToHexString(hash.Hash);
            
            var file = new DirectoryInfo(subfolder.AbsolutePath)
                .EnumerateFiles()
                .SingleOrDefault(f => f.Name.Replace(f.Extension, string.Empty) == hex);

            if (file is null) continue;
            
            var bytes = await System.IO.File.ReadAllBytesAsync(file.FullName);
                
            Images.Add(bytes);
        }

        return Page();
    }
}