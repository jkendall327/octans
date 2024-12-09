using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Octans.Tests;

public static class ServiceCollectionExtensions
{
    public static void ReplaceExistingRegistrationsWith<T>(this IServiceCollection services, T replacement) where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(replacement);
    }
}