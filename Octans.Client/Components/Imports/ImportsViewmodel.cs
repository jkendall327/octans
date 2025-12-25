using System.IO.Abstractions;
using Microsoft.AspNetCore.Components.Forms;
using Octans.Core;
using Octans.Core.Communication;
using Octans.Core.Importing;

namespace Octans.Client;

using Octans.Client.Components.Imports;

public interface IRawUrlImportViewmodel
{
    Task SendUrlsToServer();
    string RawInputs { get; set; }
    bool AllowReimportDeleted { get; set; }
    TagChooser.TagChooserResult? TagResult { get; set; }
}

public interface ILocalFileImportViewmodel
{
    ImportResult? Result { get; }
    Task SendLocalFilesToServer(Dictionary<string, IEnumerable<TagModel>>? tags = null);
    IReadOnlyList<IBrowserFile> LocalFiles { get; set; }
}

public class LocalFileImportViewmodel(
    IFileSystem fileSystem,
    IWebHostEnvironment environment,
    IImporter importer,
    ILogger<LocalFileImportViewmodel> logger) : ILocalFileImportViewmodel
{
    public IReadOnlyList<IBrowserFile> LocalFiles { get; set; } = [];

    public ImportResult? Result { get; private set; }

    public async Task SendLocalFilesToServer(Dictionary<string, IEnumerable<TagModel>>? tags = null)
    {
        if (!LocalFiles.Any()) return;

        logger.LogInformation("Sending {Count} files to server", LocalFiles.Count);

        var uploadPath = fileSystem.Path.Combine(environment.WebRootPath, "uploads");
        fileSystem.Directory.CreateDirectory(uploadPath);

        var items = new List<ImportItem>();

        foreach (var file in LocalFiles)
        {
            if (file.Size <= 0) continue;

            var filePath = fileSystem.Path.Combine(uploadPath, file.Name);

            await using var stream = fileSystem.FileStream.New(filePath, FileMode.Create);
            await using var source = file.OpenReadStream();
            await source.CopyToAsync(stream);

            var item = new ImportItem { Filepath = filePath };
            if (tags is not null && tags.TryGetValue(file.Name, out var fileTags))
            {
                item = new ImportItem
                {
                    Filepath = filePath,
                    Tags = fileTags.ToList()
                };
            }

            items.Add(item);
        }

        var request = new ImportRequest
        {
            ImportType = ImportType.File,
            Items = items,
            DeleteAfterImport = false
        };

        Result = await importer.ProcessImport(request);

        LocalFiles = [];
    }
}

public class RawUrlImportViewmodel(
    IOctansApi client,
    ILogger<RawUrlImportViewmodel> logger) : IRawUrlImportViewmodel
{
    public string RawInputs { get; set; } = string.Empty;
    public bool AllowReimportDeleted { get; set; }
    public TagChooser.TagChooserResult? TagResult { get; set; }

    public async Task SendUrlsToServer()
    {
        if (string.IsNullOrWhiteSpace(RawInputs))
            return;

        var urls = RawInputs
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(url => url.Trim())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();

        if (urls.Count > 0)
        {
            logger.LogInformation("Sending {Count} URLs to server with type {ImportType}", urls.Count, ImportType.RawUrl);

            var importItems = new List<ImportItem>();
            foreach (var url in urls)
            {
                ICollection<TagModel>? tags = null;
                if (TagResult is not null)
                {
                    var viewTags = TagResult.GetTagsSinglePath(url);
                    tags = viewTags.Select(t => new TagModel(t.Namespace, t.Subtag)).ToList();
                }

                var item = new ImportItem
                {
                    Url = new(url),
                    Tags = tags
                };
                importItems.Add(item);
            }

            var request = new ImportRequest
            {
                ImportType = ImportType.RawUrl,
                Items = importItems,
                DeleteAfterImport = false,
                AllowReimportDeleted = AllowReimportDeleted
            };

            await client.ProcessImport(request);

            RawInputs = string.Empty;
        }
    }
}
