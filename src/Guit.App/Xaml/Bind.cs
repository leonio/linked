using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Guit.App.Xaml;

/// <summary>
/// Static functions for x:Bind function bindings. Use these instead of IValueConverter:
/// they are compiled, strongly typed, and need no reflection.
/// XAML: <c>IsOpen="{x:Bind xaml:Bind.IsNotEmpty(ViewModel.ErrorMessage), Mode=OneWay}"</c>
/// </summary>
public static class Bind
{
    public static bool Not(bool value) => !value;

    /// <summary>For Visibility targets. A function result is not converted from bool for you: return Visibility.</summary>
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool IsNotEmpty(string? value) => !string.IsNullOrEmpty(value);

    /// <summary>Read rows are dimmed; unread rows are fully opaque.</summary>
    public static double UnreadOpacity(bool isUnread) => isUnread ? 1.0 : 0.55;

    /// <summary>
    /// Creates an image decoded at the size it is shown, so a 460px avatar does not sit in memory as 460px.
    /// </summary>
    /// <param name="url">The image URL, or null for no image.</param>
    /// <param name="logicalSize">Width in XAML units of the element that shows it.</param>
    public static ImageSource? Image(string? url, int logicalSize)
        => string.IsNullOrEmpty(url)
            ? null
            : new BitmapImage(new Uri(url)) { DecodePixelWidth = logicalSize, DecodePixelType = DecodePixelType.Logical };
}
