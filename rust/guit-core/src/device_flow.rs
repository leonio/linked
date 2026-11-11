//! The polling half of GitHub's OAuth device flow.

use std::time::Duration;

use crate::api::{ApiError, DeviceCode, GitHub};
use crate::http::Transport;

/// How the wait ended.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DeviceFlowOutcome {
    Authorized(String),
    Denied,
    Expired,
    /// The caller asked to stop (the member pressed Cancel).
    Cancelled,
}

/// Polls until the member approves or denies the request in their browser, or the code expires.
/// Honours the server's polling interval, including `slow_down`.
///
/// `sleep` waits for the given time and returns `false` to cancel; it is a parameter so tests do
/// not really sleep and the app can make the wait cancellable.
pub fn wait_for_token<T: Transport + ?Sized>(
    github: &GitHub<'_, T>,
    client_id: &str,
    code: &DeviceCode,
    mut sleep: impl FnMut(Duration) -> bool,
) -> Result<DeviceFlowOutcome, ApiError> {
    let mut interval = Duration::from_secs(code.interval);
    let mut waited = Duration::ZERO;
    let lifetime = Duration::from_secs(code.expires_in);

    loop {
        if !sleep(interval) {
            return Ok(DeviceFlowOutcome::Cancelled);
        }

        waited += interval;
        if waited > lifetime {
            return Ok(DeviceFlowOutcome::Expired);
        }

        let reply = github.poll_device_token(client_id, &code.device_code)?;
        if let Some(token) = reply.access_token.filter(|token| !token.is_empty()) {
            return Ok(DeviceFlowOutcome::Authorized(token));
        }

        match reply.error.as_deref() {
            Some("authorization_pending") => {}
            Some("slow_down") => {
                interval = reply.interval.map_or(interval + Duration::from_secs(5), Duration::from_secs);
            }
            Some("access_denied") => return Ok(DeviceFlowOutcome::Denied),
            Some("expired_token") => return Ok(DeviceFlowOutcome::Expired),
            other => {
                return Err(ApiError::Decode(
                    reply.error_description.or_else(|| other.map(str::to_string)).unwrap_or_else(|| {
                        "The device flow reply had neither a token nor an error.".to_string()
                    }),
                ));
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::api::testing::Canned;

    fn code(interval: u64, expires_in: u64) -> DeviceCode {
        DeviceCode {
            device_code: "dev".into(),
            user_code: "WDJB-MJHT".into(),
            verification_uri: "https://github.com/login/device".into(),
            expires_in,
            interval,
        }
    }

    #[test]
    fn keeps_polling_through_pending_and_slow_down() {
        let transport = Canned::new()
            .reply(200, r#"{"error":"authorization_pending"}"#)
            .reply(200, r#"{"error":"slow_down","interval":10}"#)
            .reply(200, r#"{"access_token":"gho_abc","token_type":"bearer","scope":"repo"}"#);
        let mut sleeps = Vec::new();

        let outcome = wait_for_token(&GitHub::new(&transport), "id", &code(5, 900), |wait| {
            sleeps.push(wait.as_secs());
            true
        });

        assert_eq!(outcome, Ok(DeviceFlowOutcome::Authorized("gho_abc".into())));
        assert_eq!(sleeps, [5, 5, 10]);
    }

    #[test]
    fn slow_down_without_an_interval_adds_five_seconds() {
        let transport = Canned::new().reply(200, r#"{"error":"slow_down"}"#).reply(200, r#"{"error":"access_denied"}"#);
        let mut sleeps = Vec::new();

        let outcome = wait_for_token(&GitHub::new(&transport), "id", &code(5, 900), |wait| {
            sleeps.push(wait.as_secs());
            true
        });

        assert_eq!(outcome, Ok(DeviceFlowOutcome::Denied));
        assert_eq!(sleeps, [5, 10]);
    }

    #[test]
    fn terminal_errors_expiry_and_cancel() {
        let expired = Canned::new().reply(200, r#"{"error":"expired_token"}"#);
        assert_eq!(wait_for_token(&GitHub::new(&expired), "id", &code(5, 900), |_| true), Ok(DeviceFlowOutcome::Expired));

        // The code's lifetime runs out before GitHub is even asked again.
        let never_asked = Canned::new();
        assert_eq!(wait_for_token(&GitHub::new(&never_asked), "id", &code(5, 3), |_| true), Ok(DeviceFlowOutcome::Expired));

        let cancelled = Canned::new();
        assert_eq!(wait_for_token(&GitHub::new(&cancelled), "id", &code(5, 900), |_| false), Ok(DeviceFlowOutcome::Cancelled));
        assert!(cancelled.sent.borrow().is_empty());
    }

    #[test]
    fn an_unknown_error_code_is_reported() {
        let transport = Canned::new().reply(200, r#"{"error":"device_flow_disabled","error_description":"Device Flow must be explicitly enabled for this App"}"#);

        let outcome = wait_for_token(&GitHub::new(&transport), "id", &code(5, 900), |_| true);

        assert_eq!(outcome, Err(ApiError::Decode("Device Flow must be explicitly enabled for this App".into())));
    }
}
