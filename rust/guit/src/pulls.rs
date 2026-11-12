//! The pull requests page and the conversation page: their state and their views.
//! The rules for what a row shows live in `guit_core::conversation`; this is state plus layout.

use std::rc::Rc;

use guit_core::conversation::{
    CommentItem, PullHeader, PullRequestItem, PullRequestScope, REACTIONS,
};
use guit_core::diff::{DiffKind, DiffRow};
use guit_core::link::SubjectLink;
use guit_core::rich::{Block, BlockKind};
use windows_reactor::*;

use crate::{Guit, Message, error_bar};

/// The member's open pull requests for one scope.
#[derive(Default)]
pub struct Pulls {
    pub scope: PullRequestScope,
    pub items: Vec<PullRequestItem>,
    pub loading: bool,
    pub error: Option<String>,
    /// Bumped by every load, so the reply of a superseded load is ignored.
    pub generation: u64,
    /// False until the page has been shown once; the first visit loads it.
    pub loaded: bool,
}

/// One issue or pull request being read.
pub struct Conversation {
    pub link: SubjectLink,
    pub title: String,
    pub state: String,
    pub comments: Vec<CommentItem>,
    pub loading: bool,
    pub error: Option<String>,
    /// The reply being written.
    pub draft: String,
    pub sending: bool,
    /// Set for a pull request: state, branches and diff size for the header.
    pub pull: Option<PullHeader>,
    /// Pages of comments loaded so far, and whether the last one was full.
    pub page: u32,
    pub has_more: bool,
    pub loading_more: bool,
    /// The "Files changed" view, when it is open on top of the conversation.
    pub files: Option<Files>,
}

/// Files fetched per request (GitHub's maximum).
pub const FILES_PER_PAGE: u32 = 100;

/// The changed files of a pull request as one flat list of diff rows.
pub struct Files {
    pub rows: Rc<Vec<DiffRow>>,
    pub loading: bool,
    pub error: Option<String>,
    pub page: u32,
    /// Bumped whenever `rows` changes, for the virtualised list.
    pub revision: u64,
}

impl Default for Files {
    fn default() -> Self {
        Self {
            rows: Rc::new(Vec::new()),
            loading: true,
            error: None,
            page: 0,
            revision: 0,
        }
    }
}

/// A row that stands for "there are more files": the view shows it as a button.
pub fn more_files_marker() -> DiffRow {
    DiffRow {
        kind: DiffKind::Note,
        text: MORE_FILES.to_string(),
        old_number: String::new(),
        new_number: String::new(),
        detail: String::new(),
    }
}

pub fn is_more_files_marker(row: &DiffRow) -> bool {
    row.kind == DiffKind::Note && row.text == MORE_FILES
}

const MORE_FILES: &str = "\u{0}more-files";

impl Conversation {
    pub fn opening(link: SubjectLink) -> Self {
        Self {
            link,
            title: String::new(),
            state: String::new(),
            comments: Vec::new(),
            loading: true,
            error: None,
            draft: String::new(),
            sending: false,
            pull: None,
            page: 1,
            has_more: false,
            loading_more: false,
            files: None,
        }
    }

    pub fn can_send(&self) -> bool {
        !self.sending && !self.loading && !self.draft.trim().is_empty()
    }
}

pub fn pulls_page(pulls: &Pulls, context: &ViewContext<Guit>) -> View {
    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(16.0)
        .margin(Thickness::new(24.0, 16.0, 24.0, 8.0))
        .children((
            TextBlock::new().text("Pull requests").font_size(28.0),
            SelectorBar::new()
                .grid_column(1)
                .vertical_alignment(VerticalAlignment::Bottom)
                .items(
                    PullRequestScope::ALL
                        .into_iter()
                        .map(|scope| {
                            SelectorBarItem::new()
                                .text(scope.label())
                                .is_selected(scope == pulls.scope)
                                .into()
                        })
                        .collect::<Vec<View>>(),
                )
                .on_selected_text_changed(context.callback(|text: Option<Rc<str>>| {
                    Message::ScopeSelected(PullRequestScope::from_label(text.as_deref()))
                })),
            Button::new()
                .grid_column(2)
                .vertical_alignment(VerticalAlignment::Center)
                .automation_name("Refresh")
                .content(FontIcon::new().glyph("\u{E72C}").font_size(16.0))
                .on_click(context.message(Message::ReloadPulls)),
        ));

    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::Single)
        .on_selection_changed(context.callback(Message::OpenPull))
        .items(pulls.items.iter().map(|item| {
            DataItem::new(item.reference.as_str(), item.title.as_str()).content(pull_row(item))
        }));

    let mut layers: Vec<View> = vec![list.into()];
    if pulls.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    } else if let Some(error) = &pulls.error {
        layers.push(
            error_bar(error)
                .vertical_alignment(VerticalAlignment::Top)
                .margin(Thickness::new(24.0, 0.0, 24.0, 0.0))
                .into(),
        );
    } else if pulls.items.is_empty() {
        layers.push(
            TextBlock::new()
                .text("No open pull requests here")
                .font_size(20.0)
                .opacity(0.6)
                .horizontal_alignment(HorizontalAlignment::Center)
                .vertical_alignment(VerticalAlignment::Center)
                .into(),
        );
    }

    Grid::new()
        .rows([GridLength::Auto, GridLength::STAR])
        .children((header, Grid::new().grid_row(1).children(layers)))
        .into()
}

