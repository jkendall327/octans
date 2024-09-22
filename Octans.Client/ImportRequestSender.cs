using System.IO.Abstractions;
using Octans.Core.Importing;

namespace Octans.Client;

public class ImportRequestSender
{
    private readonly IFileSystem _fileSystem;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _clientFactory;

    public ImportRequestSender(IWebHostEnvironment environment, IHttpClientFactory clientFactory, IFileSystem fileSystem)
    {
        _environment = environment;
        _clientFactory = clientFactory;
        _fileSystem = fileSystem;
    }

    public async Task<List<string>> SendImportRequest(string importUrls, List<IFormFile> files)
    {
        var importItems = new List<ImportItem>();

        // Process URLs
        if (!string.IsNullOrWhiteSpace(importUrls))
        {
            char[]? separator = ['\r', '\n'];
            var urls = importUrls.Split(separator, StringSplitOptions.RemoveEmptyEntries);
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
            return ["Nothing to import."];
        }
        
        var request = new ImportRequest
        {
            Items = importItems,
            DeleteAfterImport = false
        };

        var client = _clientFactory.CreateClient("ServerApi");
        var response = await client.PostAsJsonAsync("import", request);

        if (!response.IsSuccessStatusCode) return ["Failed to process import request."];
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        if (result is null)
        {
            throw new InvalidOperationException("Deserializing import result failed");
        }
            
        var results = result.Results.Select(r => r.Ok ? "Success" : $"Failed: {false}").ToList();
            
        return results;
    }
}