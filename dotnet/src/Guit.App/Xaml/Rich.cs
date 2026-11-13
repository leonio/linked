using Guit.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Guit.App.Xaml;

/// <summary>
/// Draws a <see cref="RichDocument"/> in a RichTextBlock:
/// <c>&lt;RichTextBlock xaml:Rich.Document="{x:Bind Content}" /&gt;</c>.
/// All parsing happened in Guit.Core; this only creates text elements, so it is cheap enough to run
/// each time a recycled list row is given a new comment.
/// </summary>
public static class Rich
{
    private const double IndentWidth = 20;

    // The property type is object on purpose: XAML only has to carry the value, never to understand its type.
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.RegisterAttached(
        "Document",
        typeof(object),
        typeof(Rich),
        new PropertyMetadata(null, OnDocumentChanged));

    private static readonly FontFamily Monospace = new("Cascadia Mono, Consolas");

    public static object? GetDocument(RichTextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(DocumentProperty);
    }

    public static void SetDocument(RichTextBlock element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(DocumentProperty, value);
    }

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not RichTextBlock target)
        {
            return;
        }

        target.Blocks.Clear();
        if (args.NewValue is not RichDocument document)
        {
            return;
        }

        foreach (RichBlock block in document.Blocks)
        {
            target.Blocks.Add(CreateParagraph(block));
        }
    }

    private static Paragraph CreateParagraph(RichBlock block)
    {
        Paragraph paragraph = new()
        {
            Margin = new Thickness(block.Indent * IndentWidth, 0, 0, 8),
        };

        switch (block.Kind)
        {
            case RichBlockKind.Heading:
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = block.Level switch { 1 => 24, 2 => 20, 3 => 18, _ => 16 };
                break;
            case RichBlockKind.Code:
                paragraph.FontFamily = Monospace;
                paragraph.FontSize = 13;
                break;
            case RichBlockKind.Rule:
                paragraph.Inlines.Add(new Run { Text = "――――――――" });
                return paragraph;
            default:
                break;
        }

        if (block.IsQuote)
        {
            paragraph.Inlines.Add(new Run { Text = "▎ " });
            paragraph.FontStyle = FontStyle.Italic;
        }

        foreach (RichInline inline in block.Inlines)
        {
            if (inline.Link is not null && Uri.TryCreate(inline.Link, UriKind.Absolute, out Uri? uri))
            {
                // A Hyperlink with a NavigateUri opens the default browser by itself.
                Hyperlink link = new() { NavigateUri = uri };
                AddRuns(link.Inlines, inline);
                paragraph.Inlines.Add(link);
            }
            else
            {
                AddRuns(paragraph.Inlines, inline);
            }
        }

        return paragraph;
    }

    /// <summary>Adds the inline's text, turning each '\n' into a LineBreak.</summary>
    private static void AddRuns(InlineCollection target, RichInline inline)
    {
        string text = inline.Text;
        int start = 0;
        while (start <= text.Length)
        {
            int newline = text.IndexOf('\n', start);
            int end = newline < 0 ? text.Length : newline;
            if (end > start)
            {
                target.Add(CreateRun(text[start..end], inline.Style));
            }

            if (newline < 0)
            {
                break;
            }

            target.Add(new LineBreak());
            start = newline + 1;
        }
    }

    private static Run CreateRun(string text, RichStyles style)
    {
        Run run = new() { Text = text };
        if ((style & RichStyles.Bold) != 0)
        {
            run.FontWeight = FontWeights.SemiBold;
        }

        if ((style & RichStyles.Italic) != 0)
        {
            run.FontStyle = FontStyle.Italic;
        }

        if ((style & RichStyles.Code) != 0)
        {
            run.FontFamily = Monospace;
        }

        if ((style & RichStyles.Strikethrough) != 0)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        return run;
    }
}