fn pull_row(item: &PullRequestItem) -> View {
    Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(12.0)
        .height(56.0)
        .children((
            FontIcon::new()
                .glyph(item.glyph)
                .font_size(16.0)
                .vertical_alignment(VerticalAlignment::Center),
            StackPanel::new()
                .grid_column(1)
                .vertical_alignment(VerticalAlignment::Center)
                .children((
                    TextBlock::new()
                        .text(item.title.as_str())
                        .font_weight(FontWeight::SEMI_BOLD)
                        .text_trimming(TextTrimming::CharacterEllipsis),
                    TextBlock::new()
                        .text(format!("{} \u{00B7} {}", item.reference, item.author))
                        .font_size(12.0)
                        .opacity(0.7)
                        .text_trimming(TextTrimming::CharacterEllipsis),
                )),
            TextBlock::new()
                .text(item.updated.as_str())
                .grid_column(2)
                .font_size(12.0)
                .opacity(0.7)
                .vertical_alignment(VerticalAlignment::Center),
        ))
        .into()
}

pub fn conversation_page(conversation: &Conversation, context: &ViewContext<Guit>) -> View {
    if let Some(files) = &conversation.files {
        return files_page(conversation, files, context);
    }

    let mut title_lines: Vec<View> = vec![
        TextBlock::new()
            .text(conversation.title.as_str())
            .font_size(20.0)
            .font_weight(FontWeight::SEMI_BOLD)
            .text_wrapping(TextWrapping::Wrap)
            .into(),
        TextBlock::new()
            .text(if conversation.state.is_empty() {
                conversation.link.reference()
            } else {
                format!(
                    "{} \u{00B7} {}",
                    conversation.link.reference(),
                    conversation.state
                )
            })
            .font_size(12.0)
            .opacity(0.7)
            .into(),
    ];
    let mut buttons: Vec<View> = Vec::new();
    if let Some(pull) = &conversation.pull {
        title_lines.push(
            TextBlock::new()
                .text(format!("{} \u{00B7} {}", pull.branches, pull.diff_summary))
                .font_size(12.0)
                .opacity(0.7)
                .into(),
        );
        buttons.push(
            Button::new()
                .content("Files changed")
                .on_click(context.message(Message::ShowFiles))
                .into(),
        );
    }
    buttons.push(
        Button::new()
            .automation_name("Open on GitHub")
            .content(FontIcon::new().glyph("\u{E8A7}").font_size(16.0))
            .on_click(context.message(Message::OpenConversationInBrowser))
            .into(),
    );

    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(12.0)
        .margin(Thickness::new(16.0, 16.0, 24.0, 8.0))
        .children((
            back_button(context.message(Message::CloseConversation)),
            StackPanel::new()
                .grid_column(1)
                .spacing(2.0)
                .children(title_lines),
            StackPanel::new()
                .grid_column(2)
                .orientation(Orientation::Horizontal)
                .spacing(8.0)
                .vertical_alignment(VerticalAlignment::Top)
                .children(buttons),
        ));

    // Comments are text of very different heights; the list still only realises what is on screen.
    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::None)
        .items(conversation.comments.iter().map(|comment| {
            DataItem::new(comment.id.to_string(), comment.author.as_str())
                .content(comment_card(comment, context))
        }));

    let mut layers: Vec<View> = vec![list.into()];
    if conversation.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    }
    if let Some(error) = &conversation.error {
        layers.push(
            error_bar(error)
                .vertical_alignment(VerticalAlignment::Top)
                .margin(Thickness::new(24.0, 0.0, 24.0, 0.0))
                .into(),
        );
    }

    let more: View = if conversation.loading_more {
        ProgressBar::new().grid_row(2).is_indeterminate(true).into()
    } else if conversation.has_more {
        Button::new()
            .grid_row(2)
            .margin(Thickness::new(24.0, 8.0, 24.0, 0.0))
            .horizontal_alignment(HorizontalAlignment::Center)
            .content("Load more comments")
            .on_click(context.message(Message::LoadMoreComments))
            .into()
    } else {
        Grid::new().grid_row(2).into()
    };

    let reply = Grid::new()
        .grid_row(3)
        .columns([GridLength::STAR, GridLength::Auto])
        .column_spacing(8.0)
        .margin(Thickness::new(24.0, 8.0, 24.0, 16.0))
        .children((
            TextBox::new()
                .text(conversation.draft.as_str())
                .accepts_return(true)
                .text_wrapping(TextWrapping::Wrap)
                .max_height(160.0)
                .placeholder_text("Write a comment")
                .on_text_changed(
                    context.callback(|text: Rc<str>| Message::DraftChanged(text.to_string())),
                ),
            Button::new()
                .grid_column(1)
                .vertical_alignment(VerticalAlignment::Bottom)
                .is_enabled(conversation.can_send())
                .content("Comment")
                .on_click(context.message(Message::SendComment)),
        ));

    Grid::new()
        .rows([
            GridLength::Auto,
            GridLength::STAR,
            GridLength::Auto,
            GridLength::Auto,
        ])
        .children((
            header,
            Grid::new().grid_row(1).children(layers),
            more,
            reply,
        ))
        .into()
}

