using System.IO.Abstractions;
using Microsoft.AspNetCore.Components.Forms;
using Octans.Core.Communication;
using Octans.Core.Importing;

namespace Octans.Client;

public interface IRawUrlImportViewmodel
{
    Task SendUrlsToServer(List<string> urls, ImportType importType, bool allowReimportDeleted);
    string UrlInput { get; set; }
    bool AllowReimportDeleted { get; set; }
}

public interface ILocalFileImportViewmodel
{
    Task SendLocalFilesToServer(IReadOnlyList<IBrowserFile> files);
    IReadOnlyList<IBrowserFile> LocalFiles { get; set; }
}

public class ImportsViewmodel(
    IFileSystem fileSystem,
    IWebHostEnvironment environment,
    IOctansApi client,
    ILogger<ImportsViewmodel> logger) : IRawUrlImportViewmodel, ILocalFileImportViewmodel
{
    public string UrlInput { get; set; } = string.Empty;
    public bool AllowReimportDeleted { get; set; }
    public IReadOnlyList<IBrowserFile> LocalFiles { get; set; } = [];

    
    public async Task SendLocalFilesToServer(IReadOnlyList<IBrowserFile> files)
    {
        if (!files.Any()) return;

        logger.LogInformation("Sending {Count} files to server", files.Count);

        var uploadPath = fileSystem.Path.Combine(environment.WebRootPath, "uploads");
        fileSystem.Directory.CreateDirectory(uploadPath);

        var items = new List<ImportItem>();

        foreach (var file in files)
        {
            if (file.Size <= 0) continue;

            var filePath = fileSystem.Path.Combine(uploadPath, file.Name);

            await using var stream = fileSystem.FileStream.New(filePath, FileMode.Create);
            await using var source = file.OpenReadStream();
            await source.CopyToAsync(stream);

            items.Add(new() { Source = new(filePath) });
        }

        var request = new ImportRequest
        {
            ImportType = ImportType.File,
            Items = items,
            DeleteAfterImport = false
        };

        await client.ProcessImport(request);
    }

    public async Task SendUrlsToServer(List<string> urls, ImportType importType, bool allowReimportDeleted)
    {
        if (!urls.Any()) return;

        logger.LogInformation("Sending {Count} URLs to server with type {ImportType}", urls.Count, importType);

        var importItems = urls
            .Select(url => new ImportItem { Source = new Uri(url) })
            .ToList();

        var request = new ImportRequest
        {
            ImportType = importType,
            Items = importItems,
            DeleteAfterImport = false,
            AllowReimportDeleted = allowReimportDeleted
        };

        await client.ProcessImport(request);
    }
}
