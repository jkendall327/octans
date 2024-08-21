using System.ComponentModel.DataAnnotations;
using System.IO.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Octans.Core.Importing;

namespace Octans.Client.Pages;

public class Imports : PageModel
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly IFileSystem _fileSystem;

    public Imports(IHttpClientFactory clientFactory, IWebHostEnvironment environment, IFileSystem fileSystem)
    {
        _clientFactory = clientFactory;
        _environment = environment;
        _fileSystem = fileSystem;
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

        var importItems = new List<ImportItem>();

        // Process URLs
        if (!string.IsNullOrWhiteSpace(ImportUrls))
        {
            var urls = ImportUrls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            importItems.AddRange(urls.Select(url => new ImportItem { Source = new(url) }));
        }

        // ASP.NET Core doesn't let us get the filepaths natively, so copy them over...
        if (Files.Count > 0)
        {
            var uploadPath = _fileSystem.Path.Combine(_environment.WebRootPath, "uploads");
            _fileSystem.Directory.CreateDirectory(uploadPath);

            foreach (var file in Files)
            {
                if (file.Length <= 0) continue;
                
                var filePath = _fileSystem.Path.Combine(uploadPath, file.FileName);
                await using var stream = _fileSystem.FileStream.New(filePath, FileMode.Create);
                await file.CopyToAsync(stream);
                importItems.Add(new() { Source = new(filePath) });
            }
        }

        if (importItems.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please provide at least one URL or file to import.");
            return Page();
        }
        
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