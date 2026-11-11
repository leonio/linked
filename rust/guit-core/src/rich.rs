//! Formatted text for comments. GitHub returns each comment rendered as HTML (`body_html`); this turns that
//! limited tag set into a flat list of styled blocks the view can draw without parsing anything.
//!
//! A port of the C# `HtmlRichText`: not a general HTML parser. It understands what GitHub emits (paragraphs,
//! emphasis, code, links, images, lists, task lists, quotes, headings, rules, tables) and treats every other tag
//! as transparent. Malformed input degrades to plain text; it never panics.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BlockKind {
    Paragraph,
    Heading,
    Code,
    ListItem,
    TableRow,
    Rule,
}

/// A run of text with one style. `text` may contain '\n' for a line break.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct Inline {
    pub text: String,
    pub bold: bool,
    pub italic: bool,
    pub code: bool,
    pub strikethrough: bool,
    /// Absolute URL the run links to.
    pub link: Option<String>,
}

/// One block of a formatted document.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Block {
    pub kind: BlockKind,
    /// The content. A list item starts with its bullet or number.
    pub inlines: Vec<Inline>,
    /// Heading level 1-6; 0 for other kinds.
    pub level: u8,
    /// Nesting depth, 0 for top level.
    pub indent: u8,
    pub is_quote: bool,
}

/// Wraps unformatted text as one paragraph (nothing for empty text).
pub fn from_plain_text(text: &str) -> Vec<Block> {
    if text.is_empty() {
        return Vec::new();
    }

    let inline = Inline {
        text: text.to_string(),
        ..Inline::default()
    };
    vec![Block {
        kind: BlockKind::Paragraph,
        inlines: vec![inline],
        level: 0,
        indent: 0,
        is_quote: false,
    }]
}

/// Parses the HTML GitHub renders for a comment.
pub fn parse_html(html: &str) -> Vec<Block> {
    let mut builder = Builder::default();
    let mut rest = html;
    while !rest.is_empty() {
        let Some(open) = rest.find('<') else {
            builder.text(rest);
            break;
        };
        if open > 0 {
            builder.text(&rest[..open]);
            rest = &rest[open..];
            continue;
        }

        // "a < b": a "<" that does not start a tag is ordinary text.
        let starts_tag = rest[1..]
            .chars()
            .next()
            .is_some_and(|next| next.is_ascii_alphabetic() || next == '/' || next == '!');
        if !starts_tag {
            builder.text("<");
            rest = &rest[1..];
            continue;
        }

        if rest.starts_with("<!--") {
            rest = rest.find("-->").map_or("", |end| &rest[end + 3..]);
            continue;
        }

        match tag_end(rest) {
            Some(close) => {
                builder.tag(&rest[1..close]);
                rest = &rest[close + 1..];
            }
            None => {
                // A "<" that never closes: keep it as text.
                builder.text(rest);
                break;
            }
        }
    }

    builder.finish()
}

/// Index of the '>' that ends the tag starting at 0, skipping any inside quoted attribute values.
fn tag_end(tag: &str) -> Option<usize> {
    let mut quote = None;
    for (index, character) in tag.char_indices().skip(1) {
        match quote {
            Some(open) if character == open => quote = None,
            Some(_) => {}
            None if character == '"' || character == '\'' => quote = Some(character),
            None if character == '>' => return Some(index),
            None => {}
        }
    }
    None
}

