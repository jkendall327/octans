using Octans.Core.Importing;
using Octans.Core.Models;

namespace Octans.Client;

public class ServerClient(HttpClient client)
{
    public async Task<bool> IsHealthy()
    {
        var response = await client.GetAsync("/health");
        
        var result = await response.Content.ReadAsStringAsync();
        
        return response.IsSuccessStatusCode && result is "Healthy";
    }
    
    public async Task<List<HashItem>?> GetAll()
    {
        return await client.GetFromJsonAsync<List<HashItem>>("files");
    }

    public async Task<ImportResult> Import(ImportRequest request)
    {
        var response = await client.PostAsJsonAsync<ImportRequest>("files", request);

        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Failed to process import request.");
        
        return await response.Content.ReadFromJsonAsync<ImportResult>();
    }
}