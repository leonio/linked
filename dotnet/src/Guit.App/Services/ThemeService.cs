using Guit.Core.Services;
using Microsoft.UI.Xaml;

namespace Guit.App.Services;

/// <summary>Applies the chosen theme to the window's content. The app sets <see cref="Window"/> once the window exists.</summary>
internal sealed class ThemeService : IThemeService
{
    public Window? Window { get; set; }

    public void Apply(AppTheme theme)
    {
        if (Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}
