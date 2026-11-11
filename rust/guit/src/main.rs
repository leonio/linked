//! Guit, the Rust build. One window: a sign-in view, then a shell with the inbox.
//!
//! This is the same app as `src/Guit.App` (C#, WinUI 3, Native AOT), written on windows-reactor so the
//! two can be measured side by side. Logic lives in `guit-core`; this crate is the UI and the
//! Windows platform layer.
#![windows_subsystem = "windows"]

mod platform;

use std::rc::Rc;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use guit_core::api::{ApiError, DeviceCode, GitHub, User};
use guit_core::device_flow::{DeviceFlowOutcome, wait_for_token};
use guit_core::inbox::NotificationItem;
use guit_core::{GITHUB_CLIENT_ID, GITHUB_SCOPES};
use platform::WinRtTransport;
use windows_reactor::*;

const PAGE_SIZE: u32 = 50;

/// Everything that can happen. Messages cross threads (background work answers with one), so they own their data.
#[derive(Clone)]
enum Message {
    SignIn,
    DeviceCodeReady(Result<DeviceCode, String>),
    SignInFinished(Result<DeviceFlowOutcome, String>),
    CancelSignIn,
    OpenVerificationPage,
    SignOut,
    UserLoaded(Option<User>),
    ShowUnread(bool),
    Refresh,
    InboxLoaded { generation: u64, result: Result<Vec<NotificationItem>, ApiError> },
    Open(usize),
}

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
    /// True shows only unread notifications; false shows all.
    unread_only: bool,
    items: Rc<Vec<Rc<NotificationItem>>>,
    loading: bool,
    error: Option<String>,
    /// Bumped by every load, so the reply of a superseded load is ignored.
    generation: u64,
}

enum Session {
    SignedOut(SignIn),
    SignedIn(Shell),
}

struct Guit {
    session: Session,
}

fn now() -> i64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map_or(0, |elapsed| elapsed.as_secs() as i64)
}

impl Guit {
    fn enter_shell(&mut self, token: String, context: &ComponentContext<Self>) {
        let user_token = token.clone();
        _ = context.spawn_background(move |_| Message::UserLoaded(GitHub::new(&WinRtTransport).user(&user_token).ok()));
        self.session = Session::SignedIn(Shell {
            token,
            login: String::new(),
            unread_only: true,
            items: Rc::new(Vec::new()),
            loading: false,
            error: None,
            generation: 0,
        });
        self.load_inbox(context);
    }

    fn load_inbox(&mut self, context: &ComponentContext<Self>) {
        let Session::SignedIn(shell) = &mut self.session else { return };
        shell.generation += 1;
        shell.loading = true;
        shell.error = None;
        let (generation, token, all) = (shell.generation, shell.token.clone(), !shell.unread_only);
        _ = context.spawn_background(move |_| {
            let result = GitHub::new(&WinRtTransport).notifications(&token, all, false, 1, PAGE_SIZE).map(|threads| {
                let now = now();
                threads.iter().map(|thread| NotificationItem::from_thread(thread, now)).collect()
            });
            Message::InboxLoaded { generation, result }
        });
    }

    fn sign_out(&mut self) {
        platform::clear_token();
        self.session = Session::SignedOut(SignIn::default());
    }
}

impl Component for Guit {
    type Input = ();
    type Message = Message;

    fn create(_input: &(), context: &ComponentContext<Self>) -> Self {
        let mut app = Self { session: Session::SignedOut(SignIn::default()) };
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
                        wait_for_token(&GitHub::new(&WinRtTransport), GITHUB_CLIENT_ID, &code, sleep)
                            .map_err(|error| error.describe()),
                    )
                }));
            }
            (Session::SignedOut(sign_in), Message::DeviceCodeReady(Err(error))) => {
                *sign_in = SignIn { error: Some(error), ..SignIn::default() };
            }
            (Session::SignedOut(_), Message::SignInFinished(Ok(DeviceFlowOutcome::Authorized(token)))) => {
                platform::save_token(&token);
                self.enter_shell(token, context);
            }
            (Session::SignedOut(sign_in), Message::SignInFinished(outcome)) => {
                let error = match outcome {
                    Ok(DeviceFlowOutcome::Denied) => Some("Sign-in was cancelled on github.com.".to_string()),
                    Ok(DeviceFlowOutcome::Expired) => Some("The code expired. Try again.".to_string()),
                    Err(error) => Some(error),
                    Ok(_) => None,
                };
                *sign_in = SignIn { error, ..SignIn::default() };
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
            (Session::SignedIn(shell), Message::ShowUnread(unread_only)) if shell.unread_only != unread_only => {
                shell.unread_only = unread_only;
                self.load_inbox(context);
            }
            (Session::SignedIn(_), Message::Refresh) => self.load_inbox(context),
            (Session::SignedIn(shell), Message::InboxLoaded { generation, result }) if generation == shell.generation => {
                shell.loading = false;
                match result {
                    Ok(items) => shell.items = Rc::new(items.into_iter().map(Rc::new).collect()),
                    // A rejected token ends the session, as in the C# app.
                    Err(error) if error.is_unauthorized() => self.sign_out(),
                    Err(error) => shell.error = Some(error.describe()),
                }
            }
            (Session::SignedIn(shell), Message::Open(index)) => {
                if let Some(item) = shell.items.get(index) {
                    platform::open_in_browser(&item.web_url);
                    if item.unread {
                        let (token, id) = (shell.token.clone(), item.id.clone());
                        // Best effort: the next refresh shows the truth either way.
                        std::thread::spawn(move || _ = GitHub::new(&WinRtTransport).mark_thread_read(&token, &id));
                    }
                }
            }

            // A message for a state the app has already left (a late reply after signing out, say).
            _ => {}
        }
    }

    fn view(&self, _input: &(), context: &mut ViewContext<Self>) -> View {
        context.window_visuals(WindowVisuals::new().backdrop(WindowBackdrop::Mica));
        let content = match &self.session {
            Session::SignedOut(sign_in) => sign_in_view(sign_in, context),
            Session::SignedIn(shell) => shell_view(shell, context),
        };
        context.window_frame("Guit", content)
    }
}

