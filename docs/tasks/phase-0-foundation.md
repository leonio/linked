# Phase 0 - Foundation (done)

Nothing to do here. This records what exists so later cards can refer to it.

| Piece | Where | Notes |
|---|---|---|
| Framework and language settings | `Directory.Build.props` | `TriageNetVersion` is the only place the .NET version appears. Warnings are errors. AOT/trim analyzers on for every project. |
| Package versions | `Directory.Packages.props` | Central package management. |
| HTTP configuration | `src/Triage.Api/Http/TriageHttp.cs` | One `SocketsHttpHandler` setup; GitHub headers (Accept, API version, User-Agent). |
| Refit configuration | `src/Triage.Api/Http/GitHubRefit.cs` | JSON context, lowercase booleans in queries. Used by app, tests and smoke test. |
| JSON metadata | `src/Triage.Api/GitHubJsonContext.cs` | snake_case, web defaults. Every top-level request/reply type is listed. |
| DI registration | `TriageApiServiceCollectionExtensions`, `TriageCoreServiceCollectionExtensions`, `App.ConfigureServices` | `App -> Core -> Api`. |
| Test helpers | `tests/Triage.Api.Tests/Stub.cs`, `tests/Triage.Core.Tests/Fakes.cs` | `Stub.Client<T>(...)`, `RecordingHandler`, hand-written fakes. |
| AOT truth test | `tests/Triage.AotSmoke/Program.cs` | Real DI graph, canned HTTP, published with Native AOT and executed in CI. |
| CI | `.github/workflows/ci.yml` | Linux: tests + smoke. Windows: build, AOT publish, size report, launch the exe and require a window. |
| OpenAPI lookup | `tools/ghapi.py` | `find`, `op`, `schema`. |