/// The value of an attribute, or `Some("")` for one with no value.
fn attribute(attributes: &str, name: &str) -> Option<String> {
    let mut rest = attributes;
    loop {
        rest = rest
            .trim_start_matches(|character: char| character.is_whitespace() || character == '/');
        if rest.is_empty() {
            return None;
        }

        let name_end = rest
            .find(|character: char| {
                character.is_whitespace() || character == '=' || character == '/'
            })
            .unwrap_or(rest.len());
        let found = &rest[..name_end];
        rest = &rest[name_end..];

        let mut value = "";
        if let Some(after) = rest.strip_prefix('=') {
            let quote = after
                .chars()
                .next()
                .filter(|character| *character == '"' || *character == '\'');
            match quote {
                Some(quote) => {
                    let body = &after[1..];
                    let end = body.find(quote).unwrap_or(body.len());
                    value = &body[..end];
                    rest = body.get(end + 1..).unwrap_or("");
                }
                None => {
                    let end = after.find(char::is_whitespace).unwrap_or(after.len());
                    value = &after[..end];
                    rest = &after[end..];
                }
            }
        }

        if found.is_empty() {
            return None;
        }
        if found.eq_ignore_ascii_case(name) {
            return Some(decode_entities(value));
        }
    }
}

/// Decodes the character references GitHub's HTML uses: the five XML ones, `&nbsp;`, and numeric ones.
fn decode_entities(text: &str) -> String {
    if !text.contains('&') {
        return text.to_string();
    }

    let mut decoded = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(start) = rest.find('&') {
        decoded.push_str(&rest[..start]);
        rest = &rest[start..];
        let end = rest[..rest.len().min(12)].find(';');
        let replacement = end.and_then(|end| {
            let name = &rest[1..end];
            match name {
                "amp" => Some('&'),
                "lt" => Some('<'),
                "gt" => Some('>'),
                "quot" => Some('"'),
                "apos" => Some('\''),
                "nbsp" => Some('\u{A0}'),
                _ => {
                    let number = name.strip_prefix('#')?;
                    let code = match number.strip_prefix(['x', 'X']) {
                        Some(hex) => u32::from_str_radix(hex, 16).ok()?,
                        None => number.parse().ok()?,
                    };
                    char::from_u32(code)
                }
            }
        });

        match (replacement, end) {
            (Some(character), Some(end)) => {
                decoded.push(character);
                rest = &rest[end + 1..];
            }
            _ => {
                decoded.push('&');
                rest = &rest[1..];
            }
        }
    }
    decoded.push_str(rest);
    decoded
}

fn normalize_link(href: Option<String>) -> Option<String> {
    let href = href.filter(|href| !href.is_empty())?;
    if href.starts_with('/') && !href.starts_with("//") {
        return Some(format!("https://github.com{href}"));
    }

    let lower = href.to_ascii_lowercase();
    (lower.starts_with("https://") || lower.starts_with("http://") || lower.starts_with("mailto:"))
        .then_some(href)
}

#[derive(Default)]
struct Builder {
    blocks: Vec<Block>,
    inlines: Vec<Inline>,
    run: String,
    /// One entry per open list: `None` for a bullet list, otherwise the next number of an ordered list.
    lists: Vec<Option<u32>>,
    kind: Option<BlockKind>,
    level: u8,
    indent: u8,
    quote_depth: u32,
    bold: u32,
    italic: u32,
    code: u32,
    strike: u32,
    cells: u32,
    link: Option<String>,
    in_pre: bool,
    pending_space: bool,
    has_text: bool,
    skip_until: Option<String>,
}

impl Builder {
    fn finish(mut self) -> Vec<Block> {
        self.flush_block();
        self.blocks
    }

    fn text(&mut self, raw: &str) {
        if self.skip_until.is_some() || raw.is_empty() {
            return;
        }

        let text = decode_entities(raw);
        if self.in_pre {
            self.run.push_str(&text);
            self.has_text = true;
            return;
        }

        for character in text.chars() {
            if matches!(character, ' ' | '\n' | '\r' | '\t' | '\u{C}') {
                // Collapse runs of white space, and drop it entirely at the start of a block or line.
                self.pending_space = self.has_text && !self.ends_with_line_break();
                continue;
            }

            if self.pending_space {
                self.run.push(' ');
                self.pending_space = false;
            }
            self.run.push(character);
            self.has_text = true;
        }
    }

