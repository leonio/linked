//! The GitHub endpoints the app uses, over any [`Transport`].
//!
//! Only the fields the app shows are declared on each type; serde skips the rest of GitHub's
//! (large) objects, which keeps parsing cheap. Field names are GitHub's own snake_case.

use serde::Deserialize;
use serde::de::DeserializeOwned;

use crate::http::{Request, Response, Transport, encode};

const API: &str = "https://api.github.com";
const WEB: &str = "https://github.com";

/// The REST API version these types were written against (same as the C# client).
pub const API_VERSION: &str = "2026-03-10";

/// Why a call failed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ApiError {
    /// No reply arrived.
    Transport(String),
    /// GitHub answered with an error status.
    Status { status: u16, rate_limited: bool },
    /// The reply was not the JSON we expected.
    Decode(String),
}

impl ApiError {
    /// A sentence for the member. The wording matches the C# client's `ApiErrors.Describe`.
    pub fn describe(&self) -> String {
        match self {
            Self::Transport(_) => "Could not reach GitHub. Check your connection.".into(),
            Self::Status { status: 401, .. } => {
                "Your GitHub session has expired. Sign in again.".into()
            }
            Self::Status {
                status: 403 | 429,
                rate_limited: true,
            } => "GitHub rate limit reached. Try again in a few minutes.".into(),
            Self::Status { status: 403, .. } => {
                "GitHub refused this request. The app may need more permissions.".into()
            }
            Self::Status { status: 404, .. } => {
                "Not found. It may have been deleted, or you may not have access.".into()
            }
            Self::Status { status: 500.., .. } => {
                "GitHub is having problems. Try again shortly.".into()
            }
            Self::Status { status, .. } => format!("GitHub returned an error ({status})."),
            Self::Decode(_) => "GitHub sent a reply this app does not understand.".into(),
        }
    }

    pub fn is_unauthorized(&self) -> bool {
        matches!(self, Self::Status { status: 401, .. })
    }
}

/// Reply of `POST /login/device/code`.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct DeviceCode {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub expires_in: u64,
    pub interval: u64,
}

/// Reply of `POST /login/oauth/access_token` while polling: GitHub answers 200 either way,
/// with a token or with an error code.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize, Default)]
pub struct DeviceTokenResponse {
    pub access_token: Option<String>,
    pub error: Option<String>,
    pub error_description: Option<String>,
    pub interval: Option<u64>,
}

/// Schema: private-user / simple-user.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct User {
    pub id: u64,
    pub login: String,
    pub avatar_url: String,
    pub html_url: String,
}

/// Schema: thread.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct NotificationThread {
    pub id: String,
    pub unread: bool,
    pub reason: String,
    pub updated_at: String,
    pub subject: NotificationSubject,
    pub repository: RepositorySummary,
}

/// Schema: thread.subject.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct NotificationSubject {
    pub title: String,
    #[serde(rename = "type")]
    pub kind: String,
    pub url: Option<String>,
}

/// Schema: minimal-repository.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct RepositorySummary {
    pub full_name: String,
    pub html_url: String,
    pub owner: User,
}

/// Reply of `GET /search/issues`.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct IssueSearchResult {
    pub total_count: u64,
    pub items: Vec<IssueSearchItem>,
}

/// Schema: issue-search-result-item.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct IssueSearchItem {
    pub number: u32,
    pub title: String,
    pub updated_at: String,
    pub html_url: String,
    /// API URL of the repository: `https://api.github.com/repos/{owner}/{repo}`.
    pub repository_url: String,
    /// Null when the author's account was deleted.
    pub user: Option<User>,
    pub draft: Option<bool>,
}

/// Schema: issue. Pull requests are issues too, so this serves both.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct Issue {
    pub id: u64,
    pub number: u32,
    pub title: String,
    pub state: String,
    pub created_at: String,
    pub user: Option<User>,
    /// The body as plain text. Present because requests ask for the "full" media type.
    pub body_text: Option<String>,
    /// Present (whatever its content) when this issue is a pull request.
    pub pull_request: Option<serde_json::Value>,
}

/// Schema: issue-comment.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct IssueComment {
    pub id: u64,
    pub created_at: String,
    pub user: Option<User>,
    pub body_text: Option<String>,
    pub author_association: Option<String>,
}

/// Reply of `GET /repos/{owner}/{repo}/actions/runs`.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct WorkflowRunList {
    pub workflow_runs: Vec<WorkflowRun>,
}

