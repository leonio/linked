using Linked.App.Services;
using Linked.Core;
using Linked.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Linked.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>The composition root. Resolve from here only in view constructors; everything else takes constructor parameters.</summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddLinkedCore();
        return services.BuildServiceProvider();
    }
}
