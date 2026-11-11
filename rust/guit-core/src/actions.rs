//! Rows and rules for the Actions pages. Mirrors the C# `RunStatus`, `RunItem`, `JobItem` and `ActionsLog`.

use crate::api::{Job, JobStep, WorkflowRun};
use crate::time;

/// One word for GitHub's status + conclusion pair, shared by runs, jobs and steps.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RunStatus {
    Success,
    Failure,
    Running,
    Queued,
    Cancelled,
    Skipped,
    Other,
}

impl RunStatus {
    pub fn from(status: Option<&str>, conclusion: Option<&str>) -> Self {
        match status {
            Some("in_progress") => Self::Running,
            Some("queued" | "waiting" | "requested" | "pending") => Self::Queued,
            _ => match conclusion {
                Some("success") => Self::Success,
                Some("failure" | "timed_out" | "startup_failure") => Self::Failure,
                Some("cancelled") => Self::Cancelled,
                Some("skipped") => Self::Skipped,
                _ => Self::Other,
            },
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Success => "success",
            Self::Failure => "failure",
            Self::Running => "running",
            Self::Queued => "queued",
            Self::Cancelled => "cancelled",
            Self::Skipped => "skipped",
            Self::Other => "other",
        }
    }

    /// A Segoe Fluent Icons character.
    pub fn glyph(self) -> &'static str {
        match self {
            Self::Success => "\u{E73E}",
            Self::Failure => "\u{EA39}",
            Self::Running => "\u{E895}",
            Self::Queued => "\u{E823}",
            Self::Cancelled => "\u{E711}",
            Self::Skipped => "\u{E72A}",
            Self::Other => "\u{E9CE}",
        }
    }

    /// True while the run can still change, so the page should keep refreshing.
    pub fn is_live(self) -> bool {
        matches!(self, Self::Running | Self::Queued)
    }
}

/// One workflow run in the runs list.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RunItem {
    pub id: u64,
    pub title: String,
    /// "CI #12 · push · main".
    pub detail: String,
    pub actor: String,
    pub updated: String,
    pub status: RunStatus,
}

impl RunItem {
    /// `now` is seconds since the Unix epoch.
    pub fn from_run(run: &WorkflowRun, now: i64) -> Self {
        let mut detail = format!(
            "{} #{} \u{00B7} {}",
            run.name.as_deref().unwrap_or("Workflow"),
            run.run_number,
            run.event
        );
        if let Some(branch) = &run.head_branch {
            detail.push_str(" \u{00B7} ");
            detail.push_str(branch);
        }

        Self {
            id: run.id,
            title: run.display_title.clone(),
            detail,
            actor: run
                .actor
                .as_ref()
                .map_or_else(String::new, |actor| actor.login.clone()),
            updated: time::parse_timestamp(&run.updated_at)
                .map_or_else(String::new, |then| time::relative(then, now)),
            status: RunStatus::from(run.status.as_deref(), run.conclusion.as_deref()),
        }
    }
}

/// One row of the run detail list: a job, or one step of a job (shown indented under it).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct JobRow {
    pub is_step: bool,
    /// The job this row is, or belongs to.
    pub job_id: u64,
    pub job_name: String,
    /// The job name, or the step name.
    pub name: String,
    pub status: RunStatus,
    /// "2m 05s" once finished; otherwise the status word, or nothing for a step that never ran.
    pub duration: String,
}

impl JobRow {
    /// The job row followed by one row per step.
    pub fn rows(jobs: &[Job]) -> Vec<Self> {
        let mut rows = Vec::new();
        for job in jobs {
            rows.push(Self::from_job(job));
            rows.extend(job.steps.iter().map(|step| Self::from_step(job, step)));
        }
        rows
    }

    fn from_job(job: &Job) -> Self {
        let status = RunStatus::from(Some(&job.status), job.conclusion.as_deref());
        Self {
            is_step: false,
            job_id: job.id,
            job_name: job.name.clone(),
            name: job.name.clone(),
            status,
            duration: elapsed(Some(&job.started_at), job.completed_at.as_deref())
                .unwrap_or_else(|| status.label().to_string()),
        }
    }

    fn from_step(job: &Job, step: &JobStep) -> Self {
        let status = RunStatus::from(Some(&step.status), step.conclusion.as_deref());
        Self {
            is_step: true,
            job_id: job.id,
            job_name: job.name.clone(),
            name: step.name.clone(),
            status,
            duration: elapsed(step.started_at.as_deref(), step.completed_at.as_deref())
                .unwrap_or_else(|| {
                    if status.is_live() {
                        status.label().to_string()
                    } else {
                        String::new()
                    }
                }),
        }
    }
}

