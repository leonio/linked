using System.Text.Json;
using System.Text.Json.Serialization;
using Guit.Api.Actions;
using Guit.Api.Auth;
using Guit.Api.Issues;
using Guit.Api.Notifications;
using Guit.Api.Pulls;
using Guit.Api.Reactions;
using Guit.Api.Repos;
using Guit.Api.Search;
using Guit.Api.Users;

namespace Guit.Api;

/// <summary>
/// Source-generated JSON metadata for every GitHub request and reply type. GitHub uses snake_case.
/// A type sent through a Refit client MUST be listed here (and its array, if a method returns one):
/// a missing type throws <see cref="NotSupportedException"/> at run time, not at build time.
/// Keep the list sorted by area.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Actions
[JsonSerializable(typeof(JobList))]
[JsonSerializable(typeof(WorkflowRun))]
[JsonSerializable(typeof(WorkflowRunList))]
// Auth
[JsonSerializable(typeof(DeviceCode))]
[JsonSerializable(typeof(DeviceTokenResponse))]
// Issues
[JsonSerializable(typeof(CreateCommentRequest))]
[JsonSerializable(typeof(Issue))]
[JsonSerializable(typeof(IssueComment))]
[JsonSerializable(typeof(IssueComment[]))]
// Notifications
[JsonSerializable(typeof(NotificationThread[]))]
// Pulls
[JsonSerializable(typeof(PullFile[]))]
[JsonSerializable(typeof(PullRequest))]
// Reactions
[JsonSerializable(typeof(CreateReactionRequest))]
// Repos
[JsonSerializable(typeof(RepositorySummary[]))]
// Search
[JsonSerializable(typeof(IssueSearchResult))]
// Users
[JsonSerializable(typeof(GitHubUser))]
public sealed partial class GitHubJsonContext : JsonSerializerContext;
