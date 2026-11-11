using System.Globalization;
using Guit.Api.Actions;
using Guit.Core.Text;

namespace Guit.Core.Models;

/// <summary>One workflow run in the runs list.</summary>
public sealed partial class RunItem
{
    private RunItem(WorkflowRun run, DateTimeOffset now)
    {
        Id = run.Id;
        Title = run.DisplayTitle;
        Detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{run.Name ?? "Workflow"} #{run.RunNumber} · {run.Event}{(run.HeadBranch is null ? null : " · " + run.HeadBranch)}");
        Actor = run.Actor?.Login ?? string.Empty;
        AvatarUrl = run.Actor is null ? string.Empty : NotificationItem.SmallAvatar(run.Actor.AvatarUrl);
        Updated = RelativeTime.Format(run.UpdatedAt, now);
        StatusKind = RunStatus.Kind(run.Status, run.Conclusion);
        StatusGlyph = RunStatus.Glyph(StatusKind);
    }

    public long Id { get; }

    public string Title { get; }

    /// <summary>"CI #12 · push · main".</summary>
    public string Detail { get; }

    public string Actor { get; }

    public string AvatarUrl { get; }

    public string Updated { get; }

    /// <summary>One of the <see cref="RunStatus"/> constants.</summary>
    public string StatusKind { get; }

    public string StatusGlyph { get; }

    public static RunItem From(WorkflowRun run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new RunItem(run, now);
    }
}

/// <summary>One job of a workflow run.</summary>
public sealed partial class JobItem
{
    private JobItem(Job job)
    {
        Name = job.Name;
        StatusKind = RunStatus.Kind(job.Status, job.Conclusion);
        StatusGlyph = RunStatus.Glyph(StatusKind);
        Duration = job.CompletedAt is { } completed ? FormatDuration(completed - job.StartedAt) : StatusKind;
        WebUrl = job.HtmlUrl;
    }

    public string Name { get; }

    public string StatusKind { get; }

    public string StatusGlyph { get; }

    /// <summary>"2m 05s" once finished; otherwise the status word ("running", "queued").</summary>
    public string Duration { get; }

    public string WebUrl { get; }

    public static JobItem From(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new JobItem(job);
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}h {duration.Minutes:00}m");
        }

        return duration.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes}m {duration.Seconds:00}s")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.Seconds}s");
    }
}