/// Schema: workflow-run.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct WorkflowRun {
    pub id: u64,
    pub display_title: String,
    pub run_number: u64,
    pub event: String,
    pub updated_at: String,
    pub name: Option<String>,
    /// "queued", "in_progress", "completed"...
    pub status: Option<String>,
    /// Null until completed.
    pub conclusion: Option<String>,
    pub head_branch: Option<String>,
    pub actor: Option<User>,
}

/// Reply of `GET /repos/{owner}/{repo}/actions/runs/{run_id}/jobs`.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct JobList {
    pub jobs: Vec<Job>,
}

/// Schema: job.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct Job {
    pub id: u64,
    pub name: String,
    pub status: String,
    pub conclusion: Option<String>,
    pub started_at: String,
    pub completed_at: Option<String>,
    #[serde(default)]
    pub steps: Vec<JobStep>,
}

/// Schema: job.steps[].
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct JobStep {
    pub name: String,
    pub status: String,
    pub conclusion: Option<String>,
    pub started_at: Option<String>,
    pub completed_at: Option<String>,
}

/// The GitHub client. Cheap to copy around: it only borrows the transport.
pub struct GitHub<'a, T: Transport + ?Sized> {
    transport: &'a T,
}

impl<'a, T: Transport + ?Sized> GitHub<'a, T> {
    pub fn new(transport: &'a T) -> Self {
        Self { transport }
    }

    /// Device flow, step 1: ask for a code the member types on github.com.
    pub fn request_device_code(
        &self,
        client_id: &str,
        scopes: &str,
    ) -> Result<DeviceCode, ApiError> {
        let request = Request::post_form(
            format!("{WEB}/login/device/code"),
            &[("client_id", client_id), ("scope", scopes)],
        )
        .header("Accept", "application/json");
        self.json(&request)
    }

    /// Device flow, step 2: ask whether the member has approved yet.
    pub fn poll_device_token(
        &self,
        client_id: &str,
        device_code: &str,
    ) -> Result<DeviceTokenResponse, ApiError> {
        let request = Request::post_form(
            format!("{WEB}/login/oauth/access_token"),
            &[
                ("client_id", client_id),
                ("device_code", device_code),
                ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            ],
        )
        .header("Accept", "application/json");
        self.json(&request)
    }

    /// operationId: users/get-authenticated
    pub fn user(&self, token: &str) -> Result<User, ApiError> {
        self.json(&Self::authorized(
            Request::get(format!("{API}/user")),
            token,
        ))
    }

    /// operationId: activity/list-notifications-for-authenticated-user
    pub fn notifications(
        &self,
        token: &str,
        all: bool,
        participating: bool,
        page: u32,
        per_page: u32,
    ) -> Result<Vec<NotificationThread>, ApiError> {
        let url = format!(
            "{API}/notifications?all={all}&participating={participating}&page={page}&per_page={per_page}"
        );
        self.json(&Self::authorized(Request::get(url), token))
    }

    /// operationId: activity/mark-thread-as-read
    pub fn mark_thread_read(&self, token: &str, thread_id: &str) -> Result<(), ApiError> {
        let mut request = Self::authorized(
            Request::get(format!("{API}/notifications/threads/{}", encode(thread_id))),
            token,
        );
        request.method = "PATCH";
        self.send(&request).map(|_| ())
    }

    /// operationId: activity/mark-thread-as-done
    pub fn mark_thread_done(&self, token: &str, thread_id: &str) -> Result<(), ApiError> {
        let mut request = Self::authorized(
            Request::get(format!("{API}/notifications/threads/{}", encode(thread_id))),
            token,
        );
        request.method = "DELETE";
        self.send(&request).map(|_| ())
    }

    /// operationId: search/issues-and-pull-requests. Limited to 30 requests a minute: never poll it.
    pub fn search_issues(
        &self,
        token: &str,
        query: &str,
        page: u32,
        per_page: u32,
    ) -> Result<IssueSearchResult, ApiError> {
        let url = format!(
            "{API}/search/issues?q={}&sort=updated&order=desc&page={page}&per_page={per_page}",
            encode(query)
        );
        self.json(&Self::authorized(Request::get(url), token))
    }

