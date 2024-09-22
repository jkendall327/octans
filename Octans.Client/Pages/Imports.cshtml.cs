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

        ImportResults = await SendImportRequest(ImportUrls, Files);

        return Page();
    }

    private async Task<List<string>> SendImportRequest(string importUrls, List<IFormFile> files)
    {
        var importItems = new List<ImportItem>();

        // Process URLs
        if (!string.IsNullOrWhiteSpace(importUrls))
        {
            var urls = ImportUrls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            importItems.AddRange(urls.Select(url => new ImportItem { Source = new(url) }));
        }

        // ASP.NET Core doesn't let us get the filepaths natively, so copy them over...
        if (files.Count > 0)
        {
            var uploadPath = _fileSystem.Path.Combine(_environment.WebRootPath, "uploads");
            _fileSystem.Directory.CreateDirectory(uploadPath);

            foreach (var file in files)
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
            return ["Nothing to import."];
        }
        
        var importRequest = new ImportRequest
        {
            Items = importItems,
            DeleteAfterImport = false
        };

        var client = _clientFactory.CreateClient();
        var response = await client.PostAsJsonAsync("http://localhost:5185/import", importRequest);

        if (!response.IsSuccessStatusCode) return ["Failed to process import request."];
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        if (result is null)
        {
            throw new InvalidOperationException("Deserializing import result failed");
        }
            
        var sendImportRequest = result.Results.Select(r => r.Ok ? "Success" : $"Failed: {false}").ToList();
            
        return sendImportRequest;

    }
}