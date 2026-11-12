//! Rows for the "Files changed" view: every changed file and its diff as one flat list.
//! A port of the C# `DiffRow`.

use crate::api::PullFile;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DiffKind {
    /// A file name heading.
    File,
    /// A "@@ -1,4 +1,6 @@" hunk heading.
    Hunk,
    Context,
    Added,
    Removed,
    /// A remark that is not code, for example "No text changes."
    Note,
}

/// One row: a file heading, a hunk heading, or one line of code.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DiffRow {
    pub kind: DiffKind,
    /// The file path, the hunk heading, or the line of code without its +/- marker.
    pub text: String,
    /// Line number in the old file; empty for added lines and headings.
    pub old_number: String,
    /// Line number in the new file; empty for removed lines and headings.
    pub new_number: String,
    /// For a file heading: "modified · +12 −3". Empty otherwise.
    pub detail: String,
}

impl DiffRow {
    fn new(kind: DiffKind, text: impl Into<String>) -> Self {
        Self {
            kind,
            text: text.into(),
            old_number: String::new(),
            new_number: String::new(),
            detail: String::new(),
        }
    }
}

/// The rows for a page of changed files: for each, a heading, then its diff.
pub fn rows(files: &[PullFile]) -> Vec<DiffRow> {
    let mut rows = Vec::new();
    for file in files {
        let path = match &file.previous_filename {
            Some(previous) if !previous.is_empty() && *previous != file.filename => {
                format!("{previous} \u{2192} {}", file.filename)
            }
            _ => file.filename.clone(),
        };
        let mut heading = DiffRow::new(DiffKind::File, path);
        heading.detail = format!(
            "{} \u{00B7} +{} \u{2212}{}",
            file.status, file.additions, file.deletions
        );
        rows.push(heading);

        match file.patch.as_deref().filter(|patch| !patch.is_empty()) {
            Some(patch) => append_patch(&mut rows, patch),
            // GitHub omits the patch for binary files, pure renames and very large diffs.
            None if file.additions + file.deletions == 0 => {
                rows.push(DiffRow::new(DiffKind::Note, "No text changes."))
            }
            None => rows.push(DiffRow::new(
                DiffKind::Note,
                "Diff not available (binary or too large). Open on GitHub to see it.",
            )),
        }
    }
    rows
}

/// Parses a unified diff (the "patch" GitHub returns for one file) into rows.
fn append_patch(rows: &mut Vec<DiffRow>, patch: &str) {
    let (mut old_line, mut new_line) = (0u32, 0u32);
    for line in patch.lines() {
        if line.starts_with("@@") {
            old_line = number_after(line, '-');
            new_line = number_after(line, '+');
            rows.push(DiffRow::new(DiffKind::Hunk, line));
            continue;
        }

        let mut characters = line.chars();
        let marker = characters.next().unwrap_or(' ');
        let code = characters.as_str().replace('\t', "    ");
        match marker {
            '+' => {
                let mut row = DiffRow::new(DiffKind::Added, code);
                row.new_number = new_line.to_string();
                new_line += 1;
                rows.push(row);
            }
            '-' => {
                let mut row = DiffRow::new(DiffKind::Removed, code);
                row.old_number = old_line.to_string();
                old_line += 1;
                rows.push(row);
            }
            // "\ No newline at end of file"
            '\\' => rows.push(DiffRow::new(DiffKind::Note, code.trim_start())),
            _ => {
                let mut row = DiffRow::new(DiffKind::Context, code);
                row.old_number = old_line.to_string();
                row.new_number = new_line.to_string();
                old_line += 1;
                new_line += 1;
                rows.push(row);
            }
        }
    }
}

/// Reads a start line out of "@@ -12,7 +14,9 @@ optional context".
fn number_after(header: &str, marker: char) -> u32 {
    header
        .split_once(marker)
        .map(|(_, rest)| {
            rest.chars()
                .take_while(char::is_ascii_digit)
                .collect::<String>()
        })
        .and_then(|digits| digits.parse().ok())
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(
        name: &str,
        status: &str,
        additions: u32,
        deletions: u32,
        patch: Option<&str>,
        previous: Option<&str>,
    ) -> PullFile {
        PullFile {
            filename: name.into(),
            status: status.into(),
            additions,
            deletions,
            patch: patch.map(str::to_string),
            previous_filename: previous.map(str::to_string),
        }
    }

    /// One row as "K old new|text [detail]". Same format as the C# tests.
    fn dump(rows: &[DiffRow]) -> String {
        rows.iter()
            .map(|row| {
                let kind = format!("{:?}", row.kind).chars().next().unwrap();
                let kind = if row.kind == DiffKind::Removed {
                    'R'
                } else {
                    kind
                };
                let detail = if row.detail.is_empty() {
                    String::new()
                } else {
                    format!(" [{}]", row.detail)
                };
                format!(
                    "{kind} {:>2} {:>2}|{}{detail}",
                    row.old_number, row.new_number, row.text
                )
            })
            .collect::<Vec<_>>()
            .join("\n")
    }

    #[test]
    fn a_patch_becomes_numbered_rows() {
        let patch = "@@ -10,4 +10,5 @@ class A\n one\n-two\n+2\n+2b\n \tthree\n\\ No newline at end of file\n@@ -40,2 +41,2 @@\n x\n y";

        let rows = rows(&[file("src/a.cs", "modified", 2, 1, Some(patch), None)]);

        assert_eq!(
            dump(&rows),
            "F      |src/a.cs [modified \u{00B7} +2 \u{2212}1]\n\
             H      |@@ -10,4 +10,5 @@ class A\n\
             C 10 10|one\n\
             R 11   |two\n\
             A    11|2\n\
             A    12|2b\n\
             C 12 13|    three\n\
             N      |No newline at end of file\n\
             H      |@@ -40,2 +41,2 @@\n\
             C 40 41|x\n\
             C 41 42|y"
        );
    }

    #[test]
    fn renames_and_files_without_a_patch_get_a_heading_and_a_note() {
        let rows = rows(&[
            file("src/new.cs", "renamed", 0, 0, None, Some("src/old.cs")),
            file("logo.png", "added", 0, 0, None, None),
            file("big.json", "modified", 9000, 100, None, None),
        ]);

        assert_eq!(
            dump(&rows),
            "F      |src/old.cs \u{2192} src/new.cs [renamed \u{00B7} +0 \u{2212}0]\n\
             N      |No text changes.\n\
             F      |logo.png [added \u{00B7} +0 \u{2212}0]\n\
             N      |No text changes.\n\
             F      |big.json [modified \u{00B7} +9000 \u{2212}100]\n\
             N      |Diff not available (binary or too large). Open on GitHub to see it."
        );
    }
}