    /// operationId: issues/get
    pub fn issue(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        number: u32,
    ) -> Result<Issue, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/issues/{number}",
            encode(owner),
            encode(repo)
        );
        self.json(&Self::with_text_bodies(Request::get(url), token))
    }

    /// operationId: issues/list-comments. Oldest first.
    pub fn issue_comments(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        number: u32,
        page: u32,
        per_page: u32,
    ) -> Result<Vec<IssueComment>, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/issues/{number}/comments?page={page}&per_page={per_page}",
            encode(owner),
            encode(repo)
        );
        self.json(&Self::with_text_bodies(Request::get(url), token))
    }

    /// operationId: issues/create-comment
    pub fn create_comment(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        number: u32,
        body: &str,
    ) -> Result<IssueComment, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/issues/{number}/comments",
            encode(owner),
            encode(repo)
        );
        let json = serde_json::json!({ "body": body }).to_string();
        self.json(&Self::with_text_bodies(
            Request::post_json(url, json),
            token,
        ))
    }

    /// operationId: repos/list-for-authenticated-user, most recently pushed first. Returns "owner/name" strings.
    pub fn my_repositories(&self, token: &str, per_page: u32) -> Result<Vec<String>, ApiError> {
        #[derive(Deserialize)]
        struct Repository {
            full_name: String,
        }

        let url = format!("{API}/user/repos?sort=pushed&page=1&per_page={per_page}");
        let repositories: Vec<Repository> =
            self.json(&Self::authorized(Request::get(url), token))?;
        Ok(repositories
            .into_iter()
            .map(|repository| repository.full_name)
            .collect())
    }

    /// operationId: actions/list-workflow-runs-for-repo. Newest first.
    pub fn workflow_runs(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        page: u32,
        per_page: u32,
    ) -> Result<Vec<WorkflowRun>, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/actions/runs?page={page}&per_page={per_page}",
            encode(owner),
            encode(repo)
        );
        let list: WorkflowRunList = self.json(&Self::authorized(Request::get(url), token))?;
        Ok(list.workflow_runs)
    }

    /// operationId: actions/get-workflow-run
    pub fn workflow_run(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        run_id: u64,
    ) -> Result<WorkflowRun, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/actions/runs/{run_id}",
            encode(owner),
            encode(repo)
        );
        self.json(&Self::authorized(Request::get(url), token))
    }

    /// operationId: actions/list-jobs-for-workflow-run. The first 100 jobs of the latest attempt, with their steps.
    pub fn jobs(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        run_id: u64,
    ) -> Result<Vec<Job>, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/actions/runs/{run_id}/jobs?page=1&per_page=100",
            encode(owner),
            encode(repo)
        );
        let list: JobList = self.json(&Self::authorized(Request::get(url), token))?;
        Ok(list.jobs)
    }

    /// operationId: actions/download-job-logs-for-workflow-run. GitHub redirects to a plain-text download,
    /// which the transport follows; this returns the log text. Logs can be megabytes.
    pub fn job_log(
        &self,
        token: &str,
        owner: &str,
        repo: &str,
        job_id: u64,
    ) -> Result<String, ApiError> {
        let url = format!(
            "{API}/repos/{}/{}/actions/jobs/{job_id}/logs",
            encode(owner),
            encode(repo)
        );
        self.send(&Self::authorized(Request::get(url), token))
            .map(|response| response.body)
    }

    /// operationId: actions/re-run-workflow
    pub fn rerun(&self, token: &str, owner: &str, repo: &str, run_id: u64) -> Result<(), ApiError> {
        let mut request = Self::authorized(
            Request::get(format!(
                "{API}/repos/{}/{}/actions/runs/{run_id}/rerun",
                encode(owner),
                encode(repo)
            )),
            token,
        );
        request.method = "POST";
        self.send(&request).map(|_| ())
    }

    /// Like `authorized`, but asks for the "full" media type so replies carry `body_text` next to the Markdown.
    fn with_text_bodies(request: Request, token: &str) -> Request {
        request
            .header("Authorization", format!("Bearer {token}"))
            .header("Accept", "application/vnd.github.full+json")
            .header("X-GitHub-Api-Version", API_VERSION)
    }

    fn authorized(request: Request, token: &str) -> Request {
        request
            .header("Authorization", format!("Bearer {token}"))
            .header("Accept", "application/vnd.github+json")
            .header("X-GitHub-Api-Version", API_VERSION)
    }

    fn send(&self, request: &Request) -> Result<Response, ApiError> {
        let response = self.transport.send(request).map_err(ApiError::Transport)?;
        if (200..300).contains(&response.status) {
            Ok(response)
        } else {
            Err(ApiError::Status {
                status: response.status,
                rate_limited: response.status == 429
                    || response.header("x-ratelimit-remaining") == Some("0"),
            })
        }
    }

    fn json<R: DeserializeOwned>(&self, request: &Request) -> Result<R, ApiError> {
        let response = self.send(request)?;
        serde_json::from_str(&response.body).map_err(|error| ApiError::Decode(error.to_string()))
    }
}