fn back_button(on_click: Callback<()>) -> Button {
    Button::new()
        .vertical_alignment(VerticalAlignment::Top)
        .automation_name("Back")
        .content(FontIcon::new().glyph("\u{E72B}").font_size(16.0))
        .on_click(on_click)
}

/// One post: who and when on the first line, then the text, then its reactions.
fn comment_card(comment: &CommentItem, context: &ViewContext<Guit>) -> View {
    let byline = match &comment.association {
        Some(association) => format!("{} \u{00B7} {}", association, comment.posted),
        None => comment.posted.clone(),
    };
    let comment_id = comment.id;
    let reactions = comment.reaction_summary();
    StackPanel::new()
        .spacing(6.0)
        .margin(Thickness::new(0.0, 8.0, 0.0, 8.0))
        .children((
            StackPanel::new()
                .orientation(Orientation::Horizontal)
                .spacing(8.0)
                .children((
                    TextBlock::new()
                        .text(comment.author.as_str())
                        .font_weight(FontWeight::SEMI_BOLD),
                    TextBlock::new()
                        .text(byline)
                        .font_size(12.0)
                        .opacity(0.7)
                        .vertical_alignment(VerticalAlignment::Center),
                )),
            RichTextBlock::new()
                .paragraphs(rich_text(&comment.content))
                .text_wrapping(TextWrapping::Wrap)
                .is_text_selection_enabled(true),
            StackPanel::new()
                .orientation(Orientation::Horizontal)
                .spacing(8.0)
                .children((
                    DropDownButton::new()
                        .automation_name("Add reaction")
                        .content(FontIcon::new().glyph("\u{E76E}").font_size(14.0))
                        .menu(Menu::new(
                            REACTIONS.map(|(name, emoji)| {
                                MenuItem::item(name, format!("{emoji}  {name}"))
                            }),
                            context.callback(move |key: Key| Message::React {
                                comment_id,
                                content: key.as_str().unwrap_or_default().to_string(),
                            }),
                        )),
                    TextBlock::new()
                        .text(reactions)
                        .vertical_alignment(VerticalAlignment::Center),
                )),
        ))
        .into()
}

/// The "Files changed" view: every file and every diff line is one row of a virtualised list.
fn files_page(conversation: &Conversation, files: &Files, context: &ViewContext<Guit>) -> View {
    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR])
        .column_spacing(12.0)
        .margin(Thickness::new(16.0, 16.0, 24.0, 8.0))
        .children((
            back_button(context.message(Message::CloseFiles)),
            StackPanel::new().grid_column(1).spacing(2.0).children((
                TextBlock::new()
                    .text("Files changed")
                    .font_size(20.0)
                    .font_weight(FontWeight::SEMI_BOLD),
                TextBlock::new()
                    .text(conversation.link.reference())
                    .font_size(12.0)
                    .opacity(0.7),
            )),
        ));

    let rows = Rc::clone(&files.rows);
    let load_more = context.message(Message::LoadMoreFiles);
    let source = VirtualSource::new(
        files.revision,
        rows.len(),
        |index| index as u64,
        move |index| diff_row(&rows[index], &load_more),
    );

    let mut layers: Vec<View> = vec![
        ScrollViewer::new()
            .margin(Thickness::new(12.0, 0.0, 12.0, 12.0))
            .content(ItemsRepeater::new().virtual_source(source))
            .into(),
    ];
    if files.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    }
    if let Some(error) = &files.error {
        layers.push(
            error_bar(error)
                .vertical_alignment(VerticalAlignment::Top)
                .margin(Thickness::new(24.0, 0.0, 24.0, 0.0))
                .into(),
        );
    } else if !files.loading && files.rows.is_empty() {
        layers.push(
            TextBlock::new()
                .text("No files changed")
                .font_size(20.0)
                .opacity(0.6)
                .horizontal_alignment(HorizontalAlignment::Center)
                .vertical_alignment(VerticalAlignment::Center)
                .into(),
        );
    }

    Grid::new()
        .rows([GridLength::Auto, GridLength::STAR])
        .children((header, Grid::new().grid_row(1).children(layers)))
        .into()
}

