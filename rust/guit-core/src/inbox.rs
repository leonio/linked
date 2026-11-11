//! One inbox row, with its display strings computed once so the view only copies text.

use std::rc::Rc;

use crate::api::NotificationThread;
use crate::link::SubjectLink;
use crate::time;

/// One notification as the list shows it. Mirrors the C# `NotificationItem`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NotificationItem {
    pub id: String,
    pub title: String,
    /// "owner/name".
    pub repository: String,
    /// The subject type: "Issue", "PullRequest", "Release", ...
    pub kind: String,
    pub reason: String,
    /// A Segoe Fluent Icons character for `kind`.
    pub glyph: &'static str,
    pub avatar_url: String,
    pub updated: String,
    pub unread: bool,
    /// Where clicking the row goes: the issue or pull request, or else the repository.
    pub web_url: String,
}

impl NotificationItem {
    /// `now` is seconds since the Unix epoch.
    pub fn from_thread(thread: &NotificationThread, now: i64) -> Self {
        let web_url = SubjectLink::parse(thread.subject.url.as_deref())
            .map_or_else(|| thread.repository.html_url.clone(), |link| link.web_url());
        Self {
            id: thread.id.clone(),
            title: thread.subject.title.clone(),
            repository: thread.repository.full_name.clone(),
            kind: thread.subject.kind.clone(),
            reason: thread.reason.replace('_', " "),
            glyph: glyph_for(&thread.subject.kind),
            avatar_url: small_avatar(&thread.repository.owner.avatar_url),
            updated: time::parse_timestamp(&thread.updated_at)
                .map_or_else(String::new, |then| time::relative(then, now)),
            unread: thread.unread,
            web_url,
        }
    }
}

/// Asks GitHub for a 64 px avatar instead of the full-size one.
pub fn small_avatar(url: &str) -> String {
    match url {
        "" => String::new(),
        url if url.contains('?') => format!("{url}&s=64"),
        url => format!("{url}?s=64"),
    }
}

fn glyph_for(kind: &str) -> &'static str {
    match kind {
        "PullRequest" => "\u{E8AB}",
        "Issue" => "\u{E9D5}",
        "Release" => "\u{E8EC}",
        "Discussion" => "\u{E8F2}",
        "CheckSuite" => "\u{E9D9}",
        _ => "\u{EA8F}",
    }
}

/// Which notifications to ask GitHub for.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum Filter {
    #[default]
    Unread,
    All,
    Participating,
}

/// Which kinds of notification to show. GitHub cannot filter by kind, so this is applied to what is loaded.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum Kind {
    #[default]
    All,
    PullRequests,
    Issues,
    Releases,
    Discussions,
    Checks,
    Other,
}

impl Kind {
    /// In the order the kind box lists them.
    pub const ALL: [Self; 7] = [
        Self::All,
        Self::PullRequests,
        Self::Issues,
        Self::Releases,
        Self::Discussions,
        Self::Checks,
        Self::Other,
    ];

    pub fn label(self) -> &'static str {
        match self {
            Self::All => "All kinds",
            Self::PullRequests => "Pull requests",
            Self::Issues => "Issues",
            Self::Releases => "Releases",
            Self::Discussions => "Discussions",
            Self::Checks => "Checks",
            Self::Other => "Other",
        }
    }

    fn matches(self, item: &NotificationItem) -> bool {
        const NAMED: [&str; 6] = [
            "PullRequest",
            "Issue",
            "Release",
            "Discussion",
            "CheckSuite",
            "WorkflowRun",
        ];
        match self {
            Self::All => true,
            Self::PullRequests => item.kind == "PullRequest",
            Self::Issues => item.kind == "Issue",
            Self::Releases => item.kind == "Release",
            Self::Discussions => item.kind == "Discussion",
            Self::Checks => item.kind == "CheckSuite" || item.kind == "WorkflowRun",
            Self::Other => !NAMED.contains(&item.kind.as_str()),
        }
    }
}

/// What to ask GitHub for, and which load it belongs to.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PageRequest {
    pub generation: u64,
    pub all: bool,
    pub participating: bool,
    pub page: u32,
    pub per_page: u32,
}

/// A row taken out of the list by "mark as done", kept so it can be put back if GitHub refuses.
#[derive(Debug, Clone)]
pub struct Removed {
    item: Rc<NotificationItem>,
    index: usize,
}

impl Removed {
    pub fn id(&self) -> &str {
        &self.item.id
    }
}

