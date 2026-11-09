using Triage.Api;
using Triage.Api.Auth;
using Triage.Core.Services;
using Triage.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Triage.Core;

public static class TriageCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API clients, services and view models. The caller must register
    /// <see cref="ITokenStore"/>, <see cref="ILauncher"/> and <see cref="GitHubOptions"/>.
    /// </summary>
    public static IServiceCollection AddTriageCore(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        services.AddTriageApi(primaryHandler);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Session>();
        services.AddSingleton<IAccessTokenProvider>(static provider => provider.GetRequiredService<Session>());

        // View models. Shell-level ones are singletons; page-level ones are transient. Keep sorted.
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SignInViewModel>();
        return services;
    }
}
