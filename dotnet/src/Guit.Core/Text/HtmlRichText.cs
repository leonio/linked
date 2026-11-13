using System.Globalization;
using System.Net;
using System.Text;

namespace Guit.Core.Text;

/// <summary>
/// Turns the HTML GitHub renders for a comment (the <c>body_html</c> field) into a <see cref="RichDocument"/>.
/// This is not a general HTML parser: it understands the limited, well-formed tag set GitHub emits
/// (paragraphs, emphasis, code, links, images, lists, task lists, quotes, headings, rules, tables)
/// and treats every other tag as transparent. Malformed input degrades to plain text; it never throws.
/// </summary>
public static class HtmlRichText
{
    public static RichDocument Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return RichDocument.Empty;
        }

        Builder builder = new();
        ReadOnlySpan<char> rest = html;
        while (!rest.IsEmpty)
        {
            int open = rest.IndexOf('<');
            if (open != 0)
            {
                ReadOnlySpan<char> text = open < 0 ? rest : rest[..open];
                builder.Text(text);
                rest = open < 0 ? [] : rest[open..];
                continue;
            }

            // "a < b": a "<" that does not start a tag is ordinary text.
            if (rest.Length < 2 || !(char.IsAsciiLetter(rest[1]) || rest[1] is '/' or '!'))
            {
                builder.Text(rest[..1]);
                rest = rest[1..];
                continue;
            }

            if (rest.StartsWith("<!--", StringComparison.Ordinal))
            {
                int end = rest.IndexOf("-->", StringComparison.Ordinal);
                rest = end < 0 ? [] : rest[(end + 3)..];
                continue;
            }

            int close = FindTagEnd(rest);
            if (close < 0)
            {
                // A lone "<" that never closes: keep it as text.
                builder.Text(rest);
                break;
            }

            builder.Tag(rest[1..close]);
            rest = rest[(close + 1)..];
        }

        return builder.Build();
    }

    /// <summary>Index of the '>' that ends the tag starting at 0, skipping any inside quoted attribute values.</summary>
    private static int FindTagEnd(ReadOnlySpan<char> tag)
    {
        char quote = '\0';
        for (int i = 1; i < tag.Length; i++)
        {
            char c = tag[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static string? Attribute(ReadOnlySpan<char> attributes, string name)
    {
        int i = 0;
        while (i < attributes.Length)
        {
            while (i < attributes.Length && (char.IsWhiteSpace(attributes[i]) || attributes[i] == '/'))
            {
                i++;
            }

            int nameStart = i;
            while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]) && attributes[i] is not ('=' or '/'))
            {
                i++;
            }

            ReadOnlySpan<char> attributeName = attributes[nameStart..i];
            if (attributeName.IsEmpty)
            {
                break;
            }

            ReadOnlySpan<char> value = [];
            if (i < attributes.Length && attributes[i] == '=')
            {
                i++;
                if (i < attributes.Length && attributes[i] is '"' or '\'')
                {
                    char quote = attributes[i++];
                    int valueStart = i;
                    while (i < attributes.Length && attributes[i] != quote)
                    {
                        i++;
                    }

                    value = attributes[valueStart..i];
                    i++;
                }
                else
                {
                    int valueStart = i;
                    while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]))
                    {
                        i++;
                    }

                    value = attributes[valueStart..i];
                }
            }

            if (attributeName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.HtmlDecode(value.ToString());
            }
        }

        return null;
    }

    private sealed class Builder
    {
        private readonly List<RichBlock> _blocks = [];
        private readonly List<RichInline> _inlines = [];
        private readonly StringBuilder _run = new();

        // One entry per open list: -1 for a bullet list, otherwise the next number of an ordered list.
        private readonly List<int> _lists = [];

        private RichBlockKind _kind = RichBlockKind.Paragraph;
        private int _level;
        private int _indent;
        private int _quoteDepth;
        private int _bold;
        private int _italic;
        private int _code;
        private int _strike;
        private int _cells;
        private string? _link;
        private bool _inPre;
        private bool _pendingSpace;
        private bool _hasText;
        private string? _skipUntil;

        public RichDocument Build()
        {
            FlushBlock();
            return _blocks.Count == 0 ? RichDocument.Empty : new RichDocument([.. _blocks]);
        }

        public void Text(ReadOnlySpan<char> raw)
        {
            if (_skipUntil is not null || raw.IsEmpty)
            {
                return;
            }

            string text = raw.Contains('&') ? WebUtility.HtmlDecode(raw.ToString()) : raw.ToString();
            if (_inPre)
            {
                _run.Append(text);
                _hasText = true;
                return;
            }

            foreach (char c in text)
            {
                if (c is ' ' or '\n' or '\r' or '\t' or '\f')
                {
                    // Collapse runs of white space, and drop it entirely at the start of a block or line.
                    _pendingSpace = _hasText && !EndsWithLineBreak();
                    continue;
                }

                if (_pendingSpace)
                {
                    _run.Append(' ');
                    _pendingSpace = false;
                }

                _run.Append(c);
                _hasText = true;
            }
        }

        public void Tag(ReadOnlySpan<char> tag)
        {
            bool closing = tag.Length > 0 && tag[0] == '/';
            if (closing)
            {
                tag = tag[1..];
            }

            int nameEnd = 0;
            while (nameEnd < tag.Length && !char.IsWhiteSpace(tag[nameEnd]) && tag[nameEnd] != '/')
            {
                nameEnd++;
            }

            string name = tag[..nameEnd].ToString().ToLowerInvariant();
            ReadOnlySpan<char> attributes = tag[nameEnd..];

            if (_skipUntil is not null)
            {
                if (closing && name == _skipUntil)
                {
                    _skipUntil = null;
                }

                return;
            }

            if (closing)
            {
                Close(name);
            }
            else
            {
                Open(name, attributes);
            }
        }

        private void Open(string name, ReadOnlySpan<char> attributes)
        {
            switch (name)
            {
                case "p":
                    // A list item's first paragraph continues the item (the bullet is already there).
                    if (_kind != RichBlockKind.ListItem || _hasText)
                    {
                        FlushBlock();
                        _indent = _lists.Count;
                    }

                    break;
                case "br":
                    FlushRun();
                    _run.Append('\n');
                    _hasText = true;
                    break;
                case "strong" or "b":
                    Restyle(ref _bold, +1);
                    break;
                case "em" or "i":
                    Restyle(ref _italic, +1);
                    break;
                case "del" or "s" or "strike":
                    Restyle(ref _strike, +1);
                    break;
                case "code" or "kbd" or "tt" or "samp":
                    if (!_inPre)
                    {
                        Restyle(ref _code, +1);
                    }

                    break;
                case "a":
                    FlushRun();
                    _link = NormalizeLink(Attribute(attributes, "href"));
                    break;
                case "img":
                    FlushRun();
                    string? alt = Attribute(attributes, "alt");
                    _inlines.Add(new RichInline(
                        "[" + (string.IsNullOrEmpty(alt) ? "image" : alt) + "]",
                        CurrentStyle(),
                        _link ?? NormalizeLink(Attribute(attributes, "src"))));
                    _hasText = true;
                    break;
                case "ul":
                    FlushBlock();
                    _lists.Add(-1);
                    break;
                case "ol":
                    FlushBlock();
                    _lists.Add(int.TryParse(Attribute(attributes, "start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ? start : 1);
                    break;
                case "li":
                    StartListItem();
                    break;
                case "input":
                    // Task list: swap the bullet for a check box.
                    if (_kind == RichBlockKind.ListItem && !_hasText && _inlines.Count == 1
                        && string.Equals(Attribute(attributes, "type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                    {
                        _inlines[0] = new RichInline(Attribute(attributes, "checked") is null ? "☐ " : "☑ ");
                    }

                    break;
                case "blockquote":
                    FlushBlock();
                    _quoteDepth++;
                    break;
                case "pre":
                    FlushBlock();
                    _kind = RichBlockKind.Code;
                    _inPre = true;
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    FlushBlock();
                    _kind = RichBlockKind.Heading;
                    _level = name[1] - '0';
                    break;
                case "hr":
                    FlushBlock();
                    _blocks.Add(new RichBlock(RichBlockKind.Rule, [], isQuote: _quoteDepth > 0));
                    break;
                case "tr":
                    FlushBlock();
                    _kind = RichBlockKind.TableRow;
                    _cells = 0;
                    break;
                case "td" or "th":
                    FlushRun();
                    if (_cells++ > 0)
                    {
                        _inlines.Add(new RichInline(" | "));
                    }

                    if (name == "th")
                    {
                        _bold++;
                    }

                    break;
                case "summary":
                    FlushBlock();
                    _bold++;
                    break;
                case "script" or "style":
                    _skipUntil = name;
                    break;
                case "div" or "table" or "details" or "section" or "article" or "dl" or "dt" or "dd":
                    FlushBlock();
                    break;
                default:
                    // span, g-emoji, sup, sub and anything unknown: keep the text, ignore the tag.
                    break;
            }
        }

        private void Close(string name)
        {
            switch (name)
            {
                case "strong" or "b":
                    Restyle(ref _bold, -1);
                    break;
                case "em" or "i":
                    Restyle(ref _italic, -1);
                    break;
                case "del" or "s" or "strike":
                    Restyle(ref _strike, -1);
                    break;
                case "code" or "kbd" or "tt" or "samp":
                    if (!_inPre)
                    {
                        Restyle(ref _code, -1);
                    }

                    break;
                case "a":
                    FlushRun();
                    _link = null;
                    break;
                case "ul" or "ol":
                    FlushBlock();
                    if (_lists.Count > 0)
                    {
                        _lists.RemoveAt(_lists.Count - 1);
                    }

                    break;
                case "blockquote":
                    FlushBlock();
                    _quoteDepth = Math.Max(0, _quoteDepth - 1);
                    break;
                case "pre":
                    _inPre = false;
                    FlushBlock();
                    break;
                case "th":
                    FlushRun();
                    _bold = Math.Max(0, _bold - 1);
                    break;
                case "summary":
                    FlushBlock();
                    _bold = Math.Max(0, _bold - 1);
                    break;
                case "p" or "li" or "tr" or "div" or "table" or "details" or "section" or "article" or "dl" or "dt" or "dd"
                    or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    FlushBlock();
                    break;
                default:
                    break;
            }
        }

        private void StartListItem()
        {
            FlushBlock();
            _kind = RichBlockKind.ListItem;
            _indent = Math.Max(0, _lists.Count - 1);

            string marker = "• ";
            if (_lists.Count > 0 && _lists[^1] >= 0)
            {
                marker = string.Create(CultureInfo.InvariantCulture, $"{_lists[^1]}. ");
                _lists[^1]++;
            }

            _inlines.Add(new RichInline(marker));
        }

        private void Restyle(ref int counter, int delta)
        {
            FlushRun();
            counter = Math.Max(0, counter + delta);
        }

        private RichStyles CurrentStyle()
            => (_bold > 0 ? RichStyles.Bold : RichStyles.None)
                | (_italic > 0 ? RichStyles.Italic : RichStyles.None)
                | (_code > 0 ? RichStyles.Code : RichStyles.None)
                | (_strike > 0 ? RichStyles.Strikethrough : RichStyles.None);

        private bool EndsWithLineBreak()
            => _run.Length > 0
                ? _run[^1] == '\n'
                : _inlines.Count > 0 && _inlines[^1].Text.EndsWith('\n');

        /// <summary>Ends the current run. Call before anything that changes style or link.</summary>
        private void FlushRun()
        {
            // A space owed between two words belongs to the run that is ending.
            if (_pendingSpace && _run.Length > 0)
            {
                _run.Append(' ');
                _pendingSpace = false;
            }

            if (_run.Length == 0)
            {
                return;
            }

            _inlines.Add(new RichInline(_run.ToString(), _inPre ? RichStyles.None : CurrentStyle(), _link));
            _run.Clear();
        }

        private void FlushBlock()
        {
            _pendingSpace = false;
            FlushRun();

            if (_hasText && _inlines.Count > 0)
            {
                // Drop the trailing space (or, for code, the trailing newline) left before the closing tag.
                RichInline last = _inlines[^1];
                string trimmed = _kind == RichBlockKind.Code ? last.Text.TrimEnd('\n', '\r') : last.Text.TrimEnd(' ', '\n');
                if (trimmed.Length == 0)
                {
                    _inlines.RemoveAt(_inlines.Count - 1);
                }
                else if (trimmed.Length != last.Text.Length)
                {
                    _inlines[^1] = last with { Text = trimmed };
                }

                if (_inlines.Count > 0)
                {
                    _blocks.Add(new RichBlock(_kind, [.. _inlines], _level, _indent, _quoteDepth > 0));
                }
            }

            _inlines.Clear();
            _kind = RichBlockKind.Paragraph;
            _level = 0;
            _indent = 0;
            _hasText = false;
        }

        private static string? NormalizeLink(string? href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return null;
            }

            if (href[0] == '/' && !href.StartsWith("//", StringComparison.Ordinal))
            {
                return "https://github.com" + href;
            }

            return href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                ? href
                : null;
        }
    }
}
