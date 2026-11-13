//! Guit, the Rust build. One window: a sign-in view, then a shell with the inbox.
//!
//! This is the same app as `src/Guit.App` (C#, WinUI 3, Native AOT), written on windows-reactor so the
//! two can be measured side by side. Logic lives in `guit-core`; this crate is the UI and the
//! Windows platform layer.
#![windows_subsystem = "windows"]

mod actions;
mod platform;
mod pulls;
mod settings_page;

use std::rc::Rc;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use actions::{Actions, JobLog, RunDetail};
use guit_core::actions::{JobRow, RunItem, log_lines};
use guit_core::api::{ApiError, DeviceCode, GitHub, User};
use guit_core::conversation::{CommentItem, PullHeader, PullRequestItem, PullRequestScope};
use guit_core::device_flow::{DeviceFlowOutcome, wait_for_token};
use guit_core::diff::{self, DiffRow};
use guit_core::inbox::{Filter, Inbox, Kind, NotificationItem, PageRequest, Removed};
use guit_core::settings::{Settings, Shortcut, Theme};
use guit_core::{GITHUB_CLIENT_ID, GITHUB_SCOPES};
use platform::WinRtTransport;
use pulls::{Conversation, Files, Pulls};
use windows_reactor::*;

/// How often the inbox checks for changes in the background.
const POLL_PERIOD: Duration = Duration::from_secs(60);

/// Why a page was fetched, so its reply goes to the right place.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Purpose {
    FirstPage,
    NextPage,
    Refresh,
}

/// Everything that can happen. Messages cross threads (background work answers with one), so they own their data.
#[derive(Clone)]
pub(crate) enum Message {
    SignIn,
    DeviceCodeReady(Result<DeviceCode, String>),
    SignInFinished(Result<DeviceFlowOutcome, String>),
    CancelSignIn,
    OpenVerificationPage,
    SignOut,
    UserLoaded(Option<User>),
    FilterSelected(Filter),
    KindSelected(Option<usize>),
    Reload,
    LoadMore,
    PollTick,
    PageLoaded {
        generation: u64,
        purpose: Purpose,
        result: Result<Vec<NotificationItem>, ApiError>,
    },
    Open(Option<usize>),
    MarkDone(String),
    MarkDoneFinished {
        id: String,
        error: Option<String>,
    },

    // The Actions pages.
    RepositoriesLoaded(Result<Vec<String>, ApiError>),
    RepositorySelected(Option<usize>),
    ReloadRuns,
    RunsLoaded {
        generation: u64,
        result: Result<Vec<RunItem>, ApiError>,
    },
    OpenRun(Option<usize>),
    ReloadRun,
    FollowRun,
    RunLoaded {
        run_id: u64,
        /// A quiet load is the background follow: it shows no spinner and does not report failures.
        quiet: bool,
        result: Result<(RunItem, Vec<JobRow>), ApiError>,
    },
    Rerun,
    RerunFinished {
        run_id: u64,
        error: Option<String>,
    },
    CloseRun,
    OpenRunInBrowser,
    OpenJob(Option<usize>),
    JobLogLoaded {
        job_id: u64,
        result: Result<(Vec<String>, bool), ApiError>,
    },
    CloseLog,

    LoadMoreComments,
    MoreCommentsLoaded {
        reference: String,
        result: Result<Vec<CommentItem>, ApiError>,
    },
    React {
        comment_id: u64,
        content: String,
    },
    ReactFinished {
        reference: String,
        comment_id: u64,
        content: String,
        error: Option<String>,
    },
    ShowFiles,
    LoadMoreFiles,
    FilesLoaded {
        reference: String,
        page: u32,
        result: Result<Vec<DiffRow>, ApiError>,
    },
    CloseFiles,

    // The shell.
    Navigate(Option<String>),
    Shortcut(Shortcut),

    // The settings page.
    ThemeSelected(Option<usize>),
    OpenProject,

    // The pull requests page.
    ScopeSelected(PullRequestScope),
    ReloadPulls,
    PullsLoaded {
        generation: u64,
        result: Result<Vec<PullRequestItem>, ApiError>,
    },
    OpenPull(Option<usize>),

    // The conversation page.
    ConversationLoaded {
        reference: String,
        result: Result<LoadedConversation, ApiError>,
    },
    CloseConversation,
    OpenConversationInBrowser,
    DraftChanged(String),
    SendComment,
    CommentSent {
        reference: String,
        result: Result<CommentItem, ApiError>,
    },
}

/// What a conversation load brings back.
#[derive(Clone)]
pub(crate) struct LoadedConversation {
    title: String,
    state: String,
    comments: Vec<CommentItem>,
    /// Set for a pull request whose details could be fetched.
    pull: Option<PullHeader>,
    has_more: bool,
}

/// The sections in the navigation pane.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Section {
    Inbox,
    PullRequests,
    Actions,
    Settings,
}

/// The navigation pane's tags, in pane order (Ctrl+1, Ctrl+2, Ctrl+3 pick the first three).
const SECTION_TAGS: [&str; 4] = ["inbox", "pulls", "actions", "settings"];

/// The sign-in view's state.
#[derive(Default)]
struct SignIn {
    /// The code to type on github.com, while a sign-in is in progress.
    user_code: Option<String>,
    verification_uri: String,
    error: Option<String>,
    busy: bool,
    task: Option<ComponentTask>,
}

/// The signed-in shell's state.
struct Shell {
    token: String,
    login: String,
    /// The inbox's rows and rules; see `guit_core::inbox::Inbox`.
    inbox: Inbox,
    error: Option<String>,
    /// Rows taken out by "mark as done" whose request is still in flight, kept to put back on failure.
    pending_done: Vec<Removed>,
    /// The background refresh. Dropping it cancels the next tick.
    poll: Option<ComponentTimer>,
    section: Section,
    pulls: Pulls,
    /// The issue or pull request being read, shown in place of the section's page.
    conversation: Option<Conversation>,
    actions: Actions,
}

enum Session {
    SignedOut(SignIn),
    SignedIn(Shell),
}

pub(crate) struct Guit {
    session: Session,
    /// Shared with the C# app through the same file, so a theme chosen in one build applies to the other.
    settings: Settings,
}

fn now() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |elapsed| elapsed.as_secs() as i64)
}

