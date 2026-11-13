using System.Reflection;
using Refit;

namespace Guit.Api.Http;

/// <summary>The one place Refit is configured. The app, the tests and the AOT smoke test all use it.</summary>
public static class GitHubRefit
{
    /// <summary>
    /// Creates settings bound to <see cref="GitHubJsonContext"/> (no reflection JSON), with lowercase
    /// booleans in query strings. Query keys are NOT renamed: a parameter such as <c>perPage</c>
    /// needs <c>[AliasAs("per_page")]</c>.
    /// Returns a new instance each call: Refit.Testing's StubHttp modifies the settings it is given.
    /// </summary>
    public static RefitSettings CreateSettings()
    {
        RefitSettings settings = RefitSettings.ForJsonContext(GitHubJsonContext.Default);
        settings.UrlParameterFormatterMap[typeof(bool)] = LowercaseBooleanFormatter.Instance;
        return settings;
    }

    /// <summary>.NET writes <c>True</c>; GitHub documents <c>true</c>.</summary>
    private sealed class LowercaseBooleanFormatter : IUrlParameterFormatter
    {
        public static readonly LowercaseBooleanFormatter Instance = new();

        public string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type) => value switch
        {
            true => "true",
            false => "false",
            _ => null,
        };
    }
}
