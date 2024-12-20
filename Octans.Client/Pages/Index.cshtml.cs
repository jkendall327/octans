using System.IO.Abstractions;
using Octans.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Octans.Client.Pages;

internal sealed class IndexModel : PageModel
{
    public List<IFileInfo> Filepaths { get; private set; } = new();

    private readonly SubfolderManager _subfolderManager;
    private readonly ServerClient _client;

    public IndexModel(SubfolderManager subfolderManager, ServerClient client)
    {
        _subfolderManager = subfolderManager;
        _client = client;
    }

    public IActionResult OnGetFilePreview(Uri path)
    {
        return PhysicalFile(path.ToString(), "image/jpeg");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var response = await _client.GetAll();

        if (response is null)
        {
            return NotFound();
        }

        var hashed = response.Select(x => HashedBytes.FromHashed(x.Hash));

        Filepaths = hashed
            .Select(hash => _subfolderManager.GetFilepath(hash))
            .OfType<IFileInfo>()
            .ToList();

        return Page();
    }
}