    fn tag(&mut self, tag: &str) {
        let (closing, tag) = match tag.strip_prefix('/') {
            Some(rest) => (true, rest),
            None => (false, tag),
        };
        let name_end = tag
            .find(|character: char| character.is_whitespace() || character == '/')
            .unwrap_or(tag.len());
        let name = tag[..name_end].to_ascii_lowercase();
        let attributes = &tag[name_end..];

        if let Some(until) = &self.skip_until {
            if closing && *until == name {
                self.skip_until = None;
            }
            return;
        }

        if closing {
            self.close(&name)
        } else {
            self.open(&name, attributes)
        }
    }

    fn open(&mut self, name: &str, attributes: &str) {
        match name {
            "p" => {
                // A list item's first paragraph continues the item (the bullet is already there).
                if self.kind != Some(BlockKind::ListItem) || self.has_text {
                    self.flush_block();
                    self.indent = self.lists.len() as u8;
                }
            }
            "br" => {
                self.flush_run();
                self.run.push('\n');
                self.has_text = true;
            }
            "strong" | "b" => self.restyle(|builder| builder.bold += 1),
            "em" | "i" => self.restyle(|builder| builder.italic += 1),
            "del" | "s" | "strike" => self.restyle(|builder| builder.strike += 1),
            "code" | "kbd" | "tt" | "samp" if !self.in_pre => {
                self.restyle(|builder| builder.code += 1)
            }
            "a" => {
                self.flush_run();
                self.link = normalize_link(attribute(attributes, "href"));
            }
            "img" => {
                self.flush_run();
                let alt = attribute(attributes, "alt")
                    .filter(|alt| !alt.is_empty())
                    .unwrap_or_else(|| "image".to_string());
                let link = self
                    .link
                    .clone()
                    .or_else(|| normalize_link(attribute(attributes, "src")));
                let mut inline = self.styled(format!("[{alt}]"));
                inline.link = link;
                self.inlines.push(inline);
                self.has_text = true;
            }
            "ul" => {
                self.flush_block();
                self.lists.push(None);
            }
            "ol" => {
                self.flush_block();
                self.lists.push(Some(
                    attribute(attributes, "start")
                        .and_then(|start| start.parse().ok())
                        .unwrap_or(1),
                ));
            }
            "li" => self.start_list_item(),
            "input" => {
                // Task list: swap the bullet for a check box.
                let is_checkbox = attribute(attributes, "type")
                    .is_some_and(|kind| kind.eq_ignore_ascii_case("checkbox"));
                if self.kind == Some(BlockKind::ListItem)
                    && !self.has_text
                    && self.inlines.len() == 1
                    && is_checkbox
                {
                    let checked = attribute(attributes, "checked").is_some();
                    self.inlines[0].text =
                        if checked { "\u{2611} " } else { "\u{2610} " }.to_string();
                }
            }
            "blockquote" => {
                self.flush_block();
                self.quote_depth += 1;
            }
            "pre" => {
                self.flush_block();
                self.kind = Some(BlockKind::Code);
                self.in_pre = true;
            }
            "h1" | "h2" | "h3" | "h4" | "h5" | "h6" => {
                self.flush_block();
                self.kind = Some(BlockKind::Heading);
                self.level = name.as_bytes()[1] - b'0';
            }
            "hr" => {
                self.flush_block();
                self.blocks.push(Block {
                    kind: BlockKind::Rule,
                    inlines: Vec::new(),
                    level: 0,
                    indent: 0,
                    is_quote: self.quote_depth > 0,
                });
            }
            "tr" => {
                self.flush_block();
                self.kind = Some(BlockKind::TableRow);
                self.cells = 0;
            }
            "td" | "th" => {
                self.flush_run();
                if self.cells > 0 {
                    self.inlines.push(Inline {
                        text: " | ".to_string(),
                        ..Inline::default()
                    });
                }
                self.cells += 1;
                if name == "th" {
                    self.bold += 1;
                }
            }
            "summary" => {
                self.flush_block();
                self.bold += 1;
            }
            "script" | "style" => self.skip_until = Some(name.to_string()),
            "div" | "table" | "details" | "section" | "article" | "dl" | "dt" | "dd" => {
                self.flush_block()
            }
            // span, g-emoji, sup, sub and anything unknown: keep the text, ignore the tag.
            _ => {}
        }
    }

