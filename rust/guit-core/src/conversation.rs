//! Rows for the pull requests page and the conversation page. Mirrors the C# `PullRequestItem` and `CommentItem`.

use crate::api::{Issue, IssueComment, IssueSearchItem, User};
use crate::inbox::small_avatar;
use crate::link::SubjectLink;
use crate::time;

/// Which of the member's open pull requests to list.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum PullRequestScope {
    #[default]
    ReviewRequested,
    Created,
    Assigned,
}

impl PullRequestScope {
    pub const ALL: [Self; 3] = [Self::ReviewRequested, Self::Created, Self::Assigned];

    pub fn label(self) -> &'static str {
        match self {
            Self::ReviewRequested => "Review requested",
            Self::Created => "Created",
            Self::Assigned => "Assigned",
        }
    }

    pub fn from_label(label: Option<&str>) -> Self {
        Self::ALL
            .into_iter()
            .find(|scope| Some(scope.label()) == label)
            .unwrap_or_default()
    }

    /// The GitHub search query for this scope. Same queries as the C# app.
    pub fn query(self) -> &'static str {
        match self {
            Self::ReviewRequested => "is:open is:pr review-requested:@me archived:false",
            Self::Created => "is:open is:pr author:@me archived:false",
            Self::Assigned => "is:open is:pr assignee:@me archived:false",
        }
    }
}

/// One row of the pull requests page.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PullRequestItem {
    pub title: String,
    /// "owner/repo #12".
    pub reference: String,
    pub author: String,
    pub updated: String,
    pub is_draft: bool,
    pub glyph: &'static str,
    /// Where the row goes; `None` if the repository URL could not be understood.
    pub link: Option<SubjectLink>,
    pub web_url: String,
}

impl PullRequestItem {
    /// `now` is seconds since the Unix epoch.
    pub fn from_search(item: &IssueSearchItem, now: i64) -> Self {
        let link = SubjectLink::from_repository_url(&item.repository_url, item.number, true);
        let is_draft = item.draft == Some(true);
        Self {
            title: item.title.clone(),
            reference: link
                .as_ref()
                .map_or_else(|| format!("#{}", item.number), SubjectLink::reference),
            author: author(item.user.as_ref()),
            updated: age(&item.updated_at, now),
            is_draft,
            glyph: if is_draft { "\u{E70F}" } else { "\u{E8AB}" },
            link,
            web_url: item.html_url.clone(),
        }
    }
}

/// One post in a conversation: the opening post of the issue, or a comment.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CommentItem {
    /// The comment id, or the issue id for the opening post.
    pub id: u64,
    pub is_opening_post: bool,
    pub author: String,
    pub avatar_url: String,
    pub body: String,
    pub posted: String,
    /// "member", "owner", "contributor"... or `None` when the author has no special relation to the repository.
    pub association: Option<String>,
}

impl CommentItem {
    pub fn from_issue(issue: &Issue, now: i64) -> Self {
        Self {
            id: issue.id,
            is_opening_post: true,
            author: author(issue.user.as_ref()),
            avatar_url: avatar(issue.user.as_ref()),
            body: issue.body_text.clone().unwrap_or_default(),
            posted: age(&issue.created_at, now),
            association: None,
        }
    }

    pub fn from_comment(comment: &IssueComment, now: i64) -> Self {
        Self {
            id: comment.id,
            is_opening_post: false,
            author: author(comment.user.as_ref()),
            avatar_url: avatar(comment.user.as_ref()),
            body: comment.body_text.clone().unwrap_or_default(),
            posted: age(&comment.created_at, now),
            association: comment
                .author_association
                .as_deref()
                .filter(|association| *association != "NONE")
                .map(|association| association.to_lowercase().replace('_', " ")),
        }
    }
}

// A deleted account comes back as null; GitHub shows those posts as written by "ghost".
fn author(user: Option<&User>) -> String {
    user.map_or_else(|| "ghost".to_string(), |user| user.login.clone())
}

fn avatar(user: Option<&User>) -> String {
    user.map_or_else(String::new, |user| small_avatar(&user.avatar_url))
}

fn age(timestamp: &str, now: i64) -> String {
    time::parse_timestamp(timestamp).map_or_else(String::new, |then| time::relative(then, now))
}

#[cfg(test)]
mod tests {
    use super::*;

    // 2026-06-15T12:00:00Z
    const NOW: i64 = 1_781_524_800;

    fn octocat() -> User {
        User {
            id: 1,
            login: "octocat".into(),
            avatar_url: "https://avatars/1".into(),
            html_url: "https://github.com/octocat".into(),
        }
    }

    #[test]
    fn a_search_result_becomes_a_pull_request_row() {
        let found = IssueSearchItem {
            number: 7,
            title: "Fix it".into(),
            updated_at: "2026-06-15T09:00:00Z".into(),
            html_url: "https://github.com/octocat/hello/pull/7".into(),
            repository_url: "https://api.github.com/repos/octocat/hello".into(),
            user: None,
            draft: Some(true),
        };

        let item = PullRequestItem::from_search(&found, NOW);

        assert_eq!(item.reference, "octocat/hello #7");
        assert_eq!(item.author, "ghost");
        assert_eq!(item.updated, "3h");
        assert!(item.is_draft);
        assert_eq!(
            item.link.unwrap().web_url(),
            "https://github.com/octocat/hello/pull/7"
        );
    }

    #[test]
    fn scopes_have_labels_and_queries() {
        assert_eq!(
            PullRequestScope::from_label(Some("Created")),
            PullRequestScope::Created
        );
        assert_eq!(
            PullRequestScope::from_label(None),
            PullRequestScope::ReviewRequested
        );
        assert_eq!(
            PullRequestScope::Assigned.query(),
            "is:open is:pr assignee:@me archived:false"
        );
    }

    #[test]
    fn the_opening_post_and_comments_become_rows() {
        let issue = Issue {
            id: 70,
            number: 7,
            title: "Fix it".into(),
            state: "open".into(),
            created_at: "2026-06-13T12:00:00Z".into(),
            user: Some(octocat()),
            body_text: Some("Please review".into()),
            pull_request: None,
        };
        let comment = IssueComment {
            id: 11,
            created_at: "2026-06-15T09:00:00Z".into(),
            user: None,
            body_text: None,
            author_association: Some("FIRST_TIME_CONTRIBUTOR".into()),
        };

        let opening = CommentItem::from_issue(&issue, NOW);
        let reply = CommentItem::from_comment(&comment, NOW);

        assert!(opening.is_opening_post);
        assert_eq!(
            (
                opening.author.as_str(),
                opening.body.as_str(),
                opening.posted.as_str()
            ),
            ("octocat", "Please review", "2d")
        );
        assert_eq!(opening.avatar_url, "https://avatars/1?s=64");
        assert_eq!((reply.author.as_str(), reply.body.as_str()), ("ghost", ""));
        assert_eq!(reply.association.as_deref(), Some("first time contributor"));

        let plain = IssueComment {
            author_association: Some("NONE".into()),
            ..comment
        };
        assert_eq!(CommentItem::from_comment(&plain, NOW).association, None);
    }
}
