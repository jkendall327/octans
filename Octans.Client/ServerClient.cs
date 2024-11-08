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
        return await client.GetFromJsonAsync<List<HashItem>>("getAll");
    }
}