//! Rows for the pull requests page and the conversation page. Mirrors the C# `PullRequestItem` and `CommentItem`.

use crate::api::{Issue, IssueComment, IssueSearchItem, PullRequest, ReactionRollup, User};
use crate::inbox::small_avatar;
use crate::link::SubjectLink;
use crate::rich::{self, Block};
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
    /// The post as plain text.
    pub body: String,
    /// The post with its formatting, parsed once here so the view only walks ready-made blocks.
    pub content: Vec<Block>,
    pub posted: String,
    /// "member", "owner", "contributor"... or `None` when the author has no special relation to the repository.
    pub association: Option<String>,
    /// Counts in the order of `REACTIONS`.
    pub reactions: [u32; 8],
}

/// The reactions GitHub accepts, in the order GitHub shows them: the API name and the emoji.
pub const REACTIONS: [(&str, &str); 8] = [
    ("+1", "\u{1F44D}"),
    ("-1", "\u{1F44E}"),
    ("laugh", "\u{1F604}"),
    ("hooray", "\u{1F389}"),
    ("confused", "\u{1F615}"),
    ("heart", "\u{2764}\u{FE0F}"),
    ("rocket", "\u{1F680}"),
    ("eyes", "\u{1F440}"),
];

fn counts(rollup: Option<&ReactionRollup>) -> [u32; 8] {
    rollup.map_or([0; 8], |r| {
        [
            r.plus_one,
            r.minus_one,
            r.laugh,
            r.hooray,
            r.confused,
            r.heart,
            r.rocket,
            r.eyes,
        ]
    })
}

/// What the header of a pull request's conversation adds to an issue's.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct PullHeader {
    /// "open", "closed", "merged" or "draft".
    pub state: String,
    /// "feature → main"; across forks the source is "owner:branch".
    pub branches: String,
    /// "+120 −8 in 5 files".
    pub diff_summary: String,
}

impl PullHeader {
    pub fn from_pull(pull: &PullRequest) -> Self {
        let state = if pull.merged {
            "merged"
        } else if pull.state == "closed" {
            "closed"
        } else if pull.draft == Some(true) {
            "draft"
        } else {
            "open"
        };
        let owner = |label: &str| {
            label
                .split_once(':')
                .map_or(String::new(), |(owner, _)| owner.to_string())
        };
        let source = if owner(&pull.head.label) == owner(&pull.base.label) {
            &pull.head.name
        } else {
            &pull.head.label
        };
        Self {
            state: state.to_string(),
            branches: format!("{source} \u{2192} {}", pull.base.name),
            diff_summary: format!(
                "+{} \u{2212}{} in {} {}",
                pull.additions,
                pull.deletions,
                pull.changed_files,
                if pull.changed_files == 1 {
                    "file"
                } else {
                    "files"
                }
            ),
        }
    }
}

impl CommentItem {
    /// The non-zero reaction counts as one line, for example "👍 3  ❤️ 1". Empty when there are none.
    pub fn reaction_summary(&self) -> String {
        REACTIONS
            .iter()
            .zip(self.reactions)
            .filter(|(_, count)| *count > 0)
            .map(|((_, emoji), count)| format!("{emoji} {count}"))
            .collect::<Vec<_>>()
            .join("  ")
    }

    /// Changes one reaction count by `delta`. Unknown names are ignored.
    pub fn adjust_reaction(&mut self, content: &str, delta: i32) {
        if let Some(index) = REACTIONS.iter().position(|(name, _)| *name == content) {
            self.reactions[index] = self.reactions[index].saturating_add_signed(delta);
        }
    }

    pub fn from_issue(issue: &Issue, now: i64) -> Self {
        Self {
            id: issue.id,
            is_opening_post: true,
            author: author(issue.user.as_ref()),
            avatar_url: avatar(issue.user.as_ref()),
            body: issue.body_text.clone().unwrap_or_default(),
            content: content(issue.body_html.as_deref(), issue.body_text.as_deref()),
            posted: age(&issue.created_at, now),
            association: None,
            reactions: counts(issue.reactions.as_ref()),
        }
    }