/// The inbox's state and rules, with no UI and no I/O: the app asks it what to fetch and tells it what arrived.
/// Mirrors the C# `InboxViewModel`: paging, a server-side filter, a client-side kind filter that tops itself up,
/// optimistic row actions, and a refresh that changes nothing when nothing changed.
#[derive(Debug, Default)]
pub struct Inbox {
    filter: Filter,
    kind: Kind,
    /// Everything loaded so far; `visible` is the part of it that matches `kind`.
    all: Vec<Rc<NotificationItem>>,
    visible: Rc<Vec<Rc<NotificationItem>>>,
    page: u32,
    has_more: bool,
    /// Bumped by every full load, so replies to a superseded load can be recognised and dropped.
    generation: u64,
    loading: bool,
    loading_more: bool,
    top_ups: u32,
    /// Bumped whenever `visible` changes, for views that cache by revision.
    revision: u64,
}

impl Inbox {
    pub const PAGE_SIZE: u32 = 50;

    /// Rows the list is meant to hold before the member has to scroll; a kind filter tops up to this.
    const COMFORTABLE_ROWS: usize = 15;
    const MAX_TOP_UP_PAGES: u32 = 4;

    pub fn filter(&self) -> Filter {
        self.filter
    }

    pub fn kind(&self) -> Kind {
        self.kind
    }

    /// The rows to show. Cheap to clone and compare.
    pub fn visible(&self) -> &Rc<Vec<Rc<NotificationItem>>> {
        &self.visible
    }

    pub fn revision(&self) -> u64 {
        self.revision
    }

    pub fn has_more(&self) -> bool {
        self.has_more
    }

    pub fn is_loading(&self) -> bool {
        self.loading
    }

    pub fn is_loading_more(&self) -> bool {
        self.loading_more
    }

    /// Starts a full load of the first page (also used when the filter changes).
    pub fn begin_load(&mut self) -> PageRequest {
        self.generation += 1;
        self.loading = true;
        self.loading_more = false;
        self.top_ups = 0;
        self.request(1)
    }

    /// Changes the server-side filter and starts the load it needs. `None` if it is already selected.
    pub fn set_filter(&mut self, filter: Filter) -> Option<PageRequest> {
        (self.filter != filter).then(|| {
            self.filter = filter;
            self.begin_load()
        })
    }

    /// The first page arrived. Returns false (and changes nothing) for a superseded load.
    pub fn first_page_loaded(&mut self, generation: u64, items: Vec<NotificationItem>) -> bool {
        if generation != self.generation {
            return false;
        }

        self.loading = false;
        self.page = 1;
        self.has_more = items.len() == Self::PAGE_SIZE as usize;
        self.all = items.into_iter().map(Rc::new).collect();
        self.rebuild();
        true
    }

    /// A load failed. Returns false for a superseded load, whose failure nobody should see.
    pub fn load_failed(&mut self, generation: u64) -> bool {
        if generation != self.generation {
            return false;
        }

        self.loading = false;
        self.loading_more = false;
        true
    }

    /// Starts loading the next page, unless there is none or a load is already running.
    pub fn begin_load_more(&mut self) -> Option<PageRequest> {
        if !self.has_more || self.loading || self.loading_more {
            return None;
        }

        self.loading_more = true;
        Some(self.request(self.page + 1))
    }

    /// The next page arrived. Returns false for a superseded load.
    pub fn more_loaded(&mut self, generation: u64, items: Vec<NotificationItem>) -> bool {
        if generation != self.generation {
            return false;
        }

        self.loading_more = false;
        self.page += 1;
        self.has_more = items.len() == Self::PAGE_SIZE as usize;
        self.all.extend(items.into_iter().map(Rc::new));
        self.rebuild();
        true
    }

    /// With a kind filter on, a page may hold few matching rows. Call after every arrival and kind change:
    /// it starts another page (a bounded number of times) while the list is short and there is more to fetch.
    pub fn top_up(&mut self) -> Option<PageRequest> {
        if self.visible.len() >= Self::COMFORTABLE_ROWS || self.top_ups >= Self::MAX_TOP_UP_PAGES {
            return None;
        }

        let request = self.begin_load_more()?;
        self.top_ups += 1;
        Some(request)
    }

    /// Shows only one kind. Filters what is loaded; follow with `top_up`.
    pub fn set_kind(&mut self, kind: Kind) {
        if self.kind != kind {
            self.kind = kind;
            self.top_ups = 0;
            self.rebuild();
        }
    }

