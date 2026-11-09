using Refit;

namespace Linked.Api.Notifications;

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

    /// <summary>operationId: activity/mark-thread-as-read</summary>
    [Patch("/notifications/threads/{threadId}")]
    Task MarkAsReadAsync(string threadId, CancellationToken cancellationToken);

    /// <summary>operationId: activity/mark-thread-as-done</summary>
    [Delete("/notifications/threads/{threadId}")]
    Task MarkAsDoneAsync(string threadId, CancellationToken cancellationToken);
}
