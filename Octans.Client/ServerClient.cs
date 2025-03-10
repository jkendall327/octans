using Octans.Core.Importing;
using Octans.Core.Models;

namespace Octans.Client;

public class ServerClient(HttpClient client)
{
    public async Task<bool> IsHealthy()
    {
        var response = await client.GetAsync(new Uri("/health"));

        var result = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode && result is "Healthy";
    }

    public async Task<List<HashItem>> GetAll()
    {
        var result = await client.GetFromJsonAsync<List<HashItem>>("files");

        return result ?? throw new InvalidOperationException("The hash items deserialized to null");
    }

    public async Task<ImportResult> Import(ImportRequest request, CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsJsonAsync("files", request, cancellationToken);

        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Failed to process import request.");

        var result = await response.Content.ReadFromJsonAsync<ImportResult>(cancellationToken);

        return result ?? throw new InvalidOperationException("The import result deserialized to null");
    }
}