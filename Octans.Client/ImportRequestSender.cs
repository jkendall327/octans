using System.IO.Abstractions;
using Octans.Core.Importing;

namespace Octans.Client;

public class ImportRequestSender
{
    private readonly IFileSystem _fileSystem;
    private readonly IWebHostEnvironment _environment;
    private readonly ServerClient _client;

    public ImportRequestSender(IFileSystem fileSystem, IWebHostEnvironment environment, ServerClient client)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _client = client;
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

        // TODO: re-enable file imports.
        // ASP.NET Core doesn't let us get the filepaths natively, so copy them over...
        if (files.Count > 0 && false)
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
            ImportType = ImportType.RawUrl,
            Items = importItems,
            DeleteAfterImport = false
        };

        var response = await _client.Import(request);

        var results = response.Results.Select(r => r.Ok ? "Success" : $"Failed: {false}").ToList();

        return results;
    }
}