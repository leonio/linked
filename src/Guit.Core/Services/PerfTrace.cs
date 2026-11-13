using System.Globalization;

namespace Guit.Core.Services;

/// <summary>
/// Records moments a benchmark wants to time, such as "the first inbox page has arrived", as Unix milliseconds
/// in a text file. Off unless the GUIT_TRACE_FILE environment variable names a file, so it costs one environment
/// read per process. The Rust app writes the same file format; tools/measure-inbox.ps1 reads it for both.
/// </summary>
public static class PerfTrace
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("GUIT_TRACE_FILE");
    private static int _inboxMarked;

    /// <summary>Marks the first time this process has notifications to show. Later calls do nothing.</summary>
    public static void InboxLoaded()
    {
        if (Path is not null && Interlocked.Exchange(ref _inboxMarked, 1) == 0)
        {
            Append(Path, "inbox", DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Appends one "name unix-milliseconds" line. Failures are ignored: tracing must never break the app.</summary>
    internal static void Append(string path, string name, DateTimeOffset when)
    {
        try
        {
            File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture, $"{name} {when.ToUnixTimeMilliseconds()}\n"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
