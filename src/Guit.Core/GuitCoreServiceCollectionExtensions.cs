using Guit.Api;
using Guit.Api.Auth;
using Guit.Core.Services;
using Guit.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guit.Core;

public static class GuitCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API clients, services and view models. The caller must register
    /// <see cref="ITokenStore"/>, <see cref="ILauncher"/> and <see cref="GitHubOptions"/>.
    /// </summary>
    public static IServiceCollection AddGuitCore(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        services.AddGuitApi(primaryHandler);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Session>();
        services.AddSingleton<IAccessTokenProvider>(static provider => provider.GetRequiredService<Session>());

        // View models. Shell-level ones are singletons; page-level ones are transient. Keep sorted.
        services.AddTransient<InboxViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SignInViewModel>();
        return services;
    }
}