    fn close(&mut self, name: &str) {
        match name {
            "strong" | "b" => self.restyle(|builder| builder.bold = builder.bold.saturating_sub(1)),
            "em" | "i" => self.restyle(|builder| builder.italic = builder.italic.saturating_sub(1)),
            "del" | "s" | "strike" => {
                self.restyle(|builder| builder.strike = builder.strike.saturating_sub(1))
            }
            "code" | "kbd" | "tt" | "samp" if !self.in_pre => {
                self.restyle(|builder| builder.code = builder.code.saturating_sub(1))
            }
            "a" => {
                self.flush_run();
                self.link = None;
            }
            "ul" | "ol" => {
                self.flush_block();
                self.lists.pop();
            }
            "blockquote" => {
                self.flush_block();
                self.quote_depth = self.quote_depth.saturating_sub(1);
            }
            "pre" => {
                self.in_pre = false;
                self.flush_block();
            }
            "th" => {
                self.flush_run();
                self.bold = self.bold.saturating_sub(1);
            }
            "summary" => {
                self.flush_block();
                self.bold = self.bold.saturating_sub(1);
            }
            "p" | "li" | "tr" | "div" | "table" | "details" | "section" | "article" | "dl"
            | "dt" | "dd" | "h1" | "h2" | "h3" | "h4" | "h5" | "h6" => self.flush_block(),
            _ => {}
        }
    }

    fn start_list_item(&mut self) {
        self.flush_block();
        self.kind = Some(BlockKind::ListItem);
        self.indent = self.lists.len().saturating_sub(1) as u8;

        let marker = match self.lists.last_mut() {
            Some(Some(number)) => {
                let marker = format!("{number}. ");
                *number += 1;
                marker
            }
            _ => "\u{2022} ".to_string(),
        };
        self.inlines.push(Inline {
            text: marker,
            ..Inline::default()
        });
    }

    /// Ends the current run, changes the style, and carries on.
    fn restyle(&mut self, change: impl FnOnce(&mut Self)) {
        self.flush_run();
        change(self);
    }

    fn styled(&self, text: String) -> Inline {
        Inline {
            text,
            bold: self.bold > 0,
            italic: self.italic > 0,
            code: self.code > 0,
            strikethrough: self.strike > 0,
            link: self.link.clone(),
        }
    }

    fn ends_with_line_break(&self) -> bool {
        if self.run.is_empty() {
            self.inlines
                .last()
                .is_some_and(|inline| inline.text.ends_with('\n'))
        } else {
            self.run.ends_with('\n')
        }
    }

    /// Ends the current run. Call before anything that changes style or link.
    fn flush_run(&mut self) {
        // A space owed between two words belongs to the run that is ending.
        if self.pending_space && !self.run.is_empty() {
            self.run.push(' ');
            self.pending_space = false;
        }
        if self.run.is_empty() {
            return;
        }

        let text = std::mem::take(&mut self.run);
        let inline = if self.in_pre {
            Inline {
                text,
                link: self.link.clone(),
                ..Inline::default()
            }
        } else {
            self.styled(text)
        };
        self.inlines.push(inline);
    }

