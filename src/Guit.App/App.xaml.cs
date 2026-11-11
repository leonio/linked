using Guit.App.Services;
using Guit.Core;
using Guit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Guit.App;

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

        // Apply the saved theme before the window is shown, so it never flashes the wrong one.
        ThemeService themes = Services.GetRequiredService<ThemeService>();
        themes.Window = _window;
        themes.Apply(SettingKeys.ReadTheme(Services.GetRequiredService<ISettingsStore>()));

        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();
        services.AddSingleton(new GitHubOptions(AppConfig.GitHubClientId));
        services.AddSingleton<ITokenStore, PasswordVaultTokenStore>();
        services.AddSingleton<ILauncher, Launcher>();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher());
        services.AddSingleton<Navigator>();
        services.AddSingleton<INavigator>(static provider => provider.GetRequiredService<Navigator>());
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IThemeService>(static provider => provider.GetRequiredService<ThemeService>());
        services.AddSingleton(new AppInfo(typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
        services.AddGuitCore();
        return services.BuildServiceProvider();
    }
}
