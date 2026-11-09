using Triage.Api.Auth;
using Triage.Api.Http;
using Triage.Api.Notifications;
using Triage.Api.Users;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Triage.Api;

public static class TriageApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Refit client. Only generated clients are used (<c>AddRefitGeneratedClient</c>),
    /// so a method the generator cannot build is a build warning (RF006) and never a reflection fallback.
    /// The caller must also register an <see cref="IAccessTokenProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="primaryHandler">Replaces the network handler. Only tests and the AOT smoke app pass this.</param>
    public static IServiceCollection AddTriageApi(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        primaryHandler ??= TriageHttp.CreateHandler;

        services.AddRefitGeneratedClient<IGitHubDeviceFlowApi>(GitHubRefit.CreateSettings())
            .ConfigureHttpClient(static client => TriageHttp.Configure(client, GitHubEndpoints.WebBase))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler);

        // One line per authenticated API interface. Keep sorted.
        services.AddGitHubApi<INotificationsApi>(primaryHandler);
        services.AddGitHubApi<IUsersApi>(primaryHandler);

        return services;
    }

    private static void AddGitHubApi<T>(this IServiceCollection services, Func<HttpMessageHandler> primaryHandler)
        where T : class
        => services.AddRefitGeneratedClient<T>(GitHubRefit.CreateSettings())
            .ConfigureHttpClient(static client => TriageHttp.ConfigureApi(client))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler)
            .AddAuthorizationHeaderValueProvider(static (provider, _, cancellationToken) =>
                provider.GetRequiredService<IAccessTokenProvider>().GetAccessTokenAsync(cancellationToken));
}