impl Guit {
    fn enter_shell(&mut self, token: String, context: &ComponentContext<Self>) {
        let user_token = token.clone();
        _ = context.spawn_background(move |_| {
            Message::UserLoaded(GitHub::new(&WinRtTransport).user(&user_token).ok())
        });

        let mut inbox = Inbox::default();
        let first = inbox.begin_load();
        fetch(context, &token, first, Purpose::FirstPage);
        self.session = Session::SignedIn(Shell {
            token,
            login: String::new(),
            inbox,
            error: None,
            pending_done: Vec::new(),
            poll: Some(context.set_timeout(POLL_PERIOD, Message::PollTick)),
            section: Section::Inbox,
            pulls: Pulls::default(),
            conversation: None,
            actions: Actions::default(),
        });
    }

    fn sign_out(&mut self) {
        platform::clear_token();
        self.session = Session::SignedOut(SignIn::default());
    }
}

/// Fetches one page of notifications off the UI thread and reports back with `Message::PageLoaded`.
fn fetch(context: &ComponentContext<Guit>, token: &str, request: PageRequest, purpose: Purpose) {
    let token = token.to_string();
    _ = context.spawn_background(move |_| {
        let result = GitHub::new(&WinRtTransport)
            .notifications(
                &token,
                request.all,
                request.participating,
                request.page,
                request.per_page,
            )
            .map(|threads| {
                let now = now();
                threads
                    .iter()
                    .map(|thread| NotificationItem::from_thread(thread, now))
                    .collect()
            });
        Message::PageLoaded {
            generation: request.generation,
            purpose,
            result,
        }
    });
}

/// The first 100 comments are loaded in one request; longer conversations show their start.
const COMMENT_COUNT: u32 = 100;

impl Shell {
    /// Shows an issue or pull request in the app and starts loading it.
    fn open_conversation(
        &mut self,
        link: guit_core::link::SubjectLink,
        context: &ComponentContext<Guit>,
    ) {
        let token = self.token.clone();
        let (owner, repo, number, reference, is_pull) = (
            link.owner.clone(),
            link.repo.clone(),
            link.number,
            link.reference(),
            link.is_pull_request,
        );
        self.conversation = Some(Conversation::opening(link));
        _ = context.spawn_background(move |_| {
            let github = GitHub::new(&WinRtTransport);
            let result = github
                .issue(&token, &owner, &repo, number)
                .and_then(|issue| {
                    let replies =
                        github.issue_comments(&token, &owner, &repo, number, 1, COMMENT_COUNT)?;
                    // The header extras are optional: a failure here must not cost the member the whole conversation.
                    let pull = (is_pull || issue.pull_request.is_some())
                        .then(|| github.pull(&token, &owner, &repo, number).ok())
                        .flatten()
                        .map(|pull| PullHeader::from_pull(&pull));
                    let now = now();
                    let mut comments = vec![CommentItem::from_issue(&issue, now)];
                    comments.extend(
                        replies
                            .iter()
                            .map(|reply| CommentItem::from_comment(reply, now)),
                    );
                    Ok(LoadedConversation {
                        title: issue.title,
                        state: issue.state,
                        comments,
                        pull,
                        has_more: replies.len() == COMMENT_COUNT as usize,
                    })
                });
            Message::ConversationLoaded { reference, result }
        });
    }

    /// Fetches one page (100 files) of the open pull request's changed files.
    fn load_files(&mut self, page: u32, context: &ComponentContext<Guit>) {
        let Some(conversation) = &self.conversation else {
            return;
        };
        let link = &conversation.link;
        let (token, owner, repo, number, reference) = (
            self.token.clone(),
            link.owner.clone(),
            link.repo.clone(),
            link.number,
            link.reference(),
        );
        _ = context.spawn_background(move |_| {
            let result = GitHub::new(&WinRtTransport)
                .pull_files(&token, &owner, &repo, number, page, pulls::FILES_PER_PAGE)
                .map(|files| (files.len(), diff::rows(&files)));
            Message::FilesLoaded {
                reference,
                page,
                result: result.map(|(count, mut rows)| {
                    // A full page means there may be more files: say so with a marker row the view turns into a button.
                    if count == pulls::FILES_PER_PAGE as usize {
                        rows.push(pulls::more_files_marker());
                    }
                    rows
                }),
            }
        });
    }

    fn load_pulls(&mut self, context: &ComponentContext<Guit>) {
        self.pulls.generation += 1;
        self.pulls.loading = true;
        self.pulls.loaded = true;
        self.pulls.error = None;
        let (generation, token, query) = (
            self.pulls.generation,
            self.token.clone(),
            self.pulls.scope.query(),
        );
        _ = context.spawn_background(move |_| {
            let result = GitHub::new(&WinRtTransport)
                .search_issues(&token, query, 1, Inbox::PAGE_SIZE)
                .map(|found| {
                    let now = now();
                    found
                        .items
                        .iter()
                        .map(|item| PullRequestItem::from_search(item, now))
                        .collect()
                });
            Message::PullsLoaded { generation, result }
        });
    }
}

/// How often an open run refreshes itself while it is queued or in progress.
const FOLLOW_PERIOD: Duration = Duration::from_secs(5);

/// After a re-run, keep checking for two minutes even though GitHub still reports the old attempt.
const TICKS_AFTER_RERUN: u32 = 24;

impl Shell {
    fn load_runs(&mut self, context: &ComponentContext<Guit>) {
        let Some((owner, repo)) = self.actions.repository() else {
            return;
        };
        self.actions.generation += 1;
        self.actions.loading = true;
        self.actions.error = None;
        let (generation, token) = (self.actions.generation, self.token.clone());
        _ = context.spawn_background(move |_| {
            let result = GitHub::new(&WinRtTransport)
                .workflow_runs(&token, &owner, &repo, 1, 30)
                .map(|runs| {
                    let now = now();
                    runs.iter().map(|run| RunItem::from_run(run, now)).collect()
                });
            Message::RunsLoaded { generation, result }
        });
    }

