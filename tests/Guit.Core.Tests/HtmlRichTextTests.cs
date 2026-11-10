using Guit.Core.Text;

namespace Guit.Core.Tests;

public sealed class HtmlRichTextTests
{
    /// <summary>Renders a document as text so a test can compare whole structures at a glance.</summary>
    private static string Dump(RichDocument document)
    {
        List<string> lines = [];
        foreach (RichBlock block in document.Blocks)
        {
            string head = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{block.Kind}{(block.Level > 0 ? block.Level : null)}{(block.Indent > 0 ? ">" + block.Indent : null)}{(block.IsQuote ? "q" : null)}");
            List<string> parts = [];
            foreach (RichInline inline in block.Inlines)
            {
                string flags = (inline.Style.HasFlag(RichStyles.Bold) ? "B" : "") + (inline.Style.HasFlag(RichStyles.Italic) ? "I" : "")
                    + (inline.Style.HasFlag(RichStyles.Code) ? "C" : "") + (inline.Style.HasFlag(RichStyles.Strikethrough) ? "S" : "");
                parts.Add((flags.Length > 0 ? flags + ":" : "") + "'" + inline.Text.Replace("\n", "\\n") + "'" + (inline.Link is null ? "" : "@" + inline.Link));
            }

            lines.Add(head + " " + string.Join(" ", parts));
        }

        return string.Join("\n", lines);
    }

    [Test]
    public async Task Paragraphs_with_inline_styles()
    {
        RichDocument document = HtmlRichText.Parse("<p>Please <strong>review</strong> this <em>soon</em>, <del>or not</del>.</p>\n<p>Second   paragraph\nhere.</p>");

        await Assert.That(Dump(document)).IsEqualTo(
            "Paragraph 'Please ' B:'review' ' this ' I:'soon' ', ' S:'or not' '.'\n" +
            "Paragraph 'Second paragraph here.'");
    }

    [Test]
    public async Task Entities_are_decoded_and_nested_styles_combine()
    {
        RichDocument document = HtmlRichText.Parse("<p>a &lt; b &amp;&amp; <strong><em>c &gt; d</em></strong></p>");

        await Assert.That(Dump(document)).IsEqualTo("Paragraph 'a < b && ' BI:'c > d'");
    }

    [Test]
    public async Task Links_inline_code_and_line_breaks()
    {
        RichDocument document = HtmlRichText.Parse(
            """<p>See <a href="https://example.com/x?a=1&amp;b=2">the docs</a> and <a class="user-mention" href="/octocat">@octocat</a>,<br>run <code>dotnet test</code> <a href="#anchor">here</a></p>""");

        await Assert.That(Dump(document)).IsEqualTo(
            "Paragraph 'See ' 'the docs'@https://example.com/x?a=1&b=2 ' and ' '@octocat'@https://github.com/octocat ',' '\\nrun ' C:'dotnet test' ' here'");
    }

    [Test]
    public async Task Code_blocks_keep_white_space_and_drop_highlight_markup()
    {
        RichDocument document = HtmlRichText.Parse(
            "<div class=\"highlight highlight-source-cs\"><pre><span class=\"pl-k\">if</span> (a &lt; b)\n{\n    <span class=\"pl-en\">Run</span>();\n}\n</pre></div><p>after</p>");

        await Assert.That(document.Blocks[0].Kind).IsEqualTo(RichBlockKind.Code);
        await Assert.That(string.Concat(document.Blocks[0].Inlines.Select(i => i.Text))).IsEqualTo("if (a < b)\n{\n    Run();\n}");
        await Assert.That(document.Blocks[1].Inlines[0].Text).IsEqualTo("after");
    }

    [Test]
    public async Task Lists_get_markers_numbers_and_nesting()
    {
        RichDocument document = HtmlRichText.Parse(
            "<ul>\n<li>one</li>\n<li>two\n<ol start=\"3\">\n<li>three</li>\n<li><p>four</p></li>\n</ol>\n</li>\n</ul>");

        await Assert.That(Dump(document)).IsEqualTo(
            "ListItem '• ' 'one'\n" +
            "ListItem '• ' 'two'\n" +
            "ListItem>1 '3. ' 'three'\n" +
            "ListItem>1 '4. ' 'four'");
    }

    [Test]
    public async Task Task_lists_show_check_boxes()
    {
        RichDocument document = HtmlRichText.Parse(
            """<ul class="contains-task-list"><li class="task-list-item"><input type="checkbox" class="task-list-item-checkbox" disabled="" checked=""> done</li><li class="task-list-item"><input type="checkbox" disabled=""> todo</li></ul>""");

        await Assert.That(Dump(document)).IsEqualTo("ListItem '☑ ' 'done'\nListItem '☐ ' 'todo'");
    }

    [Test]
    public async Task Quotes_headings_and_rules()
    {
        RichDocument document = HtmlRichText.Parse("<h2>Title</h2><blockquote>\n<p>quoted <code>x</code></p>\n</blockquote><hr><p>end</p>");

        await Assert.That(Dump(document)).IsEqualTo(
            "Heading2 'Title'\n" +
            "Paragraphq 'quoted ' C:'x'\n" +
            "Rule \n" +
            "Paragraph 'end'");
    }

    [Test]
    public async Task Tables_become_one_row_per_line_and_images_become_links()
    {
        RichDocument document = HtmlRichText.Parse(
            """<table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>a</td><td>1</td></tr></tbody></table><p><a href="https://example.com/full.png"><img src="https://example.com/thumb.png" alt="Screenshot"></a><img src="https://example.com/b.png"></p>""");

        await Assert.That(Dump(document)).IsEqualTo(
            "TableRow B:'Name' ' | ' B:'Value'\n" +
            "TableRow 'a' ' | ' '1'\n" +
            "Paragraph '[Screenshot]'@https://example.com/full.png '[image]'@https://example.com/b.png");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \n ")]
    [Arguments("<p></p><ul><li></li></ul>")]
    public async Task Nothing_to_show_gives_an_empty_document(string? html)
    {
        await Assert.That(HtmlRichText.Parse(html).Blocks.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("plain text, no tags", "Paragraph 'plain text, no tags'")]
    [Arguments("a < b and <p>unclosed", "Paragraph 'a < b and'\nParagraph 'unclosed'")]
    [Arguments("ends with <", "Paragraph 'ends with <'")]
    [Arguments("<p>never closed <strong", "Paragraph 'never closed <strong'")]
    [Arguments("<p>text <!-- hidden --> more</p><script>alert(1)</script>", "Paragraph 'text more'")]
    [Arguments("</strong></ul></blockquote><p>still fine</p>", "Paragraph 'still fine'")]
    [Arguments("<p title=\"a > b\">quoted attribute</p>", "Paragraph 'quoted attribute'")]
    public async Task Odd_input_degrades_without_throwing(string html, string expected)
    {
        await Assert.That(Dump(HtmlRichText.Parse(html))).IsEqualTo(expected);
    }

    [Test]
    public async Task FromPlainText_wraps_the_text_in_one_paragraph()
    {
        await Assert.That(Dump(RichDocument.FromPlainText("line one\nline two"))).IsEqualTo("Paragraph 'line one\\nline two'");
        await Assert.That(RichDocument.FromPlainText(null).Blocks.Count).IsEqualTo(0);
    }
}
