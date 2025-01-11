using System.IO.Abstractions;
using Octans.Core.Communication;
using Octans.Core.Importing;

namespace Octans.Client;

public class ImportRequestSender(IFileSystem fileSystem, IWebHostEnvironment environment, IOctansApi client)
{
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
            var uploadPath = fileSystem.Path.Combine(environment.WebRootPath, "uploads");
            fileSystem.Directory.CreateDirectory(uploadPath);

            foreach (var file in files)
            {
                if (file.Length <= 0) continue;

                var filePath = fileSystem.Path.Combine(uploadPath, file.FileName);
                await using var stream = fileSystem.FileStream.New(filePath, FileMode.Create);
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

        var response = await client.ProcessImport(request);

        var content = response.Content ?? throw new InvalidOperationException();
        
        var results = content.Results.Select(r => r.Ok ? "Success" : $"Failed: {false}").ToList();

        return results;
    }
}