    /// Fetches the open run and its jobs. `quiet` is the background follow.
    fn load_run(&mut self, quiet: bool, context: &ComponentContext<Guit>) {
        let Some(run) = &mut self.actions.run else {
            return;
        };
        if !quiet {
            run.loading = true;
            run.error = None;
        }
        let (token, owner, repo, run_id) = (
            self.token.clone(),
            run.owner.clone(),
            run.repo.clone(),
            run.run_id,
        );
        _ = context.spawn_background(move |_| {
            let github = GitHub::new(&WinRtTransport);
            let result = github
                .workflow_run(&token, &owner, &repo, run_id)
                .and_then(|run| {
                    let jobs = github.jobs(&token, &owner, &repo, run_id)?;
                    Ok((RunItem::from_run(&run, now()), JobRow::rows(&jobs)))
                });
            Message::RunLoaded {
                run_id,
                quiet,
                result,
            }
        });
    }
}

impl Shell {
    /// What a shortcut means on the page being shown, if anything.
    fn shortcut_message(&self, shortcut: Shortcut) -> Option<Message> {
        let conversation = self.conversation.as_ref();
        let run = self
            .actions
            .run
            .as_ref()
            .filter(|_| self.section == Section::Actions && conversation.is_none());
        match shortcut {
            // Also the way back to a section's list after opening something from it.
            Shortcut::Section(index) => SECTION_TAGS
                .get(index)
                .map(|tag| Message::Navigate(Some((*tag).to_string()))),
            // The innermost page closes first. Esc only closes pages without a text box, so typing a reply is safe.
            Shortcut::Back | Shortcut::Escape => {
                if conversation.is_some_and(|open| open.files.is_some()) {
                    Some(Message::CloseFiles)
                } else if conversation.is_some() {
                    (shortcut == Shortcut::Back).then_some(Message::CloseConversation)
                } else if run.is_some_and(|run| run.log.is_some()) {
                    Some(Message::CloseLog)
                } else if run.is_some() {
                    Some(Message::CloseRun)
                } else {
                    None
                }
            }
            Shortcut::Refresh => {
                if conversation.is_some() {
                    None
                } else if run.is_some_and(|run| run.log.is_none()) {
                    Some(Message::ReloadRun)
                } else if run.is_some() {
                    None
                } else {
                    match self.section {
                        Section::Inbox => Some(Message::Reload),
                        Section::PullRequests => Some(Message::ReloadPulls),
                        Section::Actions => Some(Message::ReloadRuns),
                        Section::Settings => None,
                    }
                }
            }
        }
    }
}

impl Component for Guit {
    type Input = ();
    type Message = Message;

    fn create(_input: &(), context: &ComponentContext<Self>) -> Self {
        let mut app = Self {
            session: Session::SignedOut(SignIn::default()),
            settings: Settings::parse(platform::load_settings().as_deref()),
        };
        if let Some(token) = platform::load_token() {
            app.enter_shell(token, context);
        }
        app
    }