fn sign_in_view(sign_in: &SignIn, context: &ViewContext<Guit>) -> View {
    let mut children: Vec<View> = vec![
        TextBlock::new().text("Sign in to GitHub").font_size(28.0).horizontal_alignment(HorizontalAlignment::Center).into(),
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
                TextBlock::new().text(code.as_str()).font_size(40.0).horizontal_alignment(HorizontalAlignment::Center).into(),
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
                        Button::new().content("Cancel").on_click(context.message(Message::CancelSignIn)),
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

fn shell_view(shell: &Shell, context: &ViewContext<Guit>) -> View {
    let header = Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto, GridLength::Auto])
        .column_spacing(16.0)
        .margin(Thickness::new(24.0, 16.0, 24.0, 8.0))
        .children((
            TextBlock::new().text("Inbox").font_size(28.0),
            SelectorBar::new()
                .grid_column(1)
                .vertical_alignment(VerticalAlignment::Bottom)
                .items((
                    SelectorBarItem::new().text("Unread").is_selected(shell.unread_only),
                    SelectorBarItem::new().text("All").is_selected(!shell.unread_only),
                ))
                .on_selected_text_changed(context.callback(|text: Option<Rc<str>>| {
                    Message::ShowUnread(text.as_deref() != Some("All"))
                })),
            Button::new()
                .grid_column(2)
                .vertical_alignment(VerticalAlignment::Center)
                .content(FontIcon::new().glyph("\u{E72C}").font_size(16.0))
                .on_click(context.message(Message::Refresh)),
            Button::new()
                .grid_column(3)
                .vertical_alignment(VerticalAlignment::Center)
                .content(if shell.login.is_empty() { "Sign out".to_string() } else { format!("Sign out {}", shell.login) })
                .on_click(context.message(Message::SignOut)),
        ));

    let list = ListView::new()
        .selection_mode(ListViewSelectionMode::Single)
        .on_selection_changed(context.callback(|index: Option<usize>| index.map_or(Message::Refresh, Message::Open)))
        .items(shell.items.iter().map(|item| DataItem::new(item.id.as_str(), item.title.as_str()).content(row(item))));

    let mut layers: Vec<View> = vec![list.into()];
    if shell.loading {
        layers.push(ProgressRing::new().is_active(true).into());
    } else if let Some(error) = &shell.error {
        layers.push(error_bar(error).vertical_alignment(VerticalAlignment::Top).margin(Thickness::new(24.0, 0.0, 24.0, 0.0)).into());
    } else if shell.items.is_empty() {
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

    let page = Grid::new().rows([GridLength::Auto, GridLength::STAR]).children((header, Grid::new().grid_row(1).children(layers)));

    NavigationView::new()
        .pane_display_mode(NavigationViewPaneDisplayMode::LeftCompact)
        .is_back_button_visible(NavigationViewBackButtonVisible::Collapsed)
        .is_settings_visible(false)
        .menu_items((NavigationViewItem::new().tag("inbox").content("Inbox").icon(Symbol::Mail).is_selected(true),))
        .content(page)
        .into()
}

/// One inbox row: an icon for the kind, the title over "repository · reason", and the age.
fn row(item: &NotificationItem) -> View {
    Grid::new()
        .columns([GridLength::Auto, GridLength::STAR, GridLength::Auto])
        .column_spacing(12.0)
        .height(56.0)
        .opacity(if item.unread { 1.0 } else { 0.55 })
        .children((
            FontIcon::new().glyph(item.glyph).font_size(16.0).vertical_alignment(VerticalAlignment::Center),
            StackPanel::new().grid_column(1).vertical_alignment(VerticalAlignment::Center).children((
                TextBlock::new().text(item.title.as_str()).font_weight(FontWeight::SEMI_BOLD).text_trimming(TextTrimming::CharacterEllipsis),
                TextBlock::new()
                    .text(format!("{} \u{00B7} {}", item.repository, item.reason))
                    .font_size(12.0)
                    .opacity(0.7)
                    .text_trimming(TextTrimming::CharacterEllipsis),
            )),
            TextBlock::new().text(item.updated.as_str()).grid_column(2).font_size(12.0).opacity(0.7).vertical_alignment(VerticalAlignment::Center),
        ))
        .into()
}

fn error_bar(message: &str) -> InfoBar {
    InfoBar::new().is_open(true).is_closable(false).severity(InfoBarSeverity::Error).message(message)
}

fn main() {
    platform::initialize();
    App::run_component::<Guit>(()).unwrap();
}
