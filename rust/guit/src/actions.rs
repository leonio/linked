//! The Actions pages: workflow runs of a repository, one run's jobs and steps, and a job's log.
//! The rules for what a row shows live in `guit_core::actions`; this is state plus layout.

use std::rc::Rc;

use guit_core::actions::{JobRow, RunItem, RunStatus};
use windows_reactor::*;

use crate::{Guit, Message, error_bar};

/// Logs can run to hundreds of thousands of lines; the end is where failures are.
pub const MAX_LOG_LINES: usize = 5_000;

/// The Actions section's state: the run list, and whichever run and log are open on top of it.
#[derive(Default)]
pub struct Actions {
    /// "owner/name" of the member's most recently pushed repositories.
    pub repositories: Vec<String>,
    pub selected: Option<usize>,
    pub runs: Vec<RunItem>,
    pub loading: bool,
    pub error: Option<String>,
    /// Bumped by every load of the run list, so the reply of a superseded load is ignored.
    pub generation: u64,
    /// False until the section has been shown once; the first visit loads it.
    pub loaded: bool,
    pub run: Option<RunDetail>,
}

impl Actions {
    /// "owner" and "name" of the selected repository.
    pub fn repository(&self) -> Option<(String, String)> {
        let (owner, name) = self.repositories.get(self.selected?)?.split_once('/')?;
        Some((owner.to_string(), name.to_string()))
    }
}

/// One workflow run being looked at.
pub struct RunDetail {
    pub owner: String,
    pub repo: String,
    pub run_id: u64,
    pub title: String,
    pub detail: String,
    pub status: Option<RunStatus>,
    pub rows: Vec<JobRow>,
    pub loading: bool,
    pub rerunning: bool,
    pub error: Option<String>,
    /// A neutral status line, for example while waiting for a re-run to start.
    pub notice: Option<String>,
    /// After a re-run GitHub needs a moment before the run reads as queued; keep checking this many more times.
    pub forced_ticks: u32,
    /// The follow timer. Dropping it stops the refreshing.
    pub timer: Option<ComponentTimer>,
    pub log: Option<JobLog>,
}

impl RunDetail {
    pub fn opening(owner: String, repo: String, run: &RunItem) -> Self {
        Self {
            owner,
            repo,
            run_id: run.id,
            title: run.title.clone(),
            detail: run.detail.clone(),
            status: Some(run.status),
            rows: Vec::new(),
            loading: true,
            rerunning: false,
            error: None,
            notice: None,
            forced_ticks: 0,
            timer: None,
            log: None,
        }
    }

    /// True while the page should keep refreshing itself.
    pub fn should_follow(&self) -> bool {
        self.status.is_some_and(RunStatus::is_live) || self.forced_ticks > 0
    }
}

/// The log of one job.
pub struct JobLog {
    pub job_id: u64,
    pub name: String,
    pub lines: Rc<Vec<String>>,
    pub loading: bool,
    pub error: Option<String>,
    pub notice: Option<String>,
}

pub fn page(actions: &Actions, context: &ViewContext<Guit>) -> View {
    match &actions.run {
        Some(RunDetail { log: Some(log), .. }) => log_page(log, context),
        Some(run) => run_page(run, context),
        None => runs_page(actions, context),
    }
}