    fn update(&mut self, message: Message, context: &ComponentContext<Self>) {
        match (&mut self.session, message) {
            // ---- signed out: the device flow -------------------------------------------------
            (Session::SignedOut(sign_in), Message::SignIn) if !sign_in.busy => {
                sign_in.busy = true;
                sign_in.error = None;
                sign_in.task = Some(context.spawn_background(|_| {
                    Message::DeviceCodeReady(
                        GitHub::new(&WinRtTransport)
                            .request_device_code(GITHUB_CLIENT_ID, GITHUB_SCOPES)
                            .map_err(|error| error.describe()),
                    )
                }));
            }
            (Session::SignedOut(sign_in), Message::DeviceCodeReady(Ok(code))) => {
                platform::copy_to_clipboard(&code.user_code);
                platform::open_in_browser(&code.verification_uri);
                sign_in.user_code = Some(code.user_code.clone());
                sign_in.verification_uri.clone_from(&code.verification_uri);
                sign_in.task = Some(context.spawn_background(move |cancel| {
                    // Sleep in short slices so Cancel takes effect at once instead of after the poll interval.
                    let sleep = |wait: Duration| {
                        let mut left = wait;
                        while !left.is_zero() && !cancel.is_cancelled() {
                            let slice = left.min(Duration::from_millis(100));
                            std::thread::sleep(slice);
                            left -= slice;
                        }
                        !cancel.is_cancelled()
                    };
                    Message::SignInFinished(
                        wait_for_token(
                            &GitHub::new(&WinRtTransport),
                            GITHUB_CLIENT_ID,
                            &code,
                            sleep,
                        )
                        .map_err(|error| error.describe()),
                    )
                }));
            }
            (Session::SignedOut(sign_in), Message::DeviceCodeReady(Err(error))) => {
                *sign_in = SignIn {
                    error: Some(error),
                    ..SignIn::default()
                };
            }
            (
                Session::SignedOut(_),
                Message::SignInFinished(Ok(DeviceFlowOutcome::Authorized(token))),
            ) => {
                platform::save_token(&token);
                self.enter_shell(token, context);
            }
            (Session::SignedOut(sign_in), Message::SignInFinished(outcome)) => {
                let error = match outcome {
                    Ok(DeviceFlowOutcome::Denied) => {
                        Some("Sign-in was cancelled on github.com.".to_string())
                    }
                    Ok(DeviceFlowOutcome::Expired) => {
                        Some("The code expired. Try again.".to_string())
                    }
                    Err(error) => Some(error),
                    Ok(_) => None,
                };
                *sign_in = SignIn {
                    error,
                    ..SignIn::default()
                };
            }
            (Session::SignedOut(sign_in), Message::CancelSignIn) => {
                if let Some(task) = sign_in.task.take() {
                    task.cancel();
                }
                *sign_in = SignIn::default();
            }
            (Session::SignedOut(sign_in), Message::OpenVerificationPage) => {
                platform::open_in_browser(&sign_in.verification_uri);
            }

            // ---- signed in: the shell and the inbox ------------------------------------------
            (Session::SignedIn(_), Message::SignOut) => self.sign_out(),
            (Session::SignedIn(shell), Message::UserLoaded(Some(user))) => shell.login = user.login,
            (Session::SignedIn(shell), Message::FilterSelected(filter)) => {
                if let Some(request) = shell.inbox.set_filter(filter) {
                    shell.error = None;
                    fetch(context, &shell.token, request, Purpose::FirstPage);
                }
            }
            (Session::SignedIn(shell), Message::KindSelected(Some(index))) => {
                shell
                    .inbox
                    .set_kind(Kind::ALL.get(index).copied().unwrap_or_default());
                if let Some(request) = shell.inbox.top_up() {
                    fetch(context, &shell.token, request, Purpose::NextPage);
                }
            }
            (Session::SignedIn(shell), Message::Reload) => {
                shell.error = None;
                let request = shell.inbox.begin_load();
                fetch(context, &shell.token, request, Purpose::FirstPage);
            }
            (Session::SignedIn(shell), Message::LoadMore) => {
                if let Some(request) = shell.inbox.begin_load_more() {
                    fetch(context, &shell.token, request, Purpose::NextPage);
                }
            }
            (Session::SignedIn(shell), Message::PollTick) => {
                shell.poll = Some(context.set_timeout(POLL_PERIOD, Message::PollTick));
                if let Some(request) = shell.inbox.refresh_request() {
                    fetch(context, &shell.token, request, Purpose::Refresh);
                }
            }
            (
                Session::SignedIn(shell),
                Message::PageLoaded {
                    generation,
                    purpose,
                    result,
                },
            ) => match result {
                Ok(items) => {
                    let applied = match purpose {
                        Purpose::FirstPage => shell.inbox.first_page_loaded(generation, items),
                        Purpose::NextPage => shell.inbox.more_loaded(generation, items),
                        Purpose::Refresh => shell.inbox.refreshed(generation, items),
                    };
                    // With a kind filter on, keep fetching (a bounded number of pages) until the list is comfortably full.
                    if applied && let Some(request) = shell.inbox.top_up() {
                        fetch(context, &shell.token, request, Purpose::NextPage);
                    }
                }
                // A rejected token ends the session, as in the C# app.
                Err(error) if error.is_unauthorized() => self.sign_out(),
                // A background refresh that fails says nothing; the next one, or a reload, will report it.
                Err(_) if purpose == Purpose::Refresh => {}
                Err(error) => {
                    if shell.inbox.load_failed(generation) {
                        shell.error = Some(error.describe());
                    }
                }
            },
            (Session::SignedIn(shell), Message::Open(Some(index))) => {
                if let Some(item) = shell.inbox.visible().get(index).cloned() {
                    // Issues and pull requests open in the app; other kinds open the repository on github.com.
                    match item.link.clone() {
                        Some(link) => shell.open_conversation(link, context),
                        None => platform::open_in_browser(&item.web_url),
                    }
                    if item.unread {
                        shell.inbox.mark_read(&item.id);
                        let (token, id) = (shell.token.clone(), item.id.clone());
                        // Best effort: the next refresh shows the truth either way.
                        std::thread::spawn(move || {
                            _ = GitHub::new(&WinRtTransport).mark_thread_read(&token, &id)
                        });
                    }
                }
            }
            (Session::SignedIn(shell), Message::MarkDone(id)) => {
                // Optimistic: the row goes at once and comes back if GitHub refuses.
                if let Some(removed) = shell.inbox.remove(&id) {
                    shell.pending_done.push(removed);
                    let token = shell.token.clone();
                    _ = context.spawn_background(move |_| {
                        match GitHub::new(&WinRtTransport).mark_thread_done(&token, &id) {
                            Ok(()) => Message::MarkDoneFinished { id, error: None },
                            Err(error) => Message::MarkDoneFinished {
                                id,
                                error: Some(error.describe()),
                            },
                        }
                    });
                }
            }
            (Session::SignedIn(shell), Message::MarkDoneFinished { id, error }) => {
                if let Some(position) = shell
                    .pending_done
                    .iter()
                    .position(|removed| removed.id() == id)
                {
                    let removed = shell.pending_done.remove(position);
                    if let Some(error) = error {
                        shell.inbox.restore(removed);
                        shell.error = Some(error);
                    }
                }
            }

            // ---- the shell, pull requests and conversations -----------------------------------
            (Session::SignedIn(shell), Message::Navigate(tag)) => {
                shell.conversation = None;
                shell.actions.run = None;
                shell.section = match tag.as_deref() {
                    Some("pulls") => Section::PullRequests,
                    Some("actions") => Section::Actions,
                    Some("settings") => Section::Settings,
                    _ => Section::Inbox,
                };
                if shell.section == Section::PullRequests && !shell.pulls.loaded {
                    shell.load_pulls(context);
                }
                if shell.section == Section::Actions && !shell.actions.loaded {
                    shell.actions.loaded = true;
                    shell.actions.loading = true;
                    let token = shell.token.clone();
                    _ = context.spawn_background(move |_| {
                        Message::RepositoriesLoaded(
                            GitHub::new(&WinRtTransport).my_repositories(&token, 50),
                        )
                    });
                }
            }
            (Session::SignedIn(shell), Message::ScopeSelected(scope))
                if shell.pulls.scope != scope =>
            {
                shell.pulls.scope = scope;
                shell.load_pulls(context);
            }
            (Session::SignedIn(shell), Message::ReloadPulls) => shell.load_pulls(context),
            (Session::SignedIn(shell), Message::PullsLoaded { generation, result })
                if generation == shell.pulls.generation =>
            {
                shell.pulls.loading = false;
                match result {
                    Ok(items) => shell.pulls.items = items,
                    Err(error) if error.is_unauthorized() => self.sign_out(),
                    Err(error) => shell.pulls.error = Some(error.describe()),
                }
            }
            (Session::SignedIn(shell), Message::OpenPull(Some(index))) => {
                if let Some(item) = shell.pulls.items.get(index) {
                    match item.link.clone() {
                        Some(link) => shell.open_conversation(link, context),
                        None => platform::open_in_browser(&item.web_url),
                    }
                }
            }
            (Session::SignedIn(shell), Message::ConversationLoaded { reference, result }) => {
                // The reply is for this conversation only if the member has not moved on to another.
                if let Some(conversation) = shell
                    .conversation
                    .as_mut()
                    .filter(|open| open.link.reference() == reference)
                {
                    conversation.loading = false;
                    match result {
                        Ok(loaded) => {
                            conversation.title = loaded.title;
                            conversation.state = loaded.state;
                            conversation.comments = loaded.comments;
                            conversation.has_more = loaded.has_more;
                            if let Some(pull) = loaded.pull {
                                conversation.state.clone_from(&pull.state);
                                conversation.pull = Some(pull);
                            }
                        }
                        Err(error) if error.is_unauthorized() => self.sign_out(),
                        Err(error) => conversation.error = Some(error.describe()),
                    }
                }
            }
            (Session::SignedIn(shell), Message::CloseConversation) => shell.conversation = None,
            (Session::SignedIn(shell), Message::OpenConversationInBrowser) => {
                if let Some(conversation) = &shell.conversation {
                    platform::open_in_browser(&conversation.link.web_url());
                }
            }
            (Session::SignedIn(shell), Message::DraftChanged(draft)) => {
                if let Some(conversation) = &mut shell.conversation {
                    conversation.draft = draft;
                }
            }
            (Session::SignedIn(shell), Message::SendComment) => {
                if let Some(conversation) =
                    shell.conversation.as_mut().filter(|open| open.can_send())
                {
                    conversation.sending = true;
                    conversation.error = None;
                    let link = &conversation.link;
                    let (token, owner, repo, number, reference) = (
                        shell.token.clone(),
                        link.owner.clone(),
                        link.repo.clone(),
                        link.number,
                        link.reference(),
                    );
                    let body = conversation.draft.trim().to_string();
                    _ = context.spawn_background(move |_| {
                        let result = GitHub::new(&WinRtTransport)
                            .create_comment(&token, &owner, &repo, number, &body)
                            .map(|created| CommentItem::from_comment(&created, now()));
                        Message::CommentSent { reference, result }
                    });
                }
            }
            (Session::SignedIn(shell), Message::CommentSent { reference, result }) => {
                if let Some(conversation) = shell
                    .conversation
                    .as_mut()
                    .filter(|open| open.link.reference() == reference)
                {
                    conversation.sending = false;
                    match result {
                        // The draft is cleared only once GitHub has the comment.
                        Ok(comment) => {
                            conversation.comments.push(comment);
                            conversation.draft.clear();
                        }
                        Err(error) => conversation.error = Some(error.describe()),
                    }
                }
            }

            (Session::SignedIn(shell), Message::LoadMoreComments) => {
                if let Some(conversation) = shell
                    .conversation
                    .as_mut()
                    .filter(|open| open.has_more && !open.loading_more)
                {
                    conversation.loading_more = true;
                    let link = &conversation.link;
                    let (token, owner, repo, number, reference) = (
                        shell.token.clone(),
                        link.owner.clone(),
                        link.repo.clone(),
                        link.number,
                        link.reference(),
                    );
                    let page = conversation.page + 1;
                    _ = context.spawn_background(move |_| {
                        let result = GitHub::new(&WinRtTransport)
                            .issue_comments(&token, &owner, &repo, number, page, COMMENT_COUNT)
                            .map(|replies| {
                                let now = now();
                                replies
                                    .iter()
                                    .map(|reply| CommentItem::from_comment(reply, now))
                                    .collect()
                            });
                        Message::MoreCommentsLoaded { reference, result }
                    });
                }
            }
            (Session::SignedIn(shell), Message::MoreCommentsLoaded { reference, result }) => {
                if let Some(conversation) = shell
                    .conversation
                    .as_mut()
                    .filter(|open| open.link.reference() == reference)
                {
                    conversation.loading_more = false;
                    match result {
                        Ok(comments) => {
                            conversation.page += 1;
                            conversation.has_more = comments.len() == COMMENT_COUNT as usize;
                            conversation.comments.extend(comments);
                        }
                        Err(error) => conversation.error = Some(error.describe()),
                    }
                }
            }
            (
                Session::SignedIn(shell),
                Message::React {
                    comment_id,
                    content,
                },
            ) => {
                if let Some(conversation) = &mut shell.conversation
                    && let Some(comment) = conversation
                        .comments
                        .iter_mut()
                        .find(|comment| comment.id == comment_id)
                {
                    // Optimistic: the count goes up at once and comes back down if GitHub refuses.
                    comment.adjust_reaction(&content, 1);
                    let link = &conversation.link;
                    let (token, owner, repo, number, reference) = (
                        shell.token.clone(),
                        link.owner.clone(),
                        link.repo.clone(),
                        link.number,
                        link.reference(),
                    );
                    let is_opening_post = comment.is_opening_post;
                    _ = context.spawn_background(move |_| {
                        let github = GitHub::new(&WinRtTransport);
                        let result = if is_opening_post {
                            github.react_to_issue(&token, &owner, &repo, number, &content)
                        } else {
                            github.react_to_comment(&token, &owner, &repo, comment_id, &content)
                        };
                        Message::ReactFinished {
                            reference,
                            comment_id,
                            content,
                            error: result.err().map(|error| error.describe()),
                        }
                    });
                }
            }
            (
                Session::SignedIn(shell),
                Message::ReactFinished {
                    reference,
                    comment_id,
                    content,
                    error: Some(error),
                },
            ) => {
                if let Some(conversation) = shell
                    .conversation
                    .as_mut()
                    .filter(|open| open.link.reference() == reference)
                {
                    if let Some(comment) = conversation
                        .comments
                        .iter_mut()
                        .find(|comment| comment.id == comment_id)
                    {
                        comment.adjust_reaction(&content, -1);
                    }
                    conversation.error = Some(error);
                }
            }
            (Session::SignedIn(shell), Message::ShowFiles) => {
                if let Some(conversation) = &mut shell.conversation {
                    conversation.files = Some(Files::default());
                    shell.load_files(1, context);
                }
            }
            (Session::SignedIn(shell), Message::LoadMoreFiles) => {
                let next = shell
                    .conversation
                    .as_mut()
                    .and_then(|open| open.files.as_mut())
                    .filter(|files| !files.loading)
                    .map(|files| {
                        files.loading = true;
                        files.page + 1
                    });
                if let Some(page) = next {
                    shell.load_files(page, context);
                }
            }
            (
                Session::SignedIn(shell),
                Message::FilesLoaded {
                    reference,
                    page,
                    result,
                },
            ) => {
                if let Some(conversation) = shell
                    .conversation
                    .as_mut()
                    .filter(|open| open.link.reference() == reference)
                    && let Some(files) = &mut conversation.files
                {
                    files.loading = false;
                    match result {
                        Ok(rows) => {
                            let mut all = (*files.rows).clone();
                            // Drop the previous page's "more files" marker before appending.
                            if all.last().is_some_and(pulls::is_more_files_marker) {
                                all.pop();
                            }
                            all.extend(rows);
                            files.rows = Rc::new(all);
                            files.page = page;
                            files.revision += 1;
                        }
                        Err(error) => files.error = Some(error.describe()),
                    }
                }
            }
            (Session::SignedIn(shell), Message::CloseFiles) => {
                if let Some(conversation) = &mut shell.conversation {
                    conversation.files = None;
                }
            }

            // ---- settings and shortcuts --------------------------------------------------------
            (Session::SignedIn(_), Message::ThemeSelected(Some(index))) => {
                if let Some(theme) = Theme::ALL
                    .get(index)
                    .copied()
                    .filter(|theme| *theme != self.settings.theme())
                {
                    self.settings.set_theme(theme);
                    platform::save_settings(&self.settings.to_json());
                }
            }
            (Session::SignedIn(_), Message::OpenProject) => {
                platform::open_in_browser("https://github.com/leonio/linked")
            }
            (Session::SignedIn(shell), Message::Shortcut(shortcut)) => {
                if let Some(message) = shell.shortcut_message(shortcut) {
                    self.update(message, context);
                }
            }

            // ---- Actions ------------------------------------------------------------------------
            (Session::SignedIn(shell), Message::RepositoriesLoaded(result)) => match result {
                Ok(repositories) => {
                    shell.actions.repositories = repositories;
                    shell.actions.selected = (!shell.actions.repositories.is_empty()).then_some(0);
                    shell.actions.loading = false;
                    shell.load_runs(context);
                }
                Err(error) if error.is_unauthorized() => self.sign_out(),
                Err(error) => {
                    shell.actions.loading = false;
                    shell.actions.loaded = false;
                    shell.actions.error = Some(error.describe());
                }
            },
            (Session::SignedIn(shell), Message::RepositorySelected(Some(index)))
                if shell.actions.selected != Some(index) =>
            {
                shell.actions.selected = Some(index);
                shell.actions.runs.clear();
                shell.load_runs(context);
            }
            (Session::SignedIn(shell), Message::ReloadRuns) => shell.load_runs(context),
            (Session::SignedIn(shell), Message::RunsLoaded { generation, result })
                if generation == shell.actions.generation =>
            {
                shell.actions.loading = false;
                match result {
                    Ok(runs) => shell.actions.runs = runs,
                    Err(error) if error.is_unauthorized() => self.sign_out(),
                    Err(error) => shell.actions.error = Some(error.describe()),
                }
            }
            (Session::SignedIn(shell), Message::OpenRun(Some(index))) => {
                if let (Some(run), Some((owner, repo))) =
                    (shell.actions.runs.get(index), shell.actions.repository())
                {
                    shell.actions.run = Some(RunDetail::opening(owner, repo, run));
                    shell.load_run(false, context);
                }
            }
            (Session::SignedIn(shell), Message::ReloadRun) => shell.load_run(false, context),
            (Session::SignedIn(shell), Message::FollowRun) => {
                if let Some(run) = &mut shell.actions.run {
                    run.timer = None;
                    if run.should_follow() {
                        run.forced_ticks = run.forced_ticks.saturating_sub(1);
                        if run.forced_ticks == 0
                            && !run.status.is_some_and(|status| status.is_live())
                        {
                            run.notice = None;
                        }
                        shell.load_run(true, context);
                    }
                }
            }
            (
                Session::SignedIn(shell),
                Message::RunLoaded {
                    run_id,
                    quiet,
                    result,
                },
            ) => {
                if let Some(run) = shell
                    .actions
                    .run
                    .as_mut()
                    .filter(|open| open.run_id == run_id)
                {
                    if !quiet {
                        run.loading = false;
                    }
                    match result {
                        Ok((summary, rows)) => {
                            run.title = summary.title;
                            run.detail = summary.detail;
                            run.status = Some(summary.status);
                            if summary.status.is_live() {
                                run.notice = None;
                            }
                            // Replace the rows only when something a row shows has changed, so a quiet follow does not flicker.
                            if run.rows != rows {
                                run.rows = rows;
                            }
                        }
                        Err(error) if error.is_unauthorized() => {
                            self.sign_out();
                            return;
                        }
                        Err(error) if !quiet => run.error = Some(error.describe()),
                        Err(_) => {}
                    }
                    // Keep following while the run is live, or for a while after a re-run was requested.
                    if run.should_follow() {
                        run.timer = Some(context.set_timeout(FOLLOW_PERIOD, Message::FollowRun));
                    }
                }
            }
            (Session::SignedIn(shell), Message::Rerun) => {
                if let Some(run) = shell.actions.run.as_mut().filter(|open| !open.rerunning) {
                    run.rerunning = true;
                    run.error = None;
                    let (token, owner, repo, run_id) = (
                        shell.token.clone(),
                        run.owner.clone(),
                        run.repo.clone(),
                        run.run_id,
                    );
                    _ = context.spawn_background(move |_| Message::RerunFinished {
                        run_id,
                        error: GitHub::new(&WinRtTransport)
                            .rerun(&token, &owner, &repo, run_id)
                            .err()
                            .map(|error| error.describe()),
                    });
                }
            }
            (Session::SignedIn(shell), Message::RerunFinished { run_id, error }) => {
                if let Some(run) = shell
                    .actions
                    .run
                    .as_mut()
                    .filter(|open| open.run_id == run_id)
                {
                    run.rerunning = false;
                    match error {
                        Some(error) => run.error = Some(error),
                        None => {
                            // GitHub accepts the request before the run changes state, so say so and keep checking.
                            run.notice = Some(
                                "Re-run requested. Waiting for GitHub to start it\u{2026}"
                                    .to_string(),
                            );
                            run.forced_ticks = TICKS_AFTER_RERUN;
                            shell.load_run(false, context);
                        }
                    }
                }
            }
            (Session::SignedIn(shell), Message::CloseRun) => shell.actions.run = None,
            (Session::SignedIn(shell), Message::OpenRunInBrowser) => {
                if let Some(run) = &shell.actions.run {
                    platform::open_in_browser(&format!(
                        "https://github.com/{}/{}/actions/runs/{}",
                        run.owner, run.repo, run.run_id
                    ));
                }
            }
            (Session::SignedIn(shell), Message::OpenJob(Some(index))) => {
                if let Some(run) = &mut shell.actions.run
                    && let Some(row) = run.rows.get(index)
                {
                    let (token, owner, repo, job_id) = (
                        shell.token.clone(),
                        run.owner.clone(),
                        run.repo.clone(),
                        row.job_id,
                    );
                    run.log = Some(JobLog {
                        job_id,
                        name: row.job_name.clone(),
                        lines: Rc::new(Vec::new()),
                        loading: true,
                        error: None,
                        notice: None,
                    });
                    _ = context.spawn_background(move |_| Message::JobLogLoaded {
                        job_id,
                        result: GitHub::new(&WinRtTransport)
                            .job_log(&token, &owner, &repo, job_id)
                            .map(|raw| log_lines(&raw, actions::MAX_LOG_LINES)),
                    });
                }
            }
            (Session::SignedIn(shell), Message::JobLogLoaded { job_id, result }) => {
                if let Some(log) = shell
                    .actions
                    .run
                    .as_mut()
                    .and_then(|run| run.log.as_mut())
                    .filter(|log| log.job_id == job_id)
                {
                    log.loading = false;
                    match result {
                        Ok((lines, truncated)) => {
                            log.lines = Rc::new(lines);
                            log.notice = truncated.then(|| {
                                format!("Showing the last {} lines. Open the run on GitHub for the full log.", actions::MAX_LOG_LINES)
                            });
                        }
                        // GitHub only publishes a job's log once the job has finished.
                        Err(ApiError::Status { status: 404, .. }) => {
                            log.error = Some(
                                "No log yet. GitHub publishes a job's log when the job finishes."
                                    .to_string(),
                            );
                        }
                        Err(error) => log.error = Some(error.describe()),
                    }
                }
            }
            (Session::SignedIn(shell), Message::CloseLog) => {
                if let Some(run) = &mut shell.actions.run {
                    run.log = None;
                }
            }

            // A message for a state the app has already left (a late reply after signing out, say).
            _ => {}
        }
    }

