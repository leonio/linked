//! Turning the API URL in a notification into something a person can open.

/// One issue or pull request.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SubjectLink {
    pub owner: String,
    pub repo: String,
    pub number: u32,
    pub is_pull_request: bool,
}

impl SubjectLink {
    /// Parses `https://api.github.com/repos/{owner}/{repo}/(issues|pulls)/{number}`.
    pub fn parse(api_url: Option<&str>) -> Option<Self> {
        let rest = api_url?.strip_prefix("https://api.github.com/repos/")?;
        let mut parts = rest.split('/');
        let (owner, repo, kind, number) =
            (parts.next()?, parts.next()?, parts.next()?, parts.next()?);
        if parts.next().is_some() || owner.is_empty() || repo.is_empty() {
            return None;
        }

        let is_pull_request = match kind {
            "pulls" => true,
            "issues" => false,
            _ => return None,
        };
        let number = number.parse::<u32>().ok().filter(|number| *number > 0)?;
        Some(Self {
            owner: owner.to_string(),
            repo: repo.to_string(),
            number,
            is_pull_request,
        })
    }

    /// The page on github.com.
    pub fn web_url(&self) -> String {
        let kind = if self.is_pull_request {
            "pull"
        } else {
            "issues"
        };
        format!(
            "https://github.com/{}/{}/{kind}/{}",
            self.owner, self.repo, self.number
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn issues_and_pull_requests_parse() {
        let issue = SubjectLink::parse(Some(
            "https://api.github.com/repos/octocat/hello-world/issues/12",
        ))
        .unwrap();
        assert_eq!(
            (
                issue.owner.as_str(),
                issue.repo.as_str(),
                issue.number,
                issue.is_pull_request
            ),
            ("octocat", "hello-world", 12, false)
        );
        assert_eq!(
            issue.web_url(),
            "https://github.com/octocat/hello-world/issues/12"
        );

        let pull =
            SubjectLink::parse(Some("https://api.github.com/repos/octocat/hello/pulls/7")).unwrap();
        assert!(pull.is_pull_request);
        assert_eq!(pull.web_url(), "https://github.com/octocat/hello/pull/7");
    }

    #[test]
    fn anything_else_is_rejected() {
        for url in [
            None,
            Some(""),
            Some("https://api.github.com/repos/octocat/hello/releases/3"),
            Some("https://api.github.com/repos/octocat/hello/issues/abc"),
            Some("https://api.github.com/repos/octocat/hello/issues/0"),
            Some("https://api.github.com/repos/octocat/hello/issues/12/comments"),
            Some("https://example.com/repos/octocat/hello/issues/12"),
        ] {
            assert_eq!(SubjectLink::parse(url), None, "{url:?}");
        }
    }
}