fn runs_page(actions: &Actions, context: &ViewContext<Guit>) -> View {
    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(16.0)
        .margin(Thickness::new(24.0, 16.0, 24.0, 8.0))
        .children((
            TextBlock::new().text("Actions").font_size(28.0),
            ComboBox::new()
                .grid_column(1)
                .min_width(280.0)
                .horizontal_alignment(HorizontalAlignment::Left)
                .vertical_alignment(VerticalAlignment::Center)
                .automation_name("Repository")
                .placeholder_text("Choose a repository")
                .items_source(actions.repositories.clone())
                .selected_index(actions.selected)
                .on_selection_changed(context.callback(Message::RepositorySelected)),
            Button::new()
                .grid_column(2)
                .vertical_alignment(VerticalAlignment::Center)
                .automation_name("Refresh")
                .content(FontIcon::new().glyph("\u{E72C}").font_size(16.0))
                .on_click(context.message(Message::ReloadRuns)),
        ));

    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::Single)
        .on_selection_changed(context.callback(Message::OpenRun))
        .items(actions.runs.iter().map(|run| {
            DataItem::new(run.id.to_string(), run.title.as_str()).content(run_row(run))
        }));

    let mut layers: Vec<View> = vec![list.into()];
    if actions.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    } else if let Some(error) = &actions.error {
        layers.push(top_bar(error_bar(error)));
    } else if actions.runs.is_empty() {
        layers.push(
            TextBlock::new()
                .text("No workflow runs")
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

fn run_row(run: &RunItem) -> View {
    let byline = if run.actor.is_empty() {
        run.detail.clone()
    } else {
        format!("{} \u{00B7} {}", run.detail, run.actor)
    };
    Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(12.0)
        .height(56.0)
        .children((
            status_icon(run.status, 16.0),
            StackPanel::new()
                .grid_column(1)
                .vertical_alignment(VerticalAlignment::Center)
                .children((
                    TextBlock::new()
                        .text(run.title.as_str())
                        .font_weight(FontWeight::SEMI_BOLD)
                        .text_trimming(TextTrimming::CharacterEllipsis),
                    TextBlock::new()
                        .text(byline)
                        .font_size(12.0)
                        .opacity(0.7)
                        .text_trimming(TextTrimming::CharacterEllipsis),
                )),
            TextBlock::new()
                .text(run.updated.as_str())
                .grid_column(2)
                .font_size(12.0)
                .opacity(0.7)
                .vertical_alignment(VerticalAlignment::Center),
        ))
        .into()
}

fn run_page(run: &RunDetail, context: &ViewContext<Guit>) -> View {
    let header = Grid::new()
        .columns([
            GridLength::Auto,
            GridLength::Auto,
            GridLength::STAR,
            GridLength::Auto,
        ])
        .column_spacing(12.0)
        .margin(Thickness::new(16.0, 16.0, 24.0, 8.0))
        .children((
            back_button(context.message(Message::CloseRun)),
            status_icon(run.status.unwrap_or(RunStatus::Other), 20.0).grid_column(1),
            StackPanel::new().grid_column(2).spacing(2.0).children((
                TextBlock::new()
                    .text(run.title.as_str())
                    .font_size(20.0)
                    .font_weight(FontWeight::SEMI_BOLD)
                    .text_wrapping(TextWrapping::Wrap),
                TextBlock::new()
                    .text(format!(
                        "{}/{} \u{00B7} {}",
                        run.owner, run.repo, run.detail
                    ))
                    .font_size(12.0)
                    .opacity(0.7),
            )),
            StackPanel::new()
                .grid_column(3)
                .orientation(Orientation::Horizontal)
                .spacing(8.0)
                .children((
                    Button::new()
                        .content("Re-run all jobs")
                        .is_enabled(!run.loading && !run.rerunning)
                        .on_click(context.message(Message::Rerun)),
                    Button::new()
                        .automation_name("Refresh")
                        .content(FontIcon::new().glyph("\u{E72C}").font_size(16.0))
                        .on_click(context.message(Message::ReloadRun)),
                    Button::new()
                        .automation_name("Open on GitHub")
                        .content(FontIcon::new().glyph("\u{E8A7}").font_size(16.0))
                        .on_click(context.message(Message::OpenRunInBrowser)),
                )),
        ));

    // One flat list: job rows with their step rows indented beneath. Selecting either opens the job's log.
    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::Single)
        .on_selection_changed(context.callback(Message::OpenJob))
        .items(run.rows.iter().enumerate().map(|(index, row)| {
            DataItem::new(format!("{}-{index}", row.job_id), row.name.as_str())
                .content(job_row(row))
        }));

    let mut layers: Vec<View> = vec![list.into()];
    if run.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    }
    if run.should_follow() {
        layers.push(
            ProgressBar::new()
                .is_indeterminate(true)
                .vertical_alignment(VerticalAlignment::Top)
                .into(),
        );
    }
    if let Some(error) = &run.error {
        layers.push(top_bar(error_bar(error)));
    } else if let Some(notice) = &run.notice {
        layers.push(top_bar(
            InfoBar::new()
                .is_open(true)
                .is_closable(false)
                .message(notice.as_str()),
        ));
    }

    Grid::new()
        .rows([GridLength::Auto, GridLength::STAR])
        .children((header, Grid::new().grid_row(1).children(layers)))
        .into()
}

