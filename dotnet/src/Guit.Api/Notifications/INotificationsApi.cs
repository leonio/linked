using Refit;

namespace Guit.Api.Notifications;

/// <summary>OpenAPI tag: activity (notifications).</summary>
[Headers("Authorization: Bearer")]
public interface INotificationsApi
{
    /// <summary>operationId: activity/list-notifications-for-authenticated-user</summary>
    /// <param name="all">True to include notifications already marked as read.</param>
    /// <param name="participating">True to include only threads the user is directly involved in.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="perPage">Page size, at most 50.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [Get("/notifications")]
    Task<NotificationThread[]> ListAsync(
        bool all,
        bool participating,
        int page,
        [AliasAs("per_page")] int perPage,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same request as <see cref="ListAsync"/>, for polling. With <paramref name="ifModifiedSince"/> set,
    /// GitHub answers 304 Not Modified (which does not count against the rate limit) when nothing changed.
    /// The reply carries the headers a poller needs: <c>Last-Modified</c> (content header) and <c>X-Poll-Interval</c>.
    /// Does not throw for an error status: check <c>StatusCode</c>. Dispose the reply.
    /// </summary>
    /// <param name="all">True to include notifications already marked as read.</param>
    /// <param name="participating">True to include only threads the user is directly involved in.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="perPage">Page size, at most 50.</param>
    /// <param name="ifModifiedSince">The Last-Modified value of the previous reply, or null for an unconditional request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [Get("/notifications")]
    Task<ApiResponse<NotificationThread[]>> PollAsync(
        bool all,
        bool participating,
        int page,
        [AliasAs("per_page")] int perPage,
        [Header("If-Modified-Since")] string? ifModifiedSince,
        CancellationToken cancellationToken);

    /// <summary>operationId: activity/mark-thread-as-read</summary>
    [Patch("/notifications/threads/{threadId}")]
    Task MarkAsReadAsync(string threadId, CancellationToken cancellationToken);

    /// <summary>operationId: activity/mark-thread-as-done</summary>
    [Delete("/notifications/threads/{threadId}")]
    Task MarkAsDoneAsync(string threadId, CancellationToken cancellationToken);
}
