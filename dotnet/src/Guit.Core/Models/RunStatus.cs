namespace Guit.Core.Models;

/// <summary>Turns GitHub's status + conclusion pair into one word and one icon, shared by runs and jobs.</summary>
public static class RunStatus
{
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Running = "running";
    public const string Queued = "queued";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
    public const string Other = "other";

    /// <param name="status">"queued", "in_progress", "completed", "waiting", "requested", "pending" or null.</param>
    /// <param name="conclusion">Null until completed.</param>
    public static string Kind(string? status, string? conclusion) => status switch
    {
        "in_progress" => Running,
        "queued" or "waiting" or "requested" or "pending" => Queued,
        _ => conclusion switch
        {
            "success" => Success,
            "failure" or "timed_out" or "startup_failure" => Failure,
            "cancelled" => Cancelled,
            "skipped" => Skipped,
            _ => Other,
        },
    };

    /// <summary>A Segoe Fluent Icons character for a kind.</summary>
    public static string Glyph(string kind) => kind switch
    {
        Success => "",
        Failure => "",
        Running => "",
        Queued => "",
        Cancelled => "",
        Skipped => "",
        _ => "",
    };
}