    fn view(&self, _input: &(), context: &mut ViewContext<Self>) -> View {
        let theme = match self.settings.theme() {
            Theme::System => WindowTheme::System,
            Theme::Light => WindowTheme::Light,
            Theme::Dark => WindowTheme::Dark,
        };
        context.window_visuals(
            WindowVisuals::new()
                .backdrop(WindowBackdrop::Mica)
                .theme(theme),
        );
        let content = match &self.session {
            Session::SignedOut(sign_in) => sign_in_view(sign_in, context),
            Session::SignedIn(shell) => shell_view(shell, self.settings.theme(), context),
        };
        context.window_frame("Guit", content)
    }
}

fn sign_in_view(sign_in: &SignIn, context: &ViewContext<Guit>) -> View {
    let mut children: Vec<View> = vec![
        TextBlock::new()
            .text("Sign in to GitHub")
            .font_size(28.0)
            .horizontal_alignment(HorizontalAlignment::Center)
            .into(),
    ];

    match &sign_in.user_code {
        None => children.push(
            Button::new()
                .content("Sign in with GitHub")
                .is_enabled(!sign_in.busy)
                .horizontal_alignment(HorizontalAlignment::Center)
                .on_click(context.message(Message::SignIn))
                .into(),
        ),
        Some(code) => {
            children.push(
                TextBlock::new()
                    .text("Enter this code on github.com. It is already on your clipboard.")
                    .text_wrapping(TextWrapping::Wrap)
                    .horizontal_alignment(HorizontalAlignment::Center)
                    .into(),
            );
            children.push(
                TextBlock::new()
                    .text(code.as_str())
                    .font_size(40.0)
                    .horizontal_alignment(HorizontalAlignment::Center)
                    .into(),
            );
            children.push(ProgressBar::new().is_indeterminate(true).into());
            children.push(
                StackPanel::new()
                    .orientation(Orientation::Horizontal)
                    .spacing(8.0)
                    .horizontal_alignment(HorizontalAlignment::Center)
                    .children((
                        HyperlinkButton::new()
                            .content("Open github.com again")
                            .on_click(context.message(Message::OpenVerificationPage)),
                        Button::new()
                            .content("Cancel")
                            .on_click(context.message(Message::CancelSignIn)),
                    ))
                    .into(),
            );
        }
    }

    if let Some(error) = &sign_in.error {
        children.push(error_bar(error).into());
    }

    StackPanel::new()
        .spacing(16.0)
        .max_width(360.0)
        .horizontal_alignment(HorizontalAlignment::Center)
        .vertical_alignment(VerticalAlignment::Center)
        .children(children)
        .into()
}