fn job_row(row: &JobRow) -> View {
    Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(12.0)
        .height(if row.is_step { 32.0 } else { 44.0 })
        .margin(Thickness::new(
            if row.is_step { 28.0 } else { 0.0 },
            0.0,
            0.0,
            0.0,
        ))
        .children((
            status_icon(row.status, 14.0),
            TextBlock::new()
                .grid_column(1)
                .text(row.name.as_str())
                .font_weight(if row.is_step {
                    FontWeight::NORMAL
                } else {
                    FontWeight::SEMI_BOLD
                })
                .vertical_alignment(VerticalAlignment::Center)
                .text_trimming(TextTrimming::CharacterEllipsis),
            TextBlock::new()
                .grid_column(2)
                .text(row.duration.as_str())
                .font_size(12.0)
                .opacity(0.7)
                .vertical_alignment(VerticalAlignment::Center),
        ))
        .into()
}

fn log_page(log: &JobLog, context: &ViewContext<Guit>) -> View {
    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR])
        .column_spacing(12.0)
        .margin(Thickness::new(16.0, 16.0, 24.0, 8.0))
        .children((
            back_button(context.message(Message::CloseLog)),
            TextBlock::new()
                .grid_column(1)
                .text(log.name.as_str())
                .font_size(20.0)
                .font_weight(FontWeight::SEMI_BOLD)
                .vertical_alignment(VerticalAlignment::Center),
        ));

    // One row per log line, so even a 5,000-line log only creates what is on screen.
    let lines = Rc::clone(&log.lines);
    let source = VirtualSource::new(
        log.job_id,
        lines.len(),
        |index| index as u64,
        move |index| {
            TextBlock::new()
                .text(lines[index].as_str())
                .font_family("Cascadia Mono, Consolas")
                .font_size(12.0)
                .text_wrapping(TextWrapping::Wrap)
                .is_text_selection_enabled(true)
        },
    );

    let mut layers: Vec<View> = vec![
        ScrollViewer::new()
            .margin(Thickness::new(24.0, 0.0, 24.0, 12.0))
            .content(ItemsRepeater::new().virtual_source(source))
            .into(),
    ];
    if log.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    }
    if let Some(error) = &log.error {
        layers.push(top_bar(
            InfoBar::new()
                .is_open(true)
                .is_closable(false)
                .severity(InfoBarSeverity::Warning)
                .message(error.as_str()),
        ));
    } else if let Some(notice) = &log.notice {
        layers.push(top_bar(
            InfoBar::new()
                .is_open(true)
                .is_closable(false)
                .message(notice.as_str()),
        ));
    }

    Grid::new()
        .rows([GridLength::Auto, GridLength::STAR])
        .children((header, Grid::new().grid_row(1).children(layers)))
        .into()
}

fn status_icon(status: RunStatus, size: f64) -> FontIcon {
    FontIcon::new()
        .glyph(status.glyph())
        .font_size(size)
        .automation_name(status.label())
        .vertical_alignment(VerticalAlignment::Center)
}

fn back_button(on_click: Callback<()>) -> Button {
    Button::new()
        .vertical_alignment(VerticalAlignment::Top)
        .automation_name("Back")
        .content(FontIcon::new().glyph("\u{E72B}").font_size(16.0))
        .on_click(on_click)
}

fn top_bar(bar: InfoBar) -> View {
    bar.vertical_alignment(VerticalAlignment::Top)
        .margin(Thickness::new(24.0, 0.0, 24.0, 0.0))
        .into()
}