/// A canned transport for tests: replies in order and records what was sent.
#[cfg(test)]
pub mod testing {
    use std::cell::RefCell;
    use std::collections::VecDeque;

    use crate::http::{Request, Response, Transport};

    #[derive(Default)]
    pub struct Canned {
        replies: RefCell<VecDeque<Result<Response, String>>>,
        pub sent: RefCell<Vec<Request>>,
    }

    impl Canned {
        pub fn new() -> Self {
            Self::default()
        }

        pub fn reply(self, status: u16, body: &str) -> Self {
            self.reply_with(status, body, &[])
        }

        pub fn reply_with(self, status: u16, body: &str, headers: &[(&str, &str)]) -> Self {
            self.replies.borrow_mut().push_back(Ok(Response {
                status,
                headers: headers
                    .iter()
                    .map(|(name, value)| (name.to_string(), value.to_string()))
                    .collect(),
                body: body.to_string(),
            }));
            self
        }

        pub fn fail(self, message: &str) -> Self {
            self.replies
                .borrow_mut()
                .push_back(Err(message.to_string()));
            self
        }
    }

    impl Transport for Canned {
        fn send(&self, request: &Request) -> Result<Response, String> {
            self.sent.borrow_mut().push(request.clone());
            self.replies
                .borrow_mut()
                .pop_front()
                .expect("no canned reply left for this request")
        }
    }
}

#[cfg(test)]
mod tests {
    use super::testing::Canned;
    use super::*;