fn shell_view(shell: &Shell, theme: Theme, context: &ViewContext<Guit>) -> View {
    let page = match (&shell.conversation, shell.section) {
        (Some(conversation), _) => pulls::conversation_page(conversation, context),
        (None, Section::Inbox) => inbox_page(shell, context),
        (None, Section::PullRequests) => pulls::pulls_page(&shell.pulls, context),
        (None, Section::Actions) => actions::page(&shell.actions, context),
        (None, Section::Settings) => settings_page::page(theme, &shell.login, context),
    };

    // Shortcuts are caught on the way down (preview), before any control sees the key. Only keys that mean a
    // shortcut on this page are marked handled; everything else reaches the focused control untouched.
    let on_shortcut = context.callback(Message::Shortcut);
    let handles: Vec<Shortcut> = [
        Shortcut::Section(0),
        Shortcut::Section(1),
        Shortcut::Section(2),
        Shortcut::Back,
        Shortcut::Escape,
        Shortcut::Refresh,
    ]
    .into_iter()
    .filter(|shortcut| shell.shortcut_message(*shortcut).is_some())
    .collect();
    let on_key = RoutedCallback::new(move |info: KeyEventInfo| {
        let shortcut = Shortcut::from_key(
            info.key.0,
            info.modifiers.contains(InputModifiers::CONTROL),
            info.modifiers.contains(InputModifiers::ALT),
        );
        match shortcut.filter(|shortcut| handles.contains(shortcut)) {
            Some(shortcut) => {
                on_shortcut.call(shortcut);
                true
            }
            None => false,
        }
    });

    let navigation = NavigationView::new()
        .pane_display_mode(NavigationViewPaneDisplayMode::LeftCompact)
        .is_back_button_visible(NavigationViewBackButtonVisible::Collapsed)
        .is_settings_visible(false)
        .menu_items((
            NavigationViewItem::new()
                .tag("inbox")
                .content("Inbox")
                .icon(Symbol::Mail)
                .is_selected(shell.section == Section::Inbox),
            NavigationViewItem::new()
                .tag("pulls")
                .content("Pull requests")
                .icon(Symbol::Switch)
                .is_selected(shell.section == Section::PullRequests),
            NavigationViewItem::new()
                .tag("actions")
                .content("Actions")
                .icon(Symbol::Play)
                .is_selected(shell.section == Section::Actions),
            NavigationViewItem::new()
                .tag("settings")
                .content("Settings")
                .icon(Symbol::Setting)
                .is_selected(shell.section == Section::Settings),
        ))
        .on_selected_tag_changed(
            context
                .callback(|tag: Option<Rc<str>>| Message::Navigate(tag.map(|tag| tag.to_string()))),
        )
        .content(page);

    Border::new()
        .on_preview_key_down(on_key)
        .content(navigation)
        .into()
}

