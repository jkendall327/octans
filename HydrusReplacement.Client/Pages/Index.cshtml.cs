using System.IO.Abstractions;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HydrusReplacement.Client.Pages;

public class IndexModel : PageModel
{
    public List<IFileInfo> Filepaths { get; set; } = new();

    private readonly SubfolderManager _subfolderManager;
    private readonly IHttpClientFactory _httpClientFactory;
    
    public IndexModel(SubfolderManager subfolderManager, IHttpClientFactory httpClientFactory)
    {
        _subfolderManager = subfolderManager;
        _httpClientFactory = httpClientFactory;
    }
    
    public async Task<IActionResult> OnGetFilePreview(Uri path)
    {
        var bytes = await System.IO.File.ReadAllBytesAsync(path.AbsolutePath);
        return File(bytes, "application/octet-stream", "1.jpg");
    }
    
    public async Task<IActionResult> OnGetAsync()
    {
        var client = _httpClientFactory.CreateClient();
        
        // TODO: don't hardcode this
        var response = await client.GetFromJsonAsync<List<HashItem>>("http://localhost:5185/getAll");

        if (response is null)
        {
            return NotFound();
        }

        var hashed = response.Select(x => new HashedBytes(x.Hash, ItemType.File));

        Filepaths = hashed
            .Select(hash => _subfolderManager.GetFilepath(hash))
            .OfType<IFileInfo>()
            .ToList();

        return Page();
    }
}