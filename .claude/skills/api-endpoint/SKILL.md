---
name: api-endpoint
description: Step-by-step recipe for adding or changing a GitHub REST endpoint in src/Triage.Api (Refit interface method, DTO records, JSON context entry, DI registration, tests). Load whenever a task touches Triage.Api.
---

# Adding a GitHub endpoint

Worked example to copy: `src/Triage.Api/Notifications/` and `tests/Triage.Api.Tests/NotificationsApiTests.cs`.
Do the steps in order. Do not skip step 1: never write a DTO from memory.

## 1. Look the operation up

```bash
python3 tools/ghapi.py find <word>              # find the operationId
python3 tools/ghapi.py op <operationId>         # parameters, body, replies
python3 tools/ghapi.py schema <name> --depth 2  # fields of a schema
```

Write down: method, path, each parameter (path/query, required or not), the 2xx status, the reply schema name.

## 2. DTO records

File: `src/Triage.Api/<Area>/<Name>.cs`. One area folder per OpenAPI tag (Users, Notifications, Issues, Pulls, Actions...).

```csharp
/// <summary>Schema: thread.</summary>
public sealed record NotificationThread(
    string Id,
    bool Unread,
    DateTimeOffset UpdatedAt,
    NotificationSubject Subject,
    DateTimeOffset? LastReadAt = null);
```

Rules:
- `public sealed record` with a primary constructor. Put the schema name in the `<summary>`.
- **Only the fields the task needs.** GitHub objects have 50+ fields; every extra field costs parse time and binary size. Unknown JSON fields are ignored automatically.
- Names are PascalCase; the JSON context converts to snake_case (`UpdatedAt` <-> `updated_at`). Use `[property: JsonPropertyName("x")]` only when that conversion gives the wrong name.
- Type mapping from `ghapi.py` output:

| ghapi.py says | C# |
|---|---|
| `string`, `string(uri)` | `string` |
| `int` | `int`; `long` for anything named `id` |
| `long` | `long` |
| `boolean` | `bool` |
| `DateTimeOffset` | `DateTimeOffset` |
| `enum(a,b,c)` | `string` (compare with constants; do not create an enum) |
| `other-schema` | another record. Reuse an existing one if it exists (`GitHubUser` covers simple-user, `RepositorySummary` covers minimal-repository) |
| `x[]` | `X[]` (array, not `List<X>`) |
| `(nullable)` or `(optional)` | make it nullable (`string?`, `long?`) **and** give it `= null` default, placed after the required parameters |
| `oneOf(...)`, `object` with no fields | Do not model it. Leave the field out. If the task needs it, stop and report. |

- Never use `object`, `dynamic`, `JsonElement`, `Dictionary<string, object>` or an interface as a DTO member.

## 3. Refit method

File: `src/Triage.Api/<Area>/I<Area>Api.cs`.

```csharp
/// <summary>OpenAPI tag: activity (notifications).</summary>
[Headers("Authorization: Bearer")]
public interface INotificationsApi
{
    /// <summary>operationId: activity/list-notifications-for-authenticated-user</summary>
    [Get("/notifications")]
    Task<NotificationThread[]> ListAsync(bool all, int page, [AliasAs("per_page")] int perPage, CancellationToken cancellationToken);

    /// <summary>operationId: activity/mark-thread-as-read</summary>
    [Patch("/notifications/threads/{threadId}")]
    Task MarkAsReadAsync(string threadId, CancellationToken cancellationToken);
}
```

