using Guit.Api.Actions;
using Guit.Api.Auth;
using Guit.Api.Http;
using Guit.Api.Issues;
using Guit.Api.Notifications;
using Guit.Api.Pulls;
using Guit.Api.Reactions;
using Guit.Api.Repos;
using Guit.Api.Search;
using Guit.Api.Users;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Guit.Api;

public static class GuitApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Refit client. Only generated clients are used (<c>AddRefitGeneratedClient</c>),
    /// so a method the generator cannot build is a build warning (RF006) and never a reflection fallback.
    /// The caller must also register an <see cref="IAccessTokenProvider"/> and an <see cref="IUnauthorizedHandler"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="primaryHandler">Replaces the network handler. Only tests and the AOT smoke app pass this.</param>
    public static IServiceCollection AddGuitApi(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        primaryHandler ??= GuitHttp.CreateHandler;

        services.AddRefitGeneratedClient<IGitHubDeviceFlowApi>(GitHubRefit.CreateSettings())
            .ConfigureHttpClient(static client => GuitHttp.Configure(client, GitHubEndpoints.WebBase))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler);

        services.AddTransient<UnauthorizedDelegatingHandler>();

        // One line per authenticated API interface. Keep sorted.
        services.AddGitHubApi<IActionsApi>(primaryHandler);
        services.AddGitHubApi<IIssuesApi>(primaryHandler);
        services.AddGitHubApi<INotificationsApi>(primaryHandler);
        services.AddGitHubApi<IPullsApi>(primaryHandler);
        services.AddGitHubApi<IReactionsApi>(primaryHandler);
        services.AddGitHubApi<IReposApi>(primaryHandler);
        services.AddGitHubApi<ISearchApi>(primaryHandler);
        services.AddGitHubApi<IUsersApi>(primaryHandler);

        return services;
    }

    private static void AddGitHubApi<T>(this IServiceCollection services, Func<HttpMessageHandler> primaryHandler)
        where T : class
        => services.AddRefitGeneratedClient<T>(GitHubRefit.CreateSettings())
            .ConfigureHttpClient(static client => GuitHttp.ConfigureApi(client))
            .ConfigurePrimaryHttpMessageHandler(primaryHandler)
            .AddHttpMessageHandler<UnauthorizedDelegatingHandler>()
            .AddAuthorizationHeaderValueProvider(static (provider, _, cancellationToken) =>
                provider.GetRequiredService<IAccessTokenProvider>().GetAccessTokenAsync(cancellationToken));
}
