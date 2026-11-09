using Linked.Api;
using Linked.Api.Auth;
using Linked.Core.Services;
using Linked.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Linked.Core;

public static class LinkedCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API clients, services and view models. The caller must register
    /// <see cref="ITokenStore"/>, <see cref="ILauncher"/> and <see cref="GitHubOptions"/>.
    /// </summary>
    public static IServiceCollection AddLinkedCore(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        services.AddLinkedApi(primaryHandler);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Session>();
        services.AddSingleton<IAccessTokenProvider>(static provider => provider.GetRequiredService<Session>());

        // View models. Shell-level ones are singletons; page-level ones are transient. Keep sorted.
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SignInViewModel>();
        return services;
    }
}
