//! The one thing this crate needs from the platform: sending an HTTP request.

/// An HTTP request. Bodies are text: GitHub's API is JSON, and the device flow is a form.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Request {
    pub method: &'static str,
    pub url: String,
    pub headers: Vec<(&'static str, String)>,
    /// The body and its content type, if any.
    pub body: Option<(&'static str, String)>,
}

impl Request {
    pub fn get(url: impl Into<String>) -> Self {
        Self { method: "GET", url: url.into(), headers: Vec::new(), body: None }
    }

    pub fn post_form(url: impl Into<String>, fields: &[(&str, &str)]) -> Self {
        let body = fields
            .iter()
            .map(|(name, value)| format!("{}={}", encode(name), encode(value)))
            .collect::<Vec<_>>()
            .join("&");
        Self {
            method: "POST",
            url: url.into(),
            headers: Vec::new(),
            body: Some(("application/x-www-form-urlencoded", body)),
        }
    }

    pub fn header(mut self, name: &'static str, value: impl Into<String>) -> Self {
        self.headers.push((name, value.into()));
        self
    }
}

/// An HTTP reply that arrived, whatever its status.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Response {
    pub status: u16,
    /// Header names in lower case.
    pub headers: Vec<(String, String)>,
    pub body: String,
}

impl Response {
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers.iter().find(|(key, _)| key == name).map(|(_, value)| value.as_str())
    }
}

/// Sends requests. The app implements this on a Windows HTTP stack; tests use a canned one.
/// `Err` means no reply arrived (offline, DNS, TLS); an error status is still `Ok`.
pub trait Transport {
    fn send(&self, request: &Request) -> Result<Response, String>;
}

/// Percent-encodes a query or form value (RFC 3986 unreserved characters pass through).
pub fn encode(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for byte in value.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'.' | b'_' | b'~' => out.push(byte as char),
            _ => {
                out.push('%');
                out.push(char::from(b"0123456789ABCDEF"[(byte >> 4) as usize]));
                out.push(char::from(b"0123456789ABCDEF"[(byte & 15) as usize]));
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_form_is_percent_encoded() {
        let request = Request::post_form("https://x/y", &[("grant_type", "urn:ietf:a b"), ("code", "A-1_~.")]);
        assert_eq!(request.body.unwrap().1, "grant_type=urn%3Aietf%3Aa%20b&code=A-1_~.");
    }

    #[test]
    fn headers_are_found_by_lower_case_name() {
        let response = Response { status: 200, headers: vec![("x-poll-interval".into(), "60".into())], body: String::new() };
        assert_eq!(response.header("x-poll-interval"), Some("60"));
        assert_eq!(response.header("etag"), None);
    }
}
