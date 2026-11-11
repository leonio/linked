using Guit.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.Text;

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

    /// <summary>Run detail rows: a step sits indented under its job.</summary>
    public static Thickness StepIndent(bool isStep) => new(isStep ? 28 : 0, 0, 0, 0);

    public static double StepRowHeight(bool isStep) => isStep ? 32 : 44;

    public static FontWeight StepWeight(bool isStep) => isStep ? FontWeights.Normal : FontWeights.SemiBold;

    // Diff colours are the one place the app sets a colour itself: green and red carry the meaning.
    // They are translucent so the same brush reads on light and dark backgrounds.
    private static readonly SolidColorBrush AddedBrush = new(Color.FromArgb(0x38, 0x2E, 0xA0, 0x43));
    private static readonly SolidColorBrush RemovedBrush = new(Color.FromArgb(0x38, 0xF8, 0x51, 0x49));
    private static readonly SolidColorBrush HunkBrush = new(Color.FromArgb(0x28, 0x38, 0x8B, 0xFD));
    private static readonly SolidColorBrush FileBrush = new(Color.FromArgb(0x18, 0x80, 0x80, 0x80));

    /// <summary>Row background in the "Files changed" list.</summary>
    public static Brush? DiffBackground(DiffRowKind kind) => kind switch
    {
        DiffRowKind.Added => AddedBrush,
        DiffRowKind.Removed => RemovedBrush,
        DiffRowKind.Hunk => HunkBrush,
        DiffRowKind.File => FileBrush,
        _ => null,
    };

    public static FontWeight DiffWeight(DiffRowKind kind) => kind == DiffRowKind.File ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>A file heading gets room above it, so files read as separate blocks.</summary>
    public static Thickness DiffMargin(DiffRowKind kind) => new(0, kind == DiffRowKind.File ? 16 : 0, 0, 0);

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
