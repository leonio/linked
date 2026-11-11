namespace Guit.Core.Services;

/// <summary>The numeric values match the theme choice's item order on the settings page.</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Small key/value settings that survive restarts. Implemented by the app as a file in the user's profile.</summary>
public interface ISettingsStore
{
    string? Read(string key);

    void Write(string key, string value);
}

/// <summary>Applies a theme to the app's window. Implemented by the app.</summary>
public interface IThemeService
{
    void Apply(AppTheme theme);
}

/// <summary>Facts about the running app, supplied by the app at start-up.</summary>
/// <param name="Version">For example "0.1.0".</param>
public sealed record AppInfo(string Version);

public static class SettingKeys
{
    public const string Theme = "theme";

    /// <summary>Reads the saved theme; anything missing or unreadable means "follow Windows".</summary>
    public static AppTheme ReadTheme(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return Enum.TryParse(store.Read(Theme), ignoreCase: true, out AppTheme theme) && Enum.IsDefined(theme) ? theme : AppTheme.System;
    }
}
