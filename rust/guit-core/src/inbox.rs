//! One inbox row, with its display strings computed once so the view only copies text.

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
            updated: time::parse_timestamp(&thread.updated_at).map_or_else(String::new, |then| time::relative(then, now)),
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
            subject: NotificationSubject { title: "Fix it".into(), kind: kind.into(), url: url.map(str::to_string) },
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
        let item = NotificationItem::from_thread(&thread("PullRequest", Some("https://api.github.com/repos/octocat/hello/pulls/7")), NOW);

        assert_eq!(item.id, "42");
        assert_eq!(item.title, "Fix it");
        assert_eq!(item.repository, "octocat/hello");
        assert_eq!(item.reason, "review requested");
        assert_eq!(item.glyph, "\u{E8AB}");
        assert_eq!(item.avatar_url, "https://avatars.githubusercontent.com/u/1?v=4&s=64");
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

    #[test]
    fn small_avatar_appends_the_size_parameter() {
        assert_eq!(small_avatar("https://avatars/u/1"), "https://avatars/u/1?s=64");
        assert_eq!(small_avatar("https://avatars/u/1?v=4"), "https://avatars/u/1?v=4&s=64");
        assert_eq!(small_avatar(""), "");
    }
}
