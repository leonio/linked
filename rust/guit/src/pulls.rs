//! The pull requests page and the conversation page: their state and their views.
//! The rules for what a row shows live in `guit_core::conversation`; this is state plus layout.

use std::rc::Rc;

use guit_core::conversation::{CommentItem, PullRequestItem, PullRequestScope};
use guit_core::link::SubjectLink;
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
}

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
    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(12.0)
        .margin(Thickness::new(16.0, 16.0, 24.0, 8.0))
        .children((
            Button::new()
                .vertical_alignment(VerticalAlignment::Top)
                .automation_name("Back")
                .content(FontIcon::new().glyph("\u{E72B}").font_size(16.0))
                .on_click(context.message(Message::CloseConversation)),
            StackPanel::new().grid_column(1).spacing(2.0).children((
                TextBlock::new()
                    .text(conversation.title.as_str())
                    .font_size(20.0)
                    .font_weight(FontWeight::SEMI_BOLD)
                    .text_wrapping(TextWrapping::Wrap),
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
                    .opacity(0.7),
            )),
            Button::new()
                .grid_column(2)
                .vertical_alignment(VerticalAlignment::Top)
                .automation_name("Open on GitHub")
                .content(FontIcon::new().glyph("\u{E8A7}").font_size(16.0))
                .on_click(context.message(Message::OpenConversationInBrowser)),
        ));

    // Comments are text of very different heights; the list still only realises what is on screen.
    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::None)
        .items(conversation.comments.iter().map(|comment| {
            DataItem::new(comment.id.to_string(), comment.author.as_str())
                .content(comment_card(comment))
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

    let reply = Grid::new()
        .grid_row(2)
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
        .rows([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .children((header, Grid::new().grid_row(1).children(layers), reply))
        .into()
}

/// One post: who and when on the first line, then the text.
fn comment_card(comment: &CommentItem) -> View {
    let byline = match &comment.association {
        Some(association) => format!("{} \u{00B7} {}", association, comment.posted),
        None => comment.posted.clone(),
    };
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
            TextBlock::new()
                .text(comment.body.as_str())
                .text_wrapping(TextWrapping::Wrap)
                .is_text_selection_enabled(true),
        ))
        .into()
}
