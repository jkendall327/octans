using Octans.Core.Models;

namespace Octans.Client;

public class ServerClient(HttpClient client)
{
    public async Task<List<HashItem>?> GetAll()
    {
        return await client.GetFromJsonAsync<List<HashItem>>("getAll");
    }
}