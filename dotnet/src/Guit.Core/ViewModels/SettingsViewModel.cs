using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Core.Services;

namespace Guit.Core.ViewModels;

/// <summary>Theme, account and about.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IThemeService _themes;
    private readonly Session _session;
    private readonly ILauncher _launcher;

    public SettingsViewModel(ISettingsStore store, IThemeService themes, Session session, ShellViewModel shell, ILauncher launcher, AppInfo app)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(app);
        _store = store;
        _themes = themes;
        _session = session;
        _launcher = launcher;
        Shell = shell;
        Version = app.Version;

        // Set the backing state without going through OnThemeIndexChanged: nothing has changed yet.
        ThemeIndex = (int)SettingKeys.ReadTheme(store);
    }

    /// <summary>The shell's view model, for the signed-in login and avatar.</summary>
    public ShellViewModel Shell { get; }

    public string Version { get; }

    /// <summary>The chosen <see cref="AppTheme"/> as a number, bound to the theme choice's selected index.</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [RelayCommand]
    private void SignOut() => _session.SignOut();

    [RelayCommand]
    private Task OpenProjectAsync() => _launcher.OpenAsync(new Uri("https://github.com/leonio/linked"));

    partial void OnThemeIndexChanged(int oldValue, int newValue)
    {
        // The control reports -1 while it is being built; ignore anything that is not a real choice.
        if (!Enum.IsDefined((AppTheme)newValue) || newValue == (int)SettingKeys.ReadTheme(_store))
        {
            return;
        }

        AppTheme theme = (AppTheme)newValue;
        _store.Write(SettingKeys.Theme, theme.ToString());
        _themes.Apply(theme);
    }
}
