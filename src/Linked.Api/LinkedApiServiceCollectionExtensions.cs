using Linked.Api.Auth;
using Linked.Api.Http;
using Linked.Api.Identity;
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

        services.AddRefitGeneratedClient<ILinkedInOAuthApi>(LinkedInOAuthJsonContext.Default)
            .ConfigureHttpClient(static client => LinkedHttp.Configure(client, LinkedInEndpoints.OAuthBase))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler);

        services.AddRefitGeneratedClient<IIdentityApi>(LinkedInOAuthJsonContext.Default)
            .ConfigureHttpClient(static client => LinkedHttp.Configure(client, LinkedInEndpoints.ApiBase))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler)
            .AddAuthorizationHeaderValueProvider(static (provider, _, cancellationToken) =>
                provider.GetRequiredService<IAccessTokenProvider>().GetAccessTokenAsync(cancellationToken));

        return services;
    }
}