    fn flush_block(&mut self) {
        self.pending_space = false;
        self.flush_run();
        let kind = self.kind.unwrap_or(BlockKind::Paragraph);

        if self.has_text && !self.inlines.is_empty() {
            // Drop the trailing space (or, for code, the trailing newline) left before the closing tag.
            let last = self.inlines.last_mut().expect("checked not empty");
            let trimmed = if kind == BlockKind::Code {
                last.text.trim_end_matches(['\n', '\r'])
            } else {
                last.text.trim_end_matches([' ', '\n'])
            };
            if trimmed.is_empty() {
                self.inlines.pop();
            } else if trimmed.len() != last.text.len() {
                last.text.truncate(trimmed.len());
            }

            if !self.inlines.is_empty() {
                self.blocks.push(Block {
                    kind,
                    inlines: std::mem::take(&mut self.inlines),
                    level: self.level,
                    indent: self.indent,
                    is_quote: self.quote_depth > 0,
                });
            }
        }

        self.inlines.clear();
        self.kind = None;
        self.level = 0;
        self.indent = 0;
        self.has_text = false;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Renders blocks as text so a test can compare whole structures at a glance. Same format as the C# tests.
    fn dump(blocks: &[Block]) -> String {
        blocks
            .iter()
            .map(|block| {
                let mut head = format!("{:?}", block.kind);
                if block.level > 0 {
                    head.push_str(&block.level.to_string());
                }
                if block.indent > 0 {
                    head.push_str(&format!(">{}", block.indent));
                }
                if block.is_quote {
                    head.push('q');
                }
                let parts: Vec<String> = block
                    .inlines
                    .iter()
                    .map(|inline| {
                        let mut flags = String::new();
                        for (on, flag) in [
                            (inline.bold, 'B'),
                            (inline.italic, 'I'),
                            (inline.code, 'C'),
                            (inline.strikethrough, 'S'),
                        ] {
                            if on {
                                flags.push(flag);
                            }
                        }
                        let prefix = if flags.is_empty() {
                            String::new()
                        } else {
                            format!("{flags}:")
                        };
                        let link = inline
                            .link
                            .as_ref()
                            .map_or_else(String::new, |link| format!("@{link}"));
                        format!("{prefix}'{}'{link}", inline.text.replace('\n', "\\n"))
                    })
                    .collect();
                format!("{head} {}", parts.join(" "))
            })
            .collect::<Vec<_>>()
            .join("\n")
    }

    #[test]
    fn paragraphs_with_inline_styles() {
        let blocks = parse_html(
            "<p>Please <strong>review</strong> this <em>soon</em>, <del>or not</del>.</p>\n<p>Second   paragraph\nhere.</p>",
        );
        assert_eq!(
            dump(&blocks),
            "Paragraph 'Please ' B:'review' ' this ' I:'soon' ', ' S:'or not' '.'\nParagraph 'Second paragraph here.'"
        );
    }

    #[test]
    fn entities_are_decoded_and_nested_styles_combine() {
        let blocks = parse_html(
            "<p>a &lt; b &amp;&amp; <strong><em>c &gt; d</em></strong> &#39;x&#x27; &unknown; &</p>",
        );
        assert_eq!(
            dump(&blocks),
            "Paragraph 'a < b && ' BI:'c > d' ' 'x' &unknown; &'"
        );
    }

    #[test]
    fn links_inline_code_and_line_breaks() {
        let blocks = parse_html(
            r##"<p>See <a href="https://example.com/x?a=1&amp;b=2">the docs</a> and <a class="user-mention" href="/octocat">@octocat</a>,<br>run <code>dotnet test</code> <a href="#anchor">here</a></p>"##,
        );
        assert_eq!(
            dump(&blocks),
            "Paragraph 'See ' 'the docs'@https://example.com/x?a=1&b=2 ' and ' '@octocat'@https://github.com/octocat ',' '\\nrun ' C:'dotnet test' ' here'"
        );
    }

    #[test]
    fn code_blocks_keep_white_space_and_drop_highlight_markup() {
        let blocks = parse_html(
            "<div class=\"highlight highlight-source-cs\"><pre><span class=\"pl-k\">if</span> (a &lt; b)\n{\n    <span class=\"pl-en\">Run</span>();\n}\n</pre></div><p>after</p>",
        );
        assert_eq!(blocks[0].kind, BlockKind::Code);
        let code: String = blocks[0]
            .inlines
            .iter()
            .map(|inline| inline.text.as_str())
            .collect();
        assert_eq!(code, "if (a < b)\n{\n    Run();\n}");
        assert_eq!(blocks[1].inlines[0].text, "after");
    }

    #[test]
    fn lists_get_markers_numbers_and_nesting() {
        let blocks = parse_html(
            "<ul>\n<li>one</li>\n<li>two\n<ol start=\"3\">\n<li>three</li>\n<li><p>four</p></li>\n</ol>\n</li>\n</ul>",
        );
        assert_eq!(
            dump(&blocks),
            "ListItem '\u{2022} ' 'one'\nListItem '\u{2022} ' 'two'\nListItem>1 '3. ' 'three'\nListItem>1 '4. ' 'four'"
        );
    }

    #[test]
    fn task_lists_show_check_boxes() {
        let blocks = parse_html(
            r#"<ul class="contains-task-list"><li class="task-list-item"><input type="checkbox" class="task-list-item-checkbox" disabled="" checked=""> done</li><li class="task-list-item"><input type="checkbox" disabled=""> todo</li></ul>"#,
        );
        assert_eq!(
            dump(&blocks),
            "ListItem '\u{2611} ' 'done'\nListItem '\u{2610} ' 'todo'"
        );
    }

    #[test]
    fn quotes_headings_and_rules() {
        let blocks = parse_html(
            "<h2>Title</h2><blockquote>\n<p>quoted <code>x</code></p>\n</blockquote><hr><p>end</p>",
        );
        assert_eq!(
            dump(&blocks),
            "Heading2 'Title'\nParagraphq 'quoted ' C:'x'\nRule \nParagraph 'end'"
        );
    }

    #[test]
    fn tables_become_one_row_per_line_and_images_become_links() {
        let blocks = parse_html(
            r#"<table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>a</td><td>1</td></tr></tbody></table><p><a href="https://example.com/full.png"><img src="https://example.com/thumb.png" alt="Screenshot"></a><img src="https://example.com/b.png"></p>"#,
        );
        assert_eq!(
            dump(&blocks),
            "TableRow B:'Name' ' | ' B:'Value'\nTableRow 'a' ' | ' '1'\nParagraph '[Screenshot]'@https://example.com/full.png '[image]'@https://example.com/b.png"
        );
    }

    #[test]
    fn nothing_to_show_gives_no_blocks() {
        for html in ["", "   \n ", "<p></p><ul><li></li></ul>"] {
            assert!(parse_html(html).is_empty(), "{html:?}");
        }
        assert!(from_plain_text("").is_empty());
        assert_eq!(
            dump(&from_plain_text("line one\nline two")),
            "Paragraph 'line one\\nline two'"
        );
    }

    #[test]
    fn odd_input_degrades_without_panicking() {
        let cases = [
            ("plain text, no tags", "Paragraph 'plain text, no tags'"),
            (
                "a < b and <p>unclosed",
                "Paragraph 'a < b and'\nParagraph 'unclosed'",
            ),
            ("ends with <", "Paragraph 'ends with <'"),
            (
                "<p>never closed <strong",
                "Paragraph 'never closed <strong'",
            ),
            (
                "<p>text <!-- hidden --> more</p><script>alert(1)</script>",
                "Paragraph 'text more'",
            ),
            (
                "</strong></ul></blockquote><p>still fine</p>",
                "Paragraph 'still fine'",
            ),
            (
                "<p title=\"a > b\">quoted attribute</p>",
                "Paragraph 'quoted attribute'",
            ),
            (
                "<p>caf\u{E9} \u{1F680} <b>\u{4F60}\u{597D}</b></p>",
                "Paragraph 'caf\u{E9} \u{1F680} ' B:'\u{4F60}\u{597D}'",
            ),
        ];
        for (html, expected) in cases {
            assert_eq!(dump(&parse_html(html)), expected, "{html:?}");
        }
    }
}
