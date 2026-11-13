using System.Net;
using Refit;

namespace Guit.Core.Services;

/// <summary>
/// Turns a failed API call into a sentence for the member.
/// Refit throws <see cref="ApiExceptionBase"/>: <see cref="ApiException"/> when GitHub answered with an error
/// status, <see cref="ApiRequestException"/> when no answer arrived. Neither derives from HttpRequestException,
/// so view models catch <c>ApiExceptionBase</c>.
/// </summary>
public static class ApiErrors
{
    public static string Describe(Exception exception) => exception switch
    {
        ApiException api => Describe(api),
        ApiRequestException or HttpRequestException => "Could not reach GitHub. Check your connection.",
        _ => "Something went wrong.",
    };

    private static string Describe(ApiException exception) => (int)exception.StatusCode switch
    {
        401 => "Your GitHub session has expired. Sign in again.",
        403 or 429 when IsRateLimited(exception) => "GitHub rate limit reached. Try again in a few minutes.",
        403 => "GitHub refused this request. The app may need more permissions.",
        404 => "Not found. It may have been deleted, or you may not have access.",
        >= 500 => "GitHub is having problems. Try again shortly.",
        _ => $"GitHub returned an error ({(int)exception.StatusCode} {exception.StatusCode}).",
    };

    private static bool IsRateLimited(ApiException exception)
        => exception.StatusCode == HttpStatusCode.TooManyRequests
            || (exception.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? values)
                && values.FirstOrDefault() == "0");
}
