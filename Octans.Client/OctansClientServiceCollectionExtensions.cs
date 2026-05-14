namespace Octans.Client;

public static class OctansClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddOctansClient(this IServiceCollection services)
    {
        return services.AddHttpClient<IOctansClient, OctansClient>();
    }

    public static IHttpClientBuilder AddOctansClient(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        return services.AddHttpClient<IOctansClient, OctansClient>(configureClient);
    }

    public static IHttpClientBuilder AddOctansClient(
        this IServiceCollection services,
        Action<IServiceProvider, HttpClient> configureClient)
    {
        return services.AddHttpClient<IOctansClient, OctansClient>(configureClient);
    }
}
