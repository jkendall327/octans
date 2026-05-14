using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Octans.Client;

public interface IBrowserStorage
{
    Task<T?> GetFromLocalStorageAsync<T>(string key);
    Task SetToLocalStorageAsync<T>(string key, T state) where T : notnull;

    Task ToSessionAsync<T>(string purpose, string key, T state) where T : notnull;
    Task<T?> FromSessionAsync<T>(string purpose, string key);
}

public class BrowserStorage(
    ProtectedLocalStorage local,
    ProtectedSessionStorage session,
    ILogger<BrowserStorage> logger) : IBrowserStorage
{
    public async Task<T?> GetFromLocalStorageAsync<T>(string key)
    {
        try
        {
            var result = await local.GetAsync<T>(key);
            return result.Value;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error pulling from local storage: {Key}", key);

            try
            {
                await local.DeleteAsync(key);
            }
            catch
            {
                return default;
            }

            return default;
        }
    }

    public async Task SetToLocalStorageAsync<T>(string key, T state) where T : notnull
    {
        try
        {
            await local.SetAsync(key, state);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error setting local storage: {Key}", key);
        }
    }

    public async Task ToSessionAsync<T>(string purpose, string key, T state) where T : notnull
    {
        await session.SetAsync(purpose, key, state);
    }

    public async Task<T?> FromSessionAsync<T>(string purpose, string key)
    {
        try
        {
            var result = await session.GetAsync<T>(purpose, key);
            return result.Value;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error pulling from session storage: {Key}", key);

            try
            {
                await session.DeleteAsync(key);
            }
            catch
            {
                return default;
            }

            return default;
        }
    }
}