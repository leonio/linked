using System.Globalization;
using Guit.Api.Pulls;

namespace Guit.Core.Models;

public enum DiffRowKind
{
    /// <summary>A file name heading.</summary>
    File,

    /// <summary>A "@@ -1,4 +1,6 @@" hunk heading.</summary>
    Hunk,
    Context,
    Added,
    Removed,

    /// <summary>A remark that is not code, for example "No diff available".</summary>
    Note,
}

/// <summary>
/// One row of the "Files changed" list: a file heading, a hunk heading, or one line of code.
/// The whole pull request is one flat list of these, so the page is a single virtualised ListView.
/// </summary>
public sealed partial class DiffRow
{
    private DiffRow(DiffRowKind kind, string text, string oldNumber = "", string newNumber = "", string detail = "")
    {
        Kind = kind;
        Text = text;
        OldNumber = oldNumber;
        NewNumber = newNumber;
        Detail = detail;
    }

    public DiffRowKind Kind { get; }

    /// <summary>The file path, the hunk heading, or the line of code without its +/- marker.</summary>
    public string Text { get; }

    /// <summary>Line number in the old file; empty for added lines and headings.</summary>
    public string OldNumber { get; }

    /// <summary>Line number in the new file; empty for removed lines and headings.</summary>
    public string NewNumber { get; }

    /// <summary>For a file heading: "modified · +12 −3". Empty otherwise.</summary>
    public string Detail { get; }

    /// <summary>Appends the rows for one changed file: a heading, then its diff.</summary>
    public static void AppendFile(List<DiffRow> rows, PullFile file)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(file);

        string path = file.PreviousFilename is { Length: > 0 } previous && previous != file.Filename
            ? $"{previous} → {file.Filename}"
            : file.Filename;
        rows.Add(new DiffRow(
            DiffRowKind.File,
            path,
            detail: string.Create(CultureInfo.InvariantCulture, $"{file.Status} · +{file.Additions} −{file.Deletions}")));

        if (string.IsNullOrEmpty(file.Patch))
        {
            // GitHub omits the patch for binary files, pure renames and very large diffs.
            rows.Add(new DiffRow(DiffRowKind.Note, file.Additions + file.Deletions == 0 ? "No text changes." : "Diff not available (binary or too large). Open on GitHub to see it."));
            return;
        }

        AppendPatch(rows, file.Patch);
    }

    /// <summary>Parses a unified diff (the "patch" GitHub returns for one file) into rows.</summary>
    internal static void AppendPatch(List<DiffRow> rows, string patch)
    {
        int oldLine = 0;
        int newLine = 0;
        foreach (ReadOnlySpan<char> line in patch.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                ParseHunkHeader(line, out oldLine, out newLine);
                rows.Add(new DiffRow(DiffRowKind.Hunk, line.ToString()));
                continue;
            }

            char marker = line.IsEmpty ? ' ' : line[0];
            string code = line.IsEmpty ? string.Empty : line[1..].ToString().Replace("\t", "    ", StringComparison.Ordinal);
            switch (marker)
            {
                case '+':
                    rows.Add(new DiffRow(DiffRowKind.Added, code, newNumber: Number(newLine++)));
                    break;
                case '-':
                    rows.Add(new DiffRow(DiffRowKind.Removed, code, oldNumber: Number(oldLine++)));
                    break;
                case '\\':
                    // "\ No newline at end of file"
                    rows.Add(new DiffRow(DiffRowKind.Note, code.TrimStart()));
                    break;
                default:
                    rows.Add(new DiffRow(DiffRowKind.Context, code, Number(oldLine++), Number(newLine++)));
                    break;
            }
        }
    }

    /// <summary>Reads the two start lines out of "@@ -12,7 +14,9 @@ optional context".</summary>
    private static void ParseHunkHeader(ReadOnlySpan<char> header, out int oldLine, out int newLine)
    {
        oldLine = ReadNumberAfter(header, '-');
        newLine = ReadNumberAfter(header, '+');
    }

    private static int ReadNumberAfter(ReadOnlySpan<char> text, char marker)
    {
        int start = text.IndexOf(marker) + 1;
        if (start <= 0)
        {
            return 0;
        }

        int end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.TryParse(text[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;
    }

    private static string Number(int line) => line.ToString(CultureInfo.InvariantCulture);
}
