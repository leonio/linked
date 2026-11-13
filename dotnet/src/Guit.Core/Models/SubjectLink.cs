using System.Globalization;

namespace Guit.Core.Models;

/// <summary>Identifies one issue or pull request. Travels between pages as a string ("owner/repo#12", "owner/repo!12" for a pull request).</summary>
public readonly record struct SubjectLink(string Owner, string Repo, int Number, bool IsPullRequest)
{
    private const string ApiPrefix = "https://api.github.com/repos/";

    /// <summary>Parses an API URL: https://api.github.com/repos/{owner}/{repo}/(issues|pulls)/{number}.</summary>
    public static bool TryParse(string? apiUrl, out SubjectLink link)
    {
        link = default;
        if (apiUrl is null || !apiUrl.StartsWith(ApiPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = apiUrl[ApiPrefix.Length..].Split('/');
        if (parts.Length != 4 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        bool isPull = parts[2] == "pulls";
        if ((!isPull && parts[2] != "issues") || !TryParseNumber(parts[3], out int number))
        {
            return false;
        }

        link = new SubjectLink(parts[0], parts[1], number, isPull);
        return true;
    }

    /// <summary>Parses the navigation argument made by <see cref="ToArgument"/>.</summary>
    public static bool TryParseArgument(string? argument, out SubjectLink link)
    {
        link = default;
        if (argument is null)
        {
            return false;
        }

        int slash = argument.IndexOf('/', StringComparison.Ordinal);
        int marker = argument.LastIndexOfAny(['#', '!']);
        if (slash <= 0 || marker <= slash + 1 || !TryParseNumber(argument.AsSpan(marker + 1), out int number))
        {
            return false;
        }

        link = new SubjectLink(argument[..slash], argument[(slash + 1)..marker], number, argument[marker] == '!');
        return true;
    }

    public string ToArgument()
        => string.Create(CultureInfo.InvariantCulture, $"{Owner}/{Repo}{(IsPullRequest ? '!' : '#')}{Number}");

    public Uri ToWebUri()
        => new(string.Create(CultureInfo.InvariantCulture, $"https://github.com/{Owner}/{Repo}/{(IsPullRequest ? "pull" : "issues")}/{Number}"));

    private static bool TryParseNumber(ReadOnlySpan<char> text, out int number)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
}