// Diff colours are the one place the app sets a colour itself: green and red carry the meaning.
// They are translucent so the same colour reads on light and dark backgrounds. Same values as the C# app.
const ADDED: Color = Color::argb(0x38, 0x2E, 0xA0, 0x43);
const REMOVED: Color = Color::argb(0x38, 0xF8, 0x51, 0x49);
const HUNK: Color = Color::argb(0x28, 0x38, 0x8B, 0xFD);
const FILE: Color = Color::argb(0x18, 0x80, 0x80, 0x80);

fn diff_row(row: &DiffRow, load_more: &Callback<()>) -> View {
    if is_more_files_marker(row) {
        return Button::new()
            .margin(Thickness::new(0.0, 12.0, 0.0, 12.0))
            .horizontal_alignment(HorizontalAlignment::Center)
            .content("Load more files")
            .on_click(load_more.clone())
            .into();
    }

    let background = match row.kind {
        DiffKind::Added => ADDED,
        DiffKind::Removed => REMOVED,
        DiffKind::Hunk => HUNK,
        DiffKind::File => FILE,
        DiffKind::Context | DiffKind::Note => Color::transparent(),
    };
    let number = |text: &str, column: i32| {
        TextBlock::new()
            .grid_column(column)
            .text(text)
            .font_family("Cascadia Mono, Consolas")
            .font_size(12.0)
            .opacity(0.6)
            .horizontal_alignment(HorizontalAlignment::Right)
    };
    Grid::new()
        .columns([
            GridLength::Pixel(44.0),
            GridLength::Pixel(44.0),
            GridLength::STAR,
            GridLength::Auto,
        ])
        .column_spacing(8.0)
        .background(background)
        // A file heading gets room above it, so files read as separate blocks.
        .margin(Thickness::new(
            0.0,
            if row.kind == DiffKind::File {
                16.0
            } else {
                0.0
            },
            0.0,
            0.0,
        ))
        .children((
            number(&row.old_number, 0),
            number(&row.new_number, 1),
            TextBlock::new()
                .grid_column(2)
                .text(row.text.as_str())
                .font_family("Cascadia Mono, Consolas")
                .font_size(12.0)
                .font_weight(if row.kind == DiffKind::File {
                    FontWeight::SEMI_BOLD
                } else {
                    FontWeight::NORMAL
                })
                .text_wrapping(TextWrapping::Wrap)
                .is_text_selection_enabled(true),
            TextBlock::new()
                .grid_column(3)
                .text(row.detail.as_str())
                .font_size(12.0)
                .opacity(0.7),
        ))
        .into()
}

/// Turns a comment's blocks into reactor's rich text. Reactor's runs carry bold, italic and links; what it has no
/// property for yet is shown with characters instead: a bar for quotes, wide spaces for nesting, a line for rules.
/// Code keeps its text but not a monospace font.
fn rich_text(blocks: &[Block]) -> RichText {
    RichText::new(blocks.iter().map(|block| {
        let mut inlines = Vec::new();
        let mut lead = "\u{2003}\u{2003}".repeat(usize::from(block.indent));
        if block.is_quote {
            lead.push_str("\u{258E} ");
        }
        if !lead.is_empty() {
            inlines.push(RichTextInline::Run(RichTextRun::plain(lead)));
        }

        if block.kind == BlockKind::Rule {
            inlines.push(RichTextInline::Run(RichTextRun::plain(
                "\u{2015}".repeat(12),
            )));
        }

        let heading = block.kind == BlockKind::Heading;
        for inline in &block.inlines {
            // Each '\n' becomes a line break; the text between them becomes a run or a link.
            for (index, line) in inline.text.split('\n').enumerate() {
                if index > 0 {
                    inlines.push(RichTextInline::LineBreak);
                }
                if line.is_empty() {
                    continue;
                }

                inlines.push(match &inline.link {
                    Some(uri) => RichTextInline::Hyperlink(RichTextHyperlink {
                        text: line.to_string(),
                        uri: uri.clone(),
                    }),
                    None => RichTextInline::Run(RichTextRun {
                        text: line.to_string(),
                        is_bold: inline.bold || heading,
                        is_italic: inline.italic || block.is_quote,
                    }),
                });
            }
        }

        RichTextParagraph::new(inlines)
    }))
}
