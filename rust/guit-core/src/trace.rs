//! Records moments a benchmark wants to time, such as "the first inbox page has arrived", as Unix milliseconds
//! in a text file. Off unless the GUIT_TRACE_FILE environment variable names a file. Same file format as the C#
//! app's `PerfTrace`; tools/measure-inbox.ps1 reads it for both.

use std::io::Write;
use std::sync::OnceLock;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

/// Marks the first time this process has notifications to show. Later calls do nothing.
pub fn inbox_loaded() {
    static PATH: OnceLock<Option<String>> = OnceLock::new();
    static MARKED: AtomicBool = AtomicBool::new(false);

    if let Some(path) = PATH.get_or_init(|| std::env::var("GUIT_TRACE_FILE").ok())
        && !MARKED.swap(true, Ordering::Relaxed)
    {
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |elapsed| elapsed.as_millis());
        append(path, "inbox", now);
    }
}

/// Appends one "name unix-milliseconds" line. Failures are ignored: tracing must never break the app.
fn append(path: &str, name: &str, unix_millis: u128) {
    if let Ok(mut file) = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
    {
        _ = writeln!(file, "{name} {unix_millis}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lines_are_appended_and_a_bad_path_is_ignored() {
        let path = std::env::temp_dir().join(format!("guit-trace-{}.txt", std::process::id()));
        let path = path.to_string_lossy().to_string();
        _ = std::fs::remove_file(&path);

        append(&path, "inbox", 1_700_000_000_123);
        append(&path, "inbox", 1_700_000_000_456);
        append("/no/such/folder/trace.txt", "inbox", 1);

        assert_eq!(
            std::fs::read_to_string(&path).unwrap(),
            "inbox 1700000000123\ninbox 1700000000456\n"
        );
        _ = std::fs::remove_file(&path);
    }
}