fn elapsed(started: Option<&str>, completed: Option<&str>) -> Option<String> {
    let seconds = time::parse_timestamp(completed?)? - time::parse_timestamp(started?)?;
    Some(format_duration(seconds))
}

/// "5s", "2m 05s", "1h 02m".
pub fn format_duration(seconds: i64) -> String {
    let seconds = seconds.max(0);
    if seconds >= 3_600 {
        format!("{}h {:02}m", seconds / 3_600, seconds % 3_600 / 60)
    } else if seconds >= 60 {
        format!("{}m {:02}s", seconds / 60, seconds % 60)
    } else {
        format!("{seconds}s")
    }
}

/// Cleans a raw job log for display: drops each line's timestamp and terminal colour codes, turns workflow
/// commands (`##[group]`, `##[error]`...) into plain markers, and keeps only the last `max_lines` lines,
/// since the end of a log is where failures are. Returns the lines and whether earlier ones were dropped.
pub fn log_lines(raw: &str, max_lines: usize) -> (Vec<String>, bool) {
    // "2026-01-02T00:00:10.1234567Z " at the start of every line.
    const TIMESTAMP: usize = 29;

    let mut lines = Vec::new();
    for line in raw.lines() {
        let line = line.strip_prefix('\u{FEFF}').unwrap_or(line);
        let bytes = line.as_bytes();
        let line = if bytes.len() >= TIMESTAMP
            && bytes[4] == b'-'
            && bytes[10] == b'T'
            && bytes[TIMESTAMP - 2] == b'Z'
            && bytes[TIMESTAMP - 1] == b' '
        {
            &line[TIMESTAMP..]
        } else {
            line
        };
        if line.starts_with("##[endgroup]") {
            continue;
        }

        lines.push(clean_log_line(line));
    }

    let truncated = lines.len() > max_lines;
    if truncated {
        lines.drain(..lines.len() - max_lines);
    }
    (lines, truncated)
}

fn clean_log_line(line: &str) -> String {
    let (prefix, line) = match line
        .strip_prefix("##[")
        .and_then(|rest| rest.split_once(']'))
    {
        Some(("group", rest)) => ("\u{25B8} ", rest),
        Some(("error", rest)) => ("\u{2716} ", rest),
        Some(("warning", rest)) => ("\u{26A0} ", rest),
        Some((_, rest)) => ("", rest),
        None => ("", line),
    };

    let mut cleaned = String::with_capacity(prefix.len() + line.len());
    cleaned.push_str(prefix);
    // Strip ANSI escape sequences: ESC [ ... letter.
    let mut characters = line.chars().peekable();
    while let Some(character) = characters.next() {
        if character == '\u{1B}' && characters.peek() == Some(&'[') {
            characters.next();
            for next in characters.by_ref() {
                if next.is_ascii_alphabetic() {
                    break;
                }
            }
        } else {
            cleaned.push(character);
        }
    }
    cleaned
}

#[cfg(test)]
mod tests {
    use super::*;

    // 2026-06-15T12:00:00Z
    const NOW: i64 = 1_781_524_800;

    #[test]
    fn status_and_conclusion_combine_into_one_word() {
        let cases = [
            (Some("completed"), Some("success"), RunStatus::Success),
            (Some("completed"), Some("failure"), RunStatus::Failure),
            (Some("completed"), Some("timed_out"), RunStatus::Failure),
            (Some("completed"), Some("cancelled"), RunStatus::Cancelled),
            (Some("completed"), Some("skipped"), RunStatus::Skipped),
            (Some("completed"), Some("neutral"), RunStatus::Other),
            (Some("in_progress"), None, RunStatus::Running),
            (Some("queued"), None, RunStatus::Queued),
            (Some("waiting"), None, RunStatus::Queued),
            (None, None, RunStatus::Other),
        ];
        for (status, conclusion, expected) in cases {
            assert_eq!(
                RunStatus::from(status, conclusion),
                expected,
                "{status:?} {conclusion:?}"
            );
        }
        assert!(
            RunStatus::Running.is_live()
                && RunStatus::Queued.is_live()
                && !RunStatus::Success.is_live()
        );
    }

