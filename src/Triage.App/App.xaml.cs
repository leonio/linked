using Triage.App.Services;
using Triage.Core;
using Triage.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Triage.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>
    /// The composition root. Resolve from here only in view constructors (windows, pages, user controls).
    /// Everything else takes constructor parameters.
    /// </summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();
        services.AddSingleton(new GitHubOptions(AppConfig.GitHubClientId));
        services.AddSingleton<ITokenStore, PasswordVaultTokenStore>();
        services.AddSingleton<ILauncher, Launcher>();
        services.AddTriageCore();
        return services.BuildServiceProvider();
    }
}