fn inbox_page(shell: &Shell, context: &ViewContext<Guit>) -> View {
    let inbox = &shell.inbox;
    let filter = inbox.filter();
    let header = Grid::new()
        .columns([
            GridLength::Auto,
            GridLength::STAR,
            GridLength::Auto,
            GridLength::Auto,
            GridLength::Auto,
        ])
        .column_spacing(16.0)
        .margin(Thickness::new(24.0, 16.0, 24.0, 8.0))
        .children((
            TextBlock::new().text("Inbox").font_size(28.0),
            SelectorBar::new()
                .grid_column(1)
                .vertical_alignment(VerticalAlignment::Bottom)
                .items((
                    SelectorBarItem::new()
                        .text("Unread")
                        .is_selected(filter == Filter::Unread),
                    SelectorBarItem::new()
                        .text("All")
                        .is_selected(filter == Filter::All),
                    SelectorBarItem::new()
                        .text("Participating")
                        .is_selected(filter == Filter::Participating),
                ))
                .on_selected_text_changed(context.callback(|text: Option<Rc<str>>| {
                    Message::FilterSelected(match text.as_deref() {
                        Some("All") => Filter::All,
                        Some("Participating") => Filter::Participating,
                        _ => Filter::Unread,
                    })
                })),
            // The items are in the order of Kind::ALL, so the selected index is the kind.
            ComboBox::new()
                .grid_column(2)
                .vertical_alignment(VerticalAlignment::Center)
                .automation_name("Kind of notification")
                .items_source(Kind::ALL.map(Kind::label))
                .selected_index(Kind::ALL.iter().position(|kind| *kind == inbox.kind()))
                .on_selection_changed(context.callback(Message::KindSelected)),
            Button::new()
                .grid_column(3)
                .vertical_alignment(VerticalAlignment::Center)
                .automation_name("Refresh")
                .content(FontIcon::new().glyph("\u{E72C}").font_size(16.0))
                .on_click(context.message(Message::Reload)),
            Button::new()
                .grid_column(4)
                .vertical_alignment(VerticalAlignment::Center)
                .content(if shell.login.is_empty() {
                    "Sign out".to_string()
                } else {
                    format!("Sign out {}", shell.login)
                })
                .on_click(context.message(Message::SignOut)),
        ));

    let rows = inbox.visible();
    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::Single)
        .on_selection_changed(context.callback(Message::Open))
        .items(rows.iter().map(|item| {
            DataItem::new(item.id.as_str(), item.title.as_str()).content(row(item, context))
        }));

    let mut layers: Vec<View> = vec![list.into()];
    if inbox.is_loading() {
        layers.push(ProgressRing::new().is_active(true).into());
    } else if rows.is_empty() && shell.error.is_none() {
        layers.push(
            TextBlock::new()
                .text("You're all caught up")
                .font_size(20.0)
                .opacity(0.6)
                .horizontal_alignment(HorizontalAlignment::Center)
                .vertical_alignment(VerticalAlignment::Center)
                .into(),
        );
    }
    if let Some(error) = &shell.error {
        layers.push(
            error_bar(error)
                .vertical_alignment(VerticalAlignment::Top)
                .margin(Thickness::new(24.0, 0.0, 24.0, 0.0))
                .into(),
        );
    }

    // The C# app loads the next page as the list nears its end; reactor's ListView has no event for that yet,
    // so the next page is one click away instead.
    let footer: View = if inbox.is_loading_more() {
        ProgressBar::new().grid_row(2).is_indeterminate(true).into()
    } else if inbox.has_more() {
        Button::new()
            .grid_row(2)
            .margin(Thickness::new(24.0, 8.0, 24.0, 12.0))
            .horizontal_alignment(HorizontalAlignment::Center)
            .content("Load more")
            .on_click(context.message(Message::LoadMore))
            .into()
    } else {
        Grid::new().grid_row(2).into()
    };

    Grid::new()
        .rows([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .children((header, Grid::new().grid_row(1).children(layers), footer))
        .into()
}

/// One inbox row: an icon for the kind, the title over "repository · reason", the age, and "mark as done".
fn row(item: &NotificationItem, context: &ViewContext<Guit>) -> View {
    Grid::new()
        .columns([
            GridLength::Auto,
            GridLength::STAR,
            GridLength::Auto,
            GridLength::Auto,
        ])
        .column_spacing(12.0)
        .height(56.0)
        .opacity(if item.unread { 1.0 } else { 0.55 })
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
                        .text(format!("{} \u{00B7} {}", item.repository, item.reason))
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
            Button::new()
                .grid_column(3)
                .vertical_alignment(VerticalAlignment::Center)
                .automation_name("Mark as done")
                .content(FontIcon::new().glyph("\u{E73E}").font_size(14.0))
                .on_click(context.message(Message::MarkDone(item.id.clone()))),
        ))
        .into()
}

pub(crate) fn error_bar(message: &str) -> InfoBar {
    InfoBar::new()
        .is_open(true)
        .is_closable(false)
        .severity(InfoBarSeverity::Error)
        .message(message)
}

fn main() {
    platform::initialize();
    App::run_component::<Guit>(()).unwrap();
}
