using System.IO.Abstractions;
using Octans.Core.Importing;

namespace Octans.Client;

internal sealed class ImportFolderBackgroundService : BackgroundService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ImportFolderBackgroundService> _logger;

    private readonly string[] _importFolders;
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif"];

    public ImportFolderBackgroundService(IConfiguration configuration,
        IHttpClientFactory clientFactory,
        IFileSystem fileSystem,
        ILogger<ImportFolderBackgroundService> logger)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _fileSystem = fileSystem;

        _importFolders = configuration.GetValue<string[]>("importFolders") ?? [];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.LogInformation("Checking for files in import folders...");

            await ScanAndImportFolders(stoppingToken);
        }
    }

    private async Task ScanAndImportFolders(CancellationToken stoppingToken)
    {
        var request = new ImportRequest
        {
            Items = new(),
            ImportType = ImportType.File,
            DeleteAfterImport = true,
            FilterData = new()
            {
                AllowedFileTypes = [".jpg", ".jpeg", ".png", ".gif"]
            }
        };

        foreach (var folder in _importFolders)
        {
            if (!_fileSystem.Directory.Exists(folder))
            {
                _logger.LogWarning("Import folder does not exist: {Folder}", folder);
                continue;
            }

            var imports = _fileSystem.Directory
                .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsImageFile)
                .Select(file => new ImportItem
                {
                    Source = new(file)
                }).ToList();

            request.Items.AddRange(imports);
        }

        await SendImportRequest(request, stoppingToken);
    }

    private async Task SendImportRequest(ImportRequest importRequest, CancellationToken stoppingToken)
    {
        var client = _clientFactory.CreateClient("ServerApi");

        try
        {
            var response = await client.PostAsJsonAsync("import", importRequest, stoppingToken);

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Sent import request for {ImportCount} items", importRequest.Items.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error sending import request to API");
        }
    }

    private bool IsImageFile(string filePath)
    {
        var extension = _fileSystem.Path.GetExtension(filePath).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }
}