    pub fn from_comment(comment: &IssueComment, now: i64) -> Self {
        Self {
            id: comment.id,
            is_opening_post: false,
            author: author(comment.user.as_ref()),
            avatar_url: avatar(comment.user.as_ref()),
            body: comment.body_text.clone().unwrap_or_default(),
            content: content(comment.body_html.as_deref(), comment.body_text.as_deref()),
            posted: age(&comment.created_at, now),
            association: comment
                .author_association
                .as_deref()
                .filter(|association| *association != "NONE")
                .map(|association| association.to_lowercase().replace('_', " ")),
            reactions: counts(comment.reactions.as_ref()),
        }
    }
}

fn content(html: Option<&str>, text: Option<&str>) -> Vec<Block> {
    match html.filter(|html| !html.is_empty()) {
        Some(html) => rich::parse_html(html),
        None => rich::from_plain_text(text.unwrap_or_default()),
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
            body_html: Some("<p>Please <strong>review</strong></p>".into()),
            pull_request: None,
            reactions: Some(ReactionRollup {
                plus_one: 3,
                heart: 1,
                ..ReactionRollup::default()
            }),
        };
        let comment = IssueComment {
            id: 11,
            created_at: "2026-06-15T09:00:00Z".into(),
            user: None,
            body_text: None,
            body_html: None,
            author_association: Some("FIRST_TIME_CONTRIBUTOR".into()),
            reactions: None,
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
        assert!(opening.content[0].inlines[1].bold);
        assert_eq!(opening.content[0].inlines[1].text, "review");
        assert!(reply.content.is_empty());
        assert_eq!(
            opening.reaction_summary(),
            "\u{1F44D} 3  \u{2764}\u{FE0F} 1"
        );
        assert_eq!(reply.reaction_summary(), "");

        let mut reacted = reply.clone();
        reacted.adjust_reaction("rocket", 1);
        reacted.adjust_reaction("nonsense", 1);
        assert_eq!(reacted.reaction_summary(), "\u{1F680} 1");
        reacted.adjust_reaction("rocket", -1);
        reacted.adjust_reaction("rocket", -1);
        assert_eq!(reacted.reaction_summary(), "");
        assert_eq!((reply.author.as_str(), reply.body.as_str()), ("ghost", ""));
        assert_eq!(reply.association.as_deref(), Some("first time contributor"));

        let plain = IssueComment {
            author_association: Some("NONE".into()),
            ..comment
        };
        assert_eq!(CommentItem::from_comment(&plain, NOW).association, None);
    }

    #[test]
    fn a_pull_request_header_describes_state_branches_and_size() {
        use crate::api::PullBranch;
        let pull =
            |state: &str, merged: bool, draft: bool, head_label: &str, files: u32| PullRequest {
                state: state.into(),
                merged,
                draft: Some(draft),
                additions: 120,
                deletions: 8,
                changed_files: files,
                head: PullBranch {
                    name: "feature".into(),
                    label: head_label.into(),
                },
                base: PullBranch {
                    name: "main".into(),
                    label: "octocat:main".into(),
                },
            };

        let open = PullHeader::from_pull(&pull("open", false, false, "octocat:feature", 5));
        assert_eq!(open.state, "open");
        assert_eq!(open.branches, "feature \u{2192} main");
        assert_eq!(open.diff_summary, "+120 \u{2212}8 in 5 files");

        assert_eq!(
            PullHeader::from_pull(&pull("open", false, true, "octocat:feature", 1)).state,
            "draft"
        );
        assert_eq!(
            PullHeader::from_pull(&pull("closed", false, true, "octocat:feature", 1)).state,
            "closed"
        );
        assert_eq!(
            PullHeader::from_pull(&pull("closed", true, false, "octocat:feature", 1)).state,
            "merged"
        );
        assert_eq!(
            PullHeader::from_pull(&pull("open", false, false, "contributor:feature", 1)).branches,
            "contributor:feature \u{2192} main"
        );
        assert_eq!(
            PullHeader::from_pull(&pull("open", false, false, "octocat:feature", 1)).diff_summary,
            "+120 \u{2212}8 in 1 file"
        );
    }
}
