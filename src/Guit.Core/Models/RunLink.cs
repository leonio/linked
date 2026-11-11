using System.Globalization;

namespace Guit.Core.Models;

/// <summary>Identifies one workflow run. Travels between pages as "owner/repo@123456".</summary>
public readonly record struct RunLink(string Owner, string Repo, long RunId)
{
    public static bool TryParseArgument(string? argument, out RunLink link)
    {
        link = default;
        if (argument is null)
        {
            return false;
        }

        int slash = argument.IndexOf('/', StringComparison.Ordinal);
        int at = argument.LastIndexOf('@');
        if (slash <= 0 || at <= slash + 1
            || !long.TryParse(argument.AsSpan(at + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long runId)
            || runId <= 0)
        {
            return false;
        }

        link = new RunLink(argument[..slash], argument[(slash + 1)..at], runId);
        return true;
    }

    /// <summary>Builds a link from "owner/repo" and a run id. False if the repository name has no slash.</summary>
    public static bool TryCreate(string? repository, long runId, out RunLink link)
        => TryParseArgument(string.Create(CultureInfo.InvariantCulture, $"{repository}@{runId}"), out link);

    public string ToArgument() => string.Create(CultureInfo.InvariantCulture, $"{Owner}/{Repo}@{RunId}");

    public Uri ToWebUri() => new(string.Create(CultureInfo.InvariantCulture, $"https://github.com/{Owner}/{Repo}/actions/runs/{RunId}"));
}

/// <summary>Identifies one job's log. Travels between pages as "owner/repo@jobId" + tab + job name.</summary>
public readonly record struct JobLogLink(string Owner, string Repo, long JobId, string JobName)
{
    public static bool TryParseArgument(string? argument, out JobLogLink link)
    {
        link = default;
        int tab = argument?.IndexOf('\t', StringComparison.Ordinal) ?? -1;
        if (tab < 0 || !RunLink.TryParseArgument(argument![..tab], out RunLink job))
        {
            return false;
        }

        link = new JobLogLink(job.Owner, job.Repo, job.RunId, argument[(tab + 1)..]);
        return true;
    }

    public string ToArgument() => new RunLink(Owner, Repo, JobId).ToArgument() + "\t" + JobName;
}