    /// A background refresh fetched the first page again. Replaces the list only if something changed,
    /// so an unchanged inbox keeps its rows. Returns true if it changed.
    pub fn refreshed(&mut self, generation: u64, items: Vec<NotificationItem>) -> bool {
        if generation != self.generation
            || self.loading
            || self.loading_more
            || self.same_as_first_page(&items)
        {
            return false;
        }

        self.page = 1;
        self.has_more = items.len() == Self::PAGE_SIZE as usize;
        self.all = items.into_iter().map(Rc::new).collect();
        self.rebuild();
        true
    }

    /// The request a background refresh should send, or `None` while a load is running.
    pub fn refresh_request(&self) -> Option<PageRequest> {
        (!self.loading && !self.loading_more).then(|| self.request(1))
    }

    /// Marks a row as read at once. Under the Unread filter the row leaves the list.
    pub fn mark_read(&mut self, id: &str) {
        let Some(index) = self.all.iter().position(|item| item.id == id) else {
            return;
        };
        if !self.all[index].unread {
            return;
        }

        if self.filter == Filter::Unread {
            self.all.remove(index);
        } else {
            let mut item = (*self.all[index]).clone();
            item.unread = false;
            self.all[index] = Rc::new(item);
        }
        self.rebuild();
    }

    /// Takes a row out at once ("mark as done"). Keep the result to `restore` it if GitHub refuses.
    pub fn remove(&mut self, id: &str) -> Option<Removed> {
        let index = self.all.iter().position(|item| item.id == id)?;
        let item = self.all.remove(index);
        self.rebuild();
        Some(Removed { item, index })
    }

    /// Puts a removed row back where it was.
    pub fn restore(&mut self, removed: Removed) {
        let index = removed.index.min(self.all.len());
        self.all.insert(index, removed.item);
        self.rebuild();
    }

    fn request(&self, page: u32) -> PageRequest {
        PageRequest {
            generation: self.generation,
            all: self.filter == Filter::All,
            participating: self.filter == Filter::Participating,
            page,
            per_page: Self::PAGE_SIZE,
        }
    }

    fn rebuild(&mut self) {
        let kind = self.kind;
        self.visible = Rc::new(
            self.all
                .iter()
                .filter(|item| kind.matches(item))
                .cloned()
                .collect(),
        );
        self.revision += 1;
    }