    fn run(name: Option<&str>, branch: Option<&str>) -> WorkflowRun {
        WorkflowRun {
            id: 30_433_642,
            display_title: "Fix it".into(),
            run_number: 12,
            event: "push".into(),
            updated_at: "2026-06-15T09:00:00Z".into(),
            name: name.map(str::to_string),
            status: Some("completed".into()),
            conclusion: Some("failure".into()),
            head_branch: branch.map(str::to_string),
            actor: None,
        }
    }

    #[test]
    fn a_run_builds_its_display_text() {
        let item = RunItem::from_run(&run(Some("CI"), Some("main")), NOW);
        assert_eq!(item.detail, "CI #12 \u{00B7} push \u{00B7} main");
        assert_eq!(
            (item.updated.as_str(), item.status, item.actor.as_str()),
            ("3h", RunStatus::Failure, "")
        );

        assert_eq!(
            RunItem::from_run(&run(None, None), NOW).detail,
            "Workflow #12 \u{00B7} push"
        );
    }

    #[test]
    fn steps_are_listed_under_their_job_with_durations() {
        let step = |name: &str,
                    status: &str,
                    conclusion: Option<&str>,
                    started: Option<&str>,
                    completed: Option<&str>| JobStep {
            name: name.into(),
            status: status.into(),
            conclusion: conclusion.map(str::to_string),
            started_at: started.map(str::to_string),
            completed_at: completed.map(str::to_string),
        };
        let jobs = [
            Job {
                id: 1,
                name: "build".into(),
                status: "in_progress".into(),
                conclusion: None,
                started_at: "2026-01-02T00:00:10Z".into(),
                completed_at: None,
                steps: vec![
                    step(
                        "Checkout",
                        "completed",
                        Some("success"),
                        Some("2026-01-02T00:00:10Z"),
                        Some("2026-01-02T00:00:13Z"),
                    ),
                    step(
                        "Build",
                        "in_progress",
                        None,
                        Some("2026-01-02T00:00:13Z"),
                        None,
                    ),
                    step("Test", "queued", None, None, None),
                    step("Publish", "completed", Some("skipped"), None, None),
                ],
            },
            Job {
                id: 2,
                name: "lint".into(),
                status: "completed".into(),
                conclusion: Some("success".into()),
                started_at: "2026-01-02T00:00:10Z".into(),
                completed_at: Some("2026-01-02T00:02:15Z".into()),
                steps: Vec::new(),
            },
        ];

        let rows = JobRow::rows(&jobs);

        let described: Vec<String> = rows
            .iter()
            .map(|row| {
                format!(
                    "{}{}:{}",
                    if row.is_step { ">" } else { "" },
                    row.name,
                    row.duration
                )
            })
            .collect();
        assert_eq!(
            described,
            [
                "build:running",
                ">Checkout:3s",
                ">Build:running",
                ">Test:queued",
                ">Publish:",
                "lint:2m 05s"
            ]
        );
        assert_eq!((rows[2].job_id, rows[2].job_name.as_str()), (1, "build"));
    }

    #[test]
    fn durations_are_short() {
        assert_eq!(format_duration(5), "5s");
        assert_eq!(format_duration(125), "2m 05s");
        assert_eq!(format_duration(3_725), "1h 02m");
        assert_eq!(format_duration(-4), "0s");
    }

    #[test]
    fn logs_are_cleaned_and_keep_their_end() {
        let raw = "\u{FEFF}2026-01-02T00:00:10.1234567Z ##[group]Run dotnet build\n\
                   2026-01-02T00:00:11.1234567Z \u{1B}[36;1mdotnet build\u{1B}[0m\r\n\
                   2026-01-02T00:00:12.1234567Z ##[endgroup]\n\
                   2026-01-02T00:00:13.1234567Z ##[error]Build failed\n\
                   2026-01-02T00:00:14.1234567Z ##[warning]Old node\n\
                   no timestamp here\n";

        let (lines, truncated) = log_lines(raw, 100);
        assert!(!truncated);
        assert_eq!(
            lines,
            [
                "\u{25B8} Run dotnet build",
                "dotnet build",
                "\u{2716} Build failed",
                "\u{26A0} Old node",
                "no timestamp here"
            ]
        );

        let (tail, truncated) = log_lines("a\nb\nc\nd", 2);
        assert!(truncated);
        assert_eq!(tail, ["c", "d"]);
        assert_eq!(log_lines("", 10), (Vec::new(), false));
    }
}
