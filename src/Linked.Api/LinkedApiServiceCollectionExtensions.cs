using System.Text.Json.Serialization;
using Linked.Api.Auth;
using Linked.Api.Http;
using Linked.Api.Users;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Linked.Api;

public static class LinkedApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Refit client. Only generated clients are used (<c>AddRefitGeneratedClient</c>),
    /// so a method the generator cannot build is a build warning (RF006) and never a reflection fallback.
    /// The caller must also register an <see cref="IAccessTokenProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="primaryHandler">Replaces the network handler. Only tests and the AOT smoke app pass this.</param>
    public static IServiceCollection AddLinkedApi(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        primaryHandler ??= LinkedHttp.CreateHandler;

        services.AddRefitGeneratedClient<IGitHubDeviceFlowApi>(GitHubJsonContext.Default)
            .ConfigureHttpClient(static client => LinkedHttp.Configure(client, GitHubEndpoints.WebBase))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler);

        // One line per authenticated API interface. Keep sorted.
        services.AddGitHubApi<IUsersApi>(GitHubJsonContext.Default, primaryHandler);

        return services;
    }

    private static void AddGitHubApi<T>(this IServiceCollection services, JsonSerializerContext context, Func<HttpMessageHandler> primaryHandler)
        where T : class
        => services.AddRefitGeneratedClient<T>(context)
            .ConfigureHttpClient(static client => LinkedHttp.ConfigureApi(client))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler)
            .AddAuthorizationHeaderValueProvider(static (provider, _, cancellationToken) =>
                provider.GetRequiredService<IAccessTokenProvider>().GetAccessTokenAsync(cancellationToken));
}
