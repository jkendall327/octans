using System.IO.Abstractions;
using Octans.Core;
using Octans.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Octans.Client.Pages;

public class IndexModel : PageModel
{
    public List<IFileInfo> Filepaths { get; set; } = new();

    private readonly SubfolderManager _subfolderManager;
    private readonly ServerClient _client;
    
    public IndexModel(SubfolderManager subfolderManager, ServerClient client)
    {
        _subfolderManager = subfolderManager;
        _client = client;
    }
    
    public async Task<IActionResult> OnGetFilePreview(Uri path)
    {
        var bytes = await System.IO.File.ReadAllBytesAsync(path.AbsolutePath);
        return File(bytes, "application/octet-stream", "1.jpg");
    }
    
    public async Task<IActionResult> OnGetAsync()
    {
        var response = await _client.GetAll();

        if (response is null)
        {
            return NotFound();
        }

        var hashed = response.Select(x => new HashedBytes(x.Hash, ItemType.File, prehashed: true));

        Filepaths = hashed
            .Select(hash => _subfolderManager.GetFilepath(hash))
            .OfType<IFileInfo>()
            .ToList();

        return Page();
    }
}