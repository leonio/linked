using Linked.Api;
using Linked.Api.Auth;
using Linked.Core.Services;
using Linked.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Linked.Core;

public static class LinkedCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API clients and view models. The caller must register an
    /// <see cref="ISessionService"/> implementation first.
    /// </summary>
    public static IServiceCollection AddLinkedCore(this IServiceCollection services, Func<HttpMessageHandler>? primaryHandler = null)
    {
        services.AddLinkedApi(primaryHandler);
        services.AddSingleton<IAccessTokenProvider>(static provider => provider.GetRequiredService<ISessionService>());
        services.AddSingleton<ShellViewModel>();
        return services;
    }
}
