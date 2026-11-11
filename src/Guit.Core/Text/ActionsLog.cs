namespace Guit.Core.Text;

/// <summary>Cleans a raw GitHub Actions job log for display.</summary>
public static class ActionsLog
{
    // "2026-01-02T00:00:10.1234567Z " at the start of every line.
    private const int TimestampLength = 29;

    /// <summary>
    /// Splits a log into lines, dropping the timestamp prefix and terminal colour codes and turning
    /// workflow commands (##[group], ##[error]...) into plain markers. Keeps only the last
    /// <paramref name="maxLines"/> lines: the end of a log is where failures are.
    /// </summary>
    /// <param name="raw">The log text as downloaded.</param>
    /// <param name="maxLines">The most lines to return.</param>
    /// <param name="truncated">True when earlier lines were dropped.</param>
    public static string[] ToLines(string? raw, int maxLines, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        List<string> lines = [];
        foreach (ReadOnlySpan<char> rawLine in raw.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine;
            // The download starts with a byte order mark.
            if (line.Length > 0 && line[0] == '\uFEFF')
            {
                line = line[1..];
            }

            if (line.Length >= TimestampLength && line[4] == '-' && line[10] == 'T' && line[TimestampLength - 2] == 'Z' && line[TimestampLength - 1] == ' ')
            {
                line = line[TimestampLength..];
            }

            if (line.StartsWith("##[endgroup]", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(Clean(line));
        }

        // A trailing newline in the file yields one empty last line.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > maxLines)
        {
            truncated = true;
            lines.RemoveRange(0, lines.Count - maxLines);
        }

        return [.. lines];
    }

    private static string Clean(ReadOnlySpan<char> line)
    {
        string prefix = string.Empty;
        if (line.StartsWith("##[", StringComparison.Ordinal))
        {
            int close = line.IndexOf(']');
            if (close > 0)
            {
                prefix = line[3..close] switch
                {
                    "group" => "▸ ",
                    "error" => "✖ ",
                    "warning" => "⚠ ",
                    _ => string.Empty,
                };
                line = line[(close + 1)..];
            }
        }

        if (!line.Contains('\u001B'))
        {
            return prefix.Length == 0 ? line.ToString() : string.Concat(prefix, line);
        }

        // Strip ANSI escape sequences: ESC [ ... letter.
        System.Text.StringBuilder cleaned = new(prefix, prefix.Length + line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\u001B' && i + 1 < line.Length && line[i + 1] == '[')
            {
                i += 2;
                while (i < line.Length && !char.IsAsciiLetter(line[i]))
                {
                    i++;
                }

                continue;
            }

            cleaned.Append(line[i]);
        }

        return cleaned.ToString();
    }
}