    fn same_as_first_page(&self, page: &[NotificationItem]) -> bool {
        let page_size = Self::PAGE_SIZE as usize;
        if page.len() > self.all.len()
            || (self.all.len() <= page_size && self.all.len() != page.len())
        {
            return false;
        }

        page.iter()
            .zip(&self.all)
            .all(|(new, old)| new.id == old.id && new.unread == old.unread)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::api::{NotificationSubject, RepositorySummary, User};

    // 2026-06-15T12:00:00Z
    const NOW: i64 = 1_781_524_800;

    fn thread(kind: &str, url: Option<&str>) -> NotificationThread {
        NotificationThread {
            id: "42".into(),
            unread: true,
            reason: "review_requested".into(),
            updated_at: "2026-06-15T09:00:00Z".into(),
            subject: NotificationSubject {
                title: "Fix it".into(),
                kind: kind.into(),
                url: url.map(str::to_string),
            },
            repository: RepositorySummary {
                full_name: "octocat/hello".into(),
                html_url: "https://github.com/octocat/hello".into(),
                owner: User {
                    id: 1,
                    login: "octocat".into(),
                    avatar_url: "https://avatars.githubusercontent.com/u/1?v=4".into(),
                    html_url: "https://github.com/octocat".into(),
                },
            },
        }
    }

    #[test]
    fn every_display_value_is_mapped() {
        let item = NotificationItem::from_thread(
            &thread(
                "PullRequest",
                Some("https://api.github.com/repos/octocat/hello/pulls/7"),
            ),
            NOW,
        );

        assert_eq!(item.id, "42");
        assert_eq!(item.title, "Fix it");
        assert_eq!(item.repository, "octocat/hello");
        assert_eq!(item.reason, "review requested");
        assert_eq!(item.glyph, "\u{E8AB}");
        assert_eq!(
            item.avatar_url,
            "https://avatars.githubusercontent.com/u/1?v=4&s=64"
        );
        assert_eq!(item.updated, "3h");
        assert!(item.unread);
        assert_eq!(item.web_url, "https://github.com/octocat/hello/pull/7");
    }

    #[test]
    fn other_kinds_open_the_repository_and_get_the_fallback_glyph() {
        let item = NotificationItem::from_thread(&thread("Commit", None), NOW);

        assert_eq!(item.glyph, "\u{EA8F}");
        assert_eq!(item.web_url, "https://github.com/octocat/hello");
    }

    fn item(id: u32, kind: &str) -> NotificationItem {
        let mut thread = thread(kind, None);
        thread.id = id.to_string();
        NotificationItem::from_thread(&thread, NOW)
    }

    fn items(first: u32, count: u32, kind: &str) -> Vec<NotificationItem> {
        (first..first + count).map(|id| item(id, kind)).collect()
    }

    /// A page of 50 with `issues` issues at the end and pull requests before them.
    fn mixed(first: u32, issues: u32) -> Vec<NotificationItem> {
        let mut page = items(first, 50 - issues, "PullRequest");
        page.extend(items(first + 50 - issues, issues, "Issue"));
        page
    }

    fn ids(inbox: &Inbox) -> Vec<&str> {
        inbox
            .visible()
            .iter()
            .map(|item| item.id.as_str())
            .collect()
    }

    fn loaded(pages: Vec<NotificationItem>) -> Inbox {
        let mut inbox = Inbox::default();
        let request = inbox.begin_load();
        assert!(inbox.first_page_loaded(request.generation, pages));
        inbox
    }

    #[test]
    fn a_load_asks_for_the_first_page_of_the_current_filter() {
        let mut inbox = Inbox::default();

        let request = inbox.begin_load();
        assert_eq!(
            (
                request.all,
                request.participating,
                request.page,
                request.per_page
            ),
            (false, false, 1, 50)
        );
        assert!(inbox.is_loading());

        assert!(inbox.first_page_loaded(request.generation, items(1, 3, "Issue")));
        assert_eq!(ids(&inbox), ["1", "2", "3"]);
        assert!(!inbox.is_loading());
        assert!(!inbox.has_more());
    }

    #[test]
    fn each_filter_sends_its_query_and_selecting_the_current_one_does_nothing() {
        let mut inbox = Inbox::default();
        assert_eq!(inbox.set_filter(Filter::Unread), None);

        let all = inbox.set_filter(Filter::All).unwrap();
        assert_eq!((all.all, all.participating), (true, false));

        let participating = inbox.set_filter(Filter::Participating).unwrap();
        assert_eq!(
            (participating.all, participating.participating),
            (false, true)
        );
    }

    #[test]
    fn a_newer_load_discards_the_reply_of_an_older_one() {
        let mut inbox = Inbox::default();
        let stale = inbox.begin_load();
        let fresh = inbox.set_filter(Filter::All).unwrap();

        assert!(!inbox.first_page_loaded(stale.generation, items(1, 1, "Issue")));
        assert!(!inbox.load_failed(stale.generation));
        assert!(inbox.is_loading());

        assert!(inbox.first_page_loaded(fresh.generation, items(9, 1, "Issue")));
        assert_eq!(ids(&inbox), ["9"]);
    }

    #[test]
    fn paging_appends_and_stops_after_a_short_page() {
        let mut inbox = loaded(items(1, 50, "Issue"));
        assert!(inbox.has_more());

        let more = inbox.begin_load_more().unwrap();
        assert_eq!(more.page, 2);
        assert_eq!(inbox.begin_load_more(), None, "one page at a time");

        assert!(inbox.more_loaded(more.generation, items(51, 3, "Issue")));
        assert_eq!(inbox.visible().len(), 53);
        assert_eq!(inbox.begin_load_more(), None, "a short page was the last");
    }

    #[test]
    fn a_failed_page_can_be_asked_for_again() {
        let mut inbox = loaded(items(1, 50, "Issue"));
        let more = inbox.begin_load_more().unwrap();

        assert!(inbox.load_failed(more.generation));

        assert_eq!(inbox.begin_load_more().unwrap().page, 2);
        assert_eq!(inbox.visible().len(), 50);
    }

    #[test]
    fn a_kind_filter_shows_only_that_kind_of_what_is_loaded() {
        let mut page = items(1, 1, "PullRequest");
        page.extend(items(2, 1, "Issue"));
        page.extend(items(3, 1, "Release"));
        page.extend(items(4, 1, "Commit"));
        let mut inbox = loaded(page);

        inbox.set_kind(Kind::Issues);
        assert_eq!(ids(&inbox), ["2"]);
        inbox.set_kind(Kind::Other);
        assert_eq!(ids(&inbox), ["4"]);
        inbox.set_kind(Kind::All);
        assert_eq!(inbox.visible().len(), 4);
        assert_eq!(Kind::ALL.map(Kind::label)[1], "Pull requests");
    }

    #[test]
    fn a_kind_filter_tops_up_until_the_list_is_comfortably_full() {
        // 5 issues per page of 50: 15 issues need three pages.
        let mut inbox = loaded(mixed(1, 5));
        inbox.set_kind(Kind::Issues);

        let second = inbox.top_up().unwrap();
        assert!(inbox.more_loaded(second.generation, mixed(51, 5)));
        let third = inbox.top_up().unwrap();
        assert!(inbox.more_loaded(third.generation, mixed(101, 5)));

        assert_eq!(inbox.visible().len(), 15);
        assert_eq!(inbox.top_up(), None);
    }

    #[test]
    fn topping_up_gives_up_after_a_few_pages_of_nothing() {
        let mut inbox = loaded(items(1, 50, "Issue"));
        inbox.set_kind(Kind::Releases);

        let mut fetched = 0;
        while let Some(request) = inbox.top_up() {
            fetched += 1;
            assert!(inbox.more_loaded(request.generation, items(1000 * fetched, 50, "Issue")));
        }

        assert_eq!(fetched, 4);
        assert!(inbox.visible().is_empty());
    }

    #[test]
    fn mark_read_dims_the_row_or_removes_it_from_the_unread_view() {
        let mut unread_view = loaded(items(1, 2, "Issue"));
        unread_view.mark_read("1");
        assert_eq!(ids(&unread_view), ["2"]);

        let mut all_view = Inbox::default();
        let request = all_view.set_filter(Filter::All).unwrap();
        assert!(all_view.first_page_loaded(request.generation, items(1, 2, "Issue")));
        all_view.mark_read("1");
        assert_eq!(ids(&all_view), ["1", "2"]);
        assert!(!all_view.visible()[0].unread);
        assert!(all_view.visible()[1].unread);
    }

    #[test]
    fn a_removed_row_can_be_restored_to_its_place_even_under_a_kind_filter() {
        let mut page = items(1, 1, "PullRequest");
        page.extend(items(2, 1, "Issue"));
        page.extend(items(3, 1, "PullRequest"));
        let mut inbox = loaded(page);
        inbox.set_kind(Kind::Issues);

        let removed = inbox.remove("2").unwrap();
        assert_eq!(removed.id(), "2");
        assert!(inbox.visible().is_empty());
        assert!(inbox.remove("nope").is_none());

        inbox.restore(removed);
        inbox.set_kind(Kind::All);
        assert_eq!(ids(&inbox), ["1", "2", "3"]);
    }

    #[test]
    fn a_refresh_with_nothing_new_keeps_the_same_rows() {
        let mut inbox = loaded(items(1, 3, "Issue"));
        let before = Rc::clone(inbox.visible());
        let request = inbox.refresh_request().unwrap();

        assert!(!inbox.refreshed(request.generation, items(1, 3, "Issue")));

        assert!(Rc::ptr_eq(&before, inbox.visible()));
    }

    #[test]
    fn a_refresh_picks_up_new_rows_and_rows_read_elsewhere() {
        let mut inbox = loaded(items(1, 2, "Issue"));
        let request = inbox.refresh_request().unwrap();
        let mut fresh = items(9, 1, "Issue");
        fresh.extend(items(1, 2, "Issue"));
        assert!(inbox.refreshed(request.generation, fresh));
        assert_eq!(ids(&inbox), ["9", "1", "2"]);

        let mut read_elsewhere = items(9, 1, "Issue");
        read_elsewhere.extend(items(1, 2, "Issue"));
        read_elsewhere[1].unread = false;
        let request = inbox.refresh_request().unwrap();
        assert!(inbox.refreshed(request.generation, read_elsewhere));
        assert!(!inbox.visible()[1].unread);
    }

    #[test]
    fn no_refresh_while_a_load_is_running() {
        let mut inbox = loaded(items(1, 50, "Issue"));
        _ = inbox.begin_load_more();

        assert_eq!(inbox.refresh_request(), None);
    }

    #[test]
    fn small_avatar_appends_the_size_parameter() {
        assert_eq!(
            small_avatar("https://avatars/u/1"),
            "https://avatars/u/1?s=64"
        );
        assert_eq!(
            small_avatar("https://avatars/u/1?v=4"),
            "https://avatars/u/1?v=4&s=64"
        );
        assert_eq!(small_avatar(""), "");
    }
}