Rules:
- `[Headers("Authorization: Bearer")]` on the interface. The token is added automatically. Never pass a token as a parameter.
- Method name ends in `Async`. Last parameter is always `CancellationToken cancellationToken`.
- Put the `operationId` in the `<summary>` so the next person can look it up.
- Path parameters: `{name}` in the route matches a parameter called `name`. Use camelCase (`{issueNumber}`), not the spec's `{issue_number}`.
- Query parameters: any other simple parameter becomes `?name=value`. **A name with more than one word needs `[AliasAs("snake_case")]`** (`[AliasAs("per_page")] int perPage`). Nullable parameter + `null` = left out.
- Query parameter types: `string`, `int`, `long`, `bool`, `DateTimeOffset` and their nullable forms only. Never `object`.
- JSON body: one `[Body] SomeRequest request` parameter, where `SomeRequest` is a record. Form body (auth only): `[Body(BodySerializationMethod.UrlEncoded)]` with `[AliasAs]` on each property.
- Return types: `Task<T>` (JSON reply), `Task<T[]>` (array), `Task` (no body: 202/204/205).
- Lists: always take `int page` and `[AliasAs("per_page")] int perPage`. Paging is done by the caller in Core: ask for page 1, 2, 3... and stop when a page comes back with fewer than `perPage` items.
- A new header for one call: `[Header("Name")] string value` parameter. Do not add default headers; `TriageHttp.ConfigureApi` already sets Accept, API version and User-Agent.
- If the build prints **RF006**, the method shape is not supported by the generator. Read the message, change the signature, do not suppress.

## 4. JSON context

`src/Triage.Api/GitHubJsonContext.cs`: add one line under the right `// Area` comment for each **top-level** type a method sends or returns. Nested records are found automatically.

```csharp
// Notifications
[JsonSerializable(typeof(NotificationThread[]))]   // method returns an array -> register the array
```

Forget this and everything compiles, then throws `NotSupportedException` at run time.

## 5. DI registration (new interface only)

`src/Triage.Api/TriageApiServiceCollectionExtensions.cs`, in the sorted block:

```csharp
services.AddGitHubApi<INotificationsApi>(primaryHandler);
```

## 6. Tests

File: `tests/Triage.Api.Tests/<Area>ApiTests.cs`. One test per method, minimum.

```csharp
[Test]
public async Task List_sends_the_query_and_maps_nested_objects()
{
    using RecordingHandler http = new(OneThread);              // canned JSON reply
    INotificationsApi api = Stub.Client<INotificationsApi>(http);

    NotificationThread[] threads = await api.ListAsync(all: true, page: 2, perPage: 50, CancellationToken.None);

    await Assert.That(http.Path).IsEqualTo("/notifications?all=true&page=2&per_page=50");
    await Assert.That(threads[0].Repository.Owner.Login).IsEqualTo("octocat");
}
```

- `RecordingHandler` (in `Stub.cs`): one canned reply; exposes `Method`, `Path` (with query), `Body`. Use it to assert what was sent.
- `StubHttp` (Refit.Testing): several routes or non-200 statuses. `Route.Get/Post/Patch/Delete("/path/{id}")`, `Reply.Json("...")`, `Reply.Status(HttpStatusCode.NoContent)`. Each route answers once. End with `http.VerifyAllCalled()`.
- The canned JSON must be realistic: copy the field names from `ghapi.py schema`, include one `null` for a nullable field, include one field the DTO does not have.
- Assert at least: the exact path and query, one nested value, one nullable value, one date if there is one.
- TUnit asserts are awaited: `await Assert.That(x).IsEqualTo(y)`, `.IsTrue()`, `.IsNull()`, `.Contains(...)`. Use `.IsTrue()`, not `.IsEqualTo(true)`.

## 7. AOT smoke

`tests/Triage.AotSmoke/Program.cs`: add a canned reply for the path in `CannedHandler` and a `Check(...)` that calls the method through DI. Then run the three commands in the `aot-rules` skill.

## Done when

- [ ] `ghapi.py op` output and the method agree on verb, path, parameter names, reply type
- [ ] DTO has only needed fields; nullable/optional fields are nullable with defaults
- [ ] `GitHubJsonContext` entry added
- [ ] Tests pass; AOT smoke publishes with no warnings and prints `all checks passed`
