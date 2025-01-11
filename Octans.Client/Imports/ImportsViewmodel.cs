using System.IO.Abstractions;
using Microsoft.AspNetCore.Components.Forms;
using Octans.Core.Communication;
using Octans.Core.Importing;

namespace Octans.Client;

public class ImportsViewmodel(IFileSystem fileSystem, IWebHostEnvironment environment, IOctansApi client)
{
    public async Task SendLocalFilesToServer(IReadOnlyList<IBrowserFile> files)
    {
        if (!files.Any()) return;

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
}