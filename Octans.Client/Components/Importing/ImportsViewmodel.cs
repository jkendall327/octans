using System.IO.Abstractions;
using Microsoft.AspNetCore.Components.Forms;
using Octans.Core.Importing;
using Octans.Core.Tags;

namespace Octans.Client.Components.Importing;

public interface IRawUrlImportViewmodel
{
    Task SendUrlsToServer();
    string RawInputs { get; set; }
    bool AllowReimportDeleted { get; set; }
    bool AutoArchive { get; set; }
    Guid? LastJobId { get; }
    TagChooser.TagChooserResult? TagResult { get; set; }
}

public interface ILocalFileImportViewmodel
{
    Guid? LastJobId { get; }
    Task SendLocalFilesToServer(Dictionary<string, IEnumerable<TagModel>>? tags = null);
    IReadOnlyList<IBrowserFile> LocalFiles { get; set; }
    bool AutoArchive { get; set; }
}

public class LocalFileImportViewmodel(
    IFileSystem fileSystem,
    IWebHostEnvironment environment,
    IOctansClient client,
    ILogger<LocalFileImportViewmodel> logger) : ILocalFileImportViewmodel
{
    public IReadOnlyList<IBrowserFile> LocalFiles { get; set; } = [];

    public Guid? LastJobId { get; private set; }
    public bool AutoArchive { get; set; }

    public async Task SendLocalFilesToServer(Dictionary<string, IEnumerable<TagModel>>? tags = null)
    {
        if (!LocalFiles.Any()) return;

        logger.LogInformation("Sending {Count} files to server", LocalFiles.Count);

        var uploadPath = fileSystem.Path.Combine(environment.WebRootPath, "uploads");
        fileSystem.Directory.CreateDirectory(uploadPath);

        var sources = new List<string>();
        var tagsBySource = new Dictionary<string, ICollection<TagModel>>();

        foreach (var file in LocalFiles)
        {
            if (file.Size <= 0) continue;

            var filePath = fileSystem.Path.Combine(uploadPath, file.Name);

            await using var stream = fileSystem.FileStream.New(filePath, FileMode.Create);
            await using var source = file.OpenReadStream();
            await source.CopyToAsync(stream);

            if (tags is not null && tags.TryGetValue(file.Name, out var fileTags))
            {
                tagsBySource[filePath] = fileTags.ToList();
            }

            sources.Add(filePath);
        }

        var request = new ImportRequest
        {
            ImportType = ImportType.File,
            Items = sources.Select(source => new ImportItem
            {
                Filepath = source,
                Tags = tagsBySource.GetValueOrDefault(source)
            }).ToList(),
            DeleteAfterImport = false,
            AutoArchive = AutoArchive
        };

        var created = await client.CreateImportJobAsync(new()
        {
            ImportType = request.ImportType,
            Sources = sources,
            DeleteAfterImport = request.DeleteAfterImport,
            AutoArchive = request.AutoArchive,
            TagsBySource = tagsBySource
        });

        LastJobId = created.JobId;

        LocalFiles = [];
    }
}

public class RawUrlImportViewmodel(
    IOctansClient client,
    ILogger<RawUrlImportViewmodel> logger) : IRawUrlImportViewmodel
{
    public string RawInputs { get; set; } = string.Empty;
    public bool AllowReimportDeleted { get; set; }
    public bool AutoArchive { get; set; }
    public Guid? LastJobId { get; private set; }
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

            var tagsBySource = new Dictionary<string, ICollection<TagModel>>();
            foreach (var url in urls)
            {
                if (TagResult is not null)
                {
                    var viewTags = TagResult.GetTagsSinglePath(url);
                    tagsBySource[url] = viewTags.Select(t => new TagModel(t.Namespace, t.Subtag)).ToList();
                }
            }

            var request = new ImportRequest
            {
                ImportType = ImportType.RawUrl,
                Items = urls.Select(url => new ImportItem
                {
                    Url = new(url),
                    Tags = tagsBySource.GetValueOrDefault(url)
                }).ToList(),
                DeleteAfterImport = false,
                AllowReimportDeleted = AllowReimportDeleted,
                AutoArchive = AutoArchive
            };

            var created = await client.CreateImportJobAsync(new()
            {
                ImportType = request.ImportType,
                Sources = urls,
                DeleteAfterImport = request.DeleteAfterImport,
                AllowReimportDeleted = request.AllowReimportDeleted,
                AutoArchive = request.AutoArchive,
                TagsBySource = tagsBySource
            });

            LastJobId = created.JobId;

            RawInputs = string.Empty;
        }
    }
}
