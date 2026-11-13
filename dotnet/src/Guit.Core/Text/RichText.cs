namespace Guit.Core.Text;

public enum RichBlockKind
{
    Paragraph,
    Heading,
    Code,
    ListItem,
    TableRow,
    Rule,
}

[Flags]
public enum RichStyles
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strikethrough = 8,
}

/// <summary>A run of text with one style. <see cref="Text"/> may contain '\n' for a line break.</summary>
/// <param name="Text">The text.</param>
/// <param name="Style">How it is drawn.</param>
/// <param name="Link">Absolute URL the run links to, or null.</param>
public readonly record struct RichInline(string Text, RichStyles Style = RichStyles.None, string? Link = null);

/// <summary>One block of a formatted document.</summary>
/// <param name="kind">What sort of block this is.</param>
/// <param name="inlines">The content. A list item starts with its bullet or number.</param>
/// <param name="level">Heading level 1-6; 0 for other kinds.</param>
/// <param name="indent">Nesting depth, 0 for top level. The view turns this into a left margin.</param>
/// <param name="isQuote">True inside a block quote.</param>
public sealed class RichBlock(RichBlockKind kind, RichInline[] inlines, int level = 0, int indent = 0, bool isQuote = false)
{
    public RichBlockKind Kind { get; } = kind;

    public IReadOnlyList<RichInline> Inlines { get; } = inlines;

    public int Level { get; } = level;

    public int Indent { get; } = indent;

    public bool IsQuote { get; } = isQuote;
}

/// <summary>
/// Formatted text in a form the view can draw without parsing anything: a flat list of blocks.
/// Built once per comment by <see cref="HtmlRichText"/>; the view (Guit.App's Rich.Document) only walks it.
/// </summary>
public sealed partial class RichDocument(RichBlock[] blocks)
{
    public static RichDocument Empty { get; } = new([]);

    public IReadOnlyList<RichBlock> Blocks { get; } = blocks;

    /// <summary>Wraps unformatted text as one paragraph.</summary>
    public static RichDocument FromPlainText(string? text)
        => string.IsNullOrEmpty(text)
            ? Empty
            : new RichDocument([new RichBlock(RichBlockKind.Paragraph, [new RichInline(text)])]);
}
