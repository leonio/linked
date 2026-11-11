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
            Self::Status { status: 401, .. } => "Your GitHub session has expired. Sign in again.".into(),
            Self::Status { status: 403 | 429, rate_limited: true } => {
                "GitHub rate limit reached. Try again in a few minutes.".into()
            }
            Self::Status { status: 403, .. } => {
                "GitHub refused this request. The app may need more permissions.".into()
            }
            Self::Status { status: 404, .. } => {
                "Not found. It may have been deleted, or you may not have access.".into()
            }
            Self::Status { status: 500.., .. } => "GitHub is having problems. Try again shortly.".into(),
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

/// The GitHub client. Cheap to copy around: it only borrows the transport.
pub struct GitHub<'a, T: Transport + ?Sized> {
    transport: &'a T,
}

impl<'a, T: Transport + ?Sized> GitHub<'a, T> {
    pub fn new(transport: &'a T) -> Self {
        Self { transport }
    }

    /// Device flow, step 1: ask for a code the member types on github.com.
    pub fn request_device_code(&self, client_id: &str, scopes: &str) -> Result<DeviceCode, ApiError> {
        let request = Request::post_form(format!("{WEB}/login/device/code"), &[("client_id", client_id), ("scope", scopes)])
            .header("Accept", "application/json");
        self.json(&request)
    }

    /// Device flow, step 2: ask whether the member has approved yet.
    pub fn poll_device_token(&self, client_id: &str, device_code: &str) -> Result<DeviceTokenResponse, ApiError> {
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
        self.json(&Self::authorized(Request::get(format!("{API}/user")), token))
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
        let url = format!("{API}/notifications?all={all}&participating={participating}&page={page}&per_page={per_page}");
        self.json(&Self::authorized(Request::get(url), token))
    }

    /// operationId: activity/mark-thread-as-read
    pub fn mark_thread_read(&self, token: &str, thread_id: &str) -> Result<(), ApiError> {
        let mut request = Self::authorized(Request::get(format!("{API}/notifications/threads/{}", encode(thread_id))), token);
        request.method = "PATCH";
        self.send(&request).map(|_| ())
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
                rate_limited: response.status == 429 || response.header("x-ratelimit-remaining") == Some("0"),
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
                headers: headers.iter().map(|(name, value)| (name.to_string(), value.to_string())).collect(),
                body: body.to_string(),
            }));
            self
        }

        pub fn fail(self, message: &str) -> Self {
            self.replies.borrow_mut().push_back(Err(message.to_string()));
            self
        }
    }

    impl Transport for Canned {
        fn send(&self, request: &Request) -> Result<Response, String> {
            self.sent.borrow_mut().push(request.clone());
            self.replies.borrow_mut().pop_front().expect("no canned reply left for this request")
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

        let threads = GitHub::new(&transport).notifications("tok", true, false, 2, 50).unwrap();

        let sent = &transport.sent.borrow()[0];
        assert_eq!(sent.method, "GET");
        assert_eq!(sent.url, "https://api.github.com/notifications?all=true&participating=false&page=2&per_page=50");
        assert!(sent.headers.contains(&("Authorization", "Bearer tok".to_string())));
        assert!(sent.headers.contains(&("X-GitHub-Api-Version", API_VERSION.to_string())));
        assert_eq!(threads.len(), 1);
        assert!(threads[0].unread);
        assert_eq!(threads[0].subject.kind, "PullRequest");
        assert_eq!(threads[0].repository.owner.login, "octocat");
    }

    #[test]
    fn a_null_subject_url_is_none() {
        let body = THREAD.replace(r#""url": "https://api.github.com/repos/octocat/hello/pulls/7""#, r#""url": null"#);
        let transport = Canned::new().reply(200, &body);

        let threads = GitHub::new(&transport).notifications("tok", false, false, 1, 50).unwrap();

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
        assert_eq!(sent[0].body.as_ref().unwrap().1, "client_id=id&scope=repo%20read%3Auser");
        assert_eq!(sent[1].url, "https://github.com/login/oauth/access_token");
        assert!(sent[1].body.as_ref().unwrap().1.contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code"));
    }

    #[test]
    fn mark_thread_read_patches_the_thread() {
        let transport = Canned::new().reply(205, "");

        GitHub::new(&transport).mark_thread_read("tok", "42").unwrap();

        let sent = &transport.sent.borrow()[0];
        assert_eq!((sent.method, sent.url.as_str()), ("PATCH", "https://api.github.com/notifications/threads/42"));
    }

    #[test]
    fn failures_are_classified_and_described() {
        let cases: [(Canned, &str); 6] = [
            (Canned::new().reply(401, ""), "Your GitHub session has expired. Sign in again."),
            (Canned::new().reply_with(403, "", &[("x-ratelimit-remaining", "0")]), "GitHub rate limit reached. Try again in a few minutes."),
            (Canned::new().reply(403, ""), "GitHub refused this request. The app may need more permissions."),
            (Canned::new().reply(404, ""), "Not found. It may have been deleted, or you may not have access."),
            (Canned::new().reply(502, ""), "GitHub is having problems. Try again shortly."),
            (Canned::new().fail("dns"), "Could not reach GitHub. Check your connection."),
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