    const THREAD: &str = r#"[{
        "id": "1", "unread": true, "reason": "review_requested",
        "updated_at": "2026-01-02T03:04:05Z", "last_read_at": null,
        "subject": { "title": "Fix it", "type": "PullRequest", "url": "https://api.github.com/repos/octocat/hello/pulls/7", "latest_comment_url": null },
        "repository": { "id": 9, "name": "hello", "full_name": "octocat/hello", "private": false, "html_url": "https://github.com/octocat/hello",
                        "owner": { "login": "octocat", "id": 583231, "avatar_url": "https://avatars/1", "html_url": "https://github.com/octocat" } }
    }]"#;

    #[test]
    fn notifications_sends_the_query_and_headers_and_maps_nested_objects() {
        let transport = Canned::new().reply(200, THREAD);

        let threads = GitHub::new(&transport)
            .notifications("tok", true, false, 2, 50)
            .unwrap();

        let sent = &transport.sent.borrow()[0];
        assert_eq!(sent.method, "GET");
        assert_eq!(
            sent.url,
            "https://api.github.com/notifications?all=true&participating=false&page=2&per_page=50"
        );
        assert!(
            sent.headers
                .contains(&("Authorization", "Bearer tok".to_string()))
        );
        assert!(
            sent.headers
                .contains(&("X-GitHub-Api-Version", API_VERSION.to_string()))
        );
        assert_eq!(threads.len(), 1);
        assert!(threads[0].unread);
        assert_eq!(threads[0].subject.kind, "PullRequest");
        assert_eq!(threads[0].repository.owner.login, "octocat");
    }

    #[test]
    fn a_null_subject_url_is_none() {
        let body = THREAD.replace(
            r#""url": "https://api.github.com/repos/octocat/hello/pulls/7""#,
            r#""url": null"#,
        );
        let transport = Canned::new().reply(200, &body);

        let threads = GitHub::new(&transport)
            .notifications("tok", false, false, 1, 50)
            .unwrap();

        assert_eq!(threads[0].subject.url, None);
    }

    #[test]
    fn the_device_flow_posts_forms_to_github_dot_com() {
        let transport = Canned::new()
            .reply(200, r#"{"device_code":"dev","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"#)
            .reply(200, r#"{"error":"authorization_pending"}"#);
        let github = GitHub::new(&transport);

        let code = github.request_device_code("id", "repo read:user").unwrap();
        let poll = github.poll_device_token("id", &code.device_code).unwrap();

        assert_eq!(code.user_code, "WDJB-MJHT");
        assert_eq!(poll.error.as_deref(), Some("authorization_pending"));
        let sent = transport.sent.borrow();
        assert_eq!(sent[0].url, "https://github.com/login/device/code");
        assert_eq!(
            sent[0].body.as_ref().unwrap().1,
            "client_id=id&scope=repo%20read%3Auser"
        );
        assert_eq!(sent[1].url, "https://github.com/login/oauth/access_token");
        assert!(
            sent[1]
                .body
                .as_ref()
                .unwrap()
                .1
                .contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code")
        );
    }

    #[test]
    fn mark_thread_read_patches_the_thread() {
        let transport = Canned::new().reply(205, "");

        GitHub::new(&transport)
            .mark_thread_read("tok", "42")
            .unwrap();

        let sent = &transport.sent.borrow()[0];
        assert_eq!(
            (sent.method, sent.url.as_str()),
            ("PATCH", "https://api.github.com/notifications/threads/42")
        );
    }

    #[test]
    fn mark_thread_done_deletes_the_thread() {
        let transport = Canned::new().reply(204, "");

        GitHub::new(&transport)
            .mark_thread_done("tok", "42")
            .unwrap();

        let sent = &transport.sent.borrow()[0];
        assert_eq!(
            (sent.method, sent.url.as_str()),
            ("DELETE", "https://api.github.com/notifications/threads/42")
        );
    }

    #[test]
    fn search_encodes_the_query_and_maps_items() {
        let transport = Canned::new().reply(200, r#"{"total_count":1,"incomplete_results":false,"items":[
            {"id":70,"number":7,"title":"Fix it","state":"open","comments":2,"updated_at":"2026-01-02T03:04:05Z",
             "html_url":"https://github.com/octocat/hello/pull/7","repository_url":"https://api.github.com/repos/octocat/hello",
             "user":null,"draft":true}]}"#);

        let found = GitHub::new(&transport)
            .search_issues("tok", "is:open is:pr author:@me", 2, 50)
            .unwrap();

        assert_eq!(
            transport.sent.borrow()[0].url,
            "https://api.github.com/search/issues?q=is%3Aopen%20is%3Apr%20author%3A%40me&sort=updated&order=desc&page=2&per_page=50"
        );
        assert_eq!(found.total_count, 1);
        assert_eq!(found.items[0].number, 7);
        assert_eq!(found.items[0].user, None);
        assert_eq!(found.items[0].draft, Some(true));
    }

    #[test]
    fn issues_and_comments_ask_for_text_bodies() {
        let transport = Canned::new()
            .reply(200, r#"{"id":70,"number":7,"title":"Fix it","state":"open","created_at":"2026-01-01T00:00:00Z",
                "user":{"login":"octocat","id":1,"avatar_url":"https://avatars/1","html_url":"https://github.com/octocat"},
                "body_text":"Please review","pull_request":{"html_url":"https://github.com/octocat/hello/pull/7"}}"#)
            .reply(200, r#"[{"id":11,"created_at":"2026-01-02T00:00:00Z","user":null,"body_text":"Looks good","author_association":"MEMBER"}]"#);
        let github = GitHub::new(&transport);

        let issue = github.issue("tok", "octocat", "hello", 7).unwrap();
        let comments = github
            .issue_comments("tok", "octocat", "hello", 7, 3, 50)
            .unwrap();

        let sent = transport.sent.borrow();
        assert_eq!(
            sent[0].url,
            "https://api.github.com/repos/octocat/hello/issues/7"
        );
        assert!(
            sent[0]
                .headers
                .contains(&("Accept", "application/vnd.github.full+json".to_string()))
        );
        assert_eq!(
            sent[1].url,
            "https://api.github.com/repos/octocat/hello/issues/7/comments?page=3&per_page=50"
        );
        assert_eq!(issue.body_text.as_deref(), Some("Please review"));
        assert!(issue.pull_request.is_some());
        assert_eq!(comments[0].user, None);
        assert_eq!(comments[0].author_association.as_deref(), Some("MEMBER"));
    }

    #[test]
    fn create_comment_posts_json() {
        let transport = Canned::new().reply(
            201,
            r#"{"id":12,"created_at":"2026-01-03T00:00:00Z","user":null,"body_text":"say \"hi\""}"#,
        );

        let created = GitHub::new(&transport)
            .create_comment("tok", "o", "r", 2, "say \"hi\"")
            .unwrap();

        let sent = &transport.sent.borrow()[0];
        assert_eq!(
            (sent.method, sent.url.as_str()),
            ("POST", "https://api.github.com/repos/o/r/issues/2/comments")
        );
        assert_eq!(
            sent.body,
            Some(("application/json", r#"{"body":"say \"hi\""}"#.to_string()))
        );
        assert_eq!(created.id, 12);
    }

    #[test]
    fn actions_calls_use_the_right_paths_and_map_replies() {
        let transport = Canned::new()
            .reply(200, r#"[{"id":9,"full_name":"octocat/hello"},{"id":10,"full_name":"octocat/other"}]"#)
            .reply(200, r#"{"total_count":1,"workflow_runs":[{"id":30433642,"name":null,"display_title":"Fix it","run_number":12,"event":"push",
                "status":"in_progress","conclusion":null,"head_branch":null,"updated_at":"2026-01-02T00:05:00Z"}]}"#)
            .reply(200, r#"{"total_count":1,"jobs":[{"id":1,"name":"build","status":"completed","conclusion":"success",
                "started_at":"2026-01-02T00:00:10Z","completed_at":"2026-01-02T00:02:15Z",
                "steps":[{"name":"Checkout","status":"completed","conclusion":"success","number":1,
                          "started_at":"2026-01-02T00:00:10Z","completed_at":"2026-01-02T00:00:12Z"},
                         {"name":"Test","status":"queued","conclusion":null,"number":2}]},
                {"id":2,"name":"lint","status":"queued","conclusion":null,"started_at":"2026-01-02T00:00:10Z","completed_at":null}]}"#)
            .reply(200, "line one\nline two\n")
            .reply(201, "{}");
        let github = GitHub::new(&transport);

        let repositories = github.my_repositories("tok", 50).unwrap();
        let runs = github
            .workflow_runs("tok", "octocat", "hello", 2, 30)
            .unwrap();
        let jobs = github.jobs("tok", "octocat", "hello", 30433642).unwrap();
        let log = github.job_log("tok", "octocat", "hello", 1).unwrap();
        github.rerun("tok", "octocat", "hello", 30433642).unwrap();

        let sent = transport.sent.borrow();
        assert_eq!(
            sent[0].url,
            "https://api.github.com/user/repos?sort=pushed&page=1&per_page=50"
        );
        assert_eq!(
            sent[1].url,
            "https://api.github.com/repos/octocat/hello/actions/runs?page=2&per_page=30"
        );
        assert_eq!(
            sent[2].url,
            "https://api.github.com/repos/octocat/hello/actions/runs/30433642/jobs?page=1&per_page=100"
        );
        assert_eq!(
            sent[3].url,
            "https://api.github.com/repos/octocat/hello/actions/jobs/1/logs"
        );
        assert_eq!(
            (sent[4].method, sent[4].url.as_str()),
            (
                "POST",
                "https://api.github.com/repos/octocat/hello/actions/runs/30433642/rerun"
            )
        );
        assert_eq!(repositories, ["octocat/hello", "octocat/other"]);
        assert_eq!(
            (
                runs[0].run_number,
                runs[0].conclusion.as_deref(),
                runs[0].name.as_deref()
            ),
            (12, None, None)
        );
        assert_eq!(jobs[0].steps.len(), 2);
        assert_eq!(jobs[0].steps[1].started_at, None);
        assert!(jobs[1].steps.is_empty());
        assert_eq!(log, "line one\nline two\n");
    }

    #[test]
    fn failures_are_classified_and_described() {
        let cases: [(Canned, &str); 6] = [
            (
                Canned::new().reply(401, ""),
                "Your GitHub session has expired. Sign in again.",
            ),
            (
                Canned::new().reply_with(403, "", &[("x-ratelimit-remaining", "0")]),
                "GitHub rate limit reached. Try again in a few minutes.",
            ),
            (
                Canned::new().reply(403, ""),
                "GitHub refused this request. The app may need more permissions.",
            ),
            (
                Canned::new().reply(404, ""),
                "Not found. It may have been deleted, or you may not have access.",
            ),
            (
                Canned::new().reply(502, ""),
                "GitHub is having problems. Try again shortly.",
            ),
            (
                Canned::new().fail("dns"),
                "Could not reach GitHub. Check your connection.",
            ),
        ];
        for (transport, expected) in cases {
            let error = GitHub::new(&transport).user("tok").unwrap_err();
            assert_eq!(error.describe(), expected);
        }
    }

    #[test]
    fn unexpected_json_is_a_decode_error_not_a_panic() {
        let transport = Canned::new().reply(200, "<html>");

        let error = GitHub::new(&transport).user("tok").unwrap_err();

        assert!(matches!(error, ApiError::Decode(_)));
        assert!(!error.is_unauthorized());
    }
}
