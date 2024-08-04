using System.IO.Abstractions;
using HydrusReplacement.Core.Importing;

namespace HydrusReplacement.Client;

public class ImportFolderBackgroundService : BackgroundService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IDirectory _directory;
    private readonly IPath _path;
    private readonly ILogger<ImportFolderBackgroundService> _logger;

    private readonly string[] _importFolders;
    
    public ImportFolderBackgroundService(IConfiguration configuration,
        IHttpClientFactory clientFactory,
        IDirectory directory,
        IPath path,
        ILogger<ImportFolderBackgroundService> logger)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _directory = directory;
        _path = path;

        _importFolders = configuration.GetValue<string[]>("importFolders") ?? Array.Empty<string>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

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
            DeleteAfterImport = true,
            FilterData = new()
            {
                AllowedFileTypes = [".jpg", ".jpeg", ".png", ".gif"]
            }
        };
        
        foreach (var folder in _importFolders)
        {
            if (!_directory.Exists(folder))
            {
                _logger.LogWarning("Import folder does not exist: {Folder}", folder);
                continue;
            }

            var imports = _directory
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
        var extension = _path.GetExtension(filePath).ToLowerInvariant();
        return new[] { ".jpg", ".jpeg", ".png", ".gif" }.Contains(extension);
    }
}