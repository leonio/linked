# Phase 0 - Foundation (done)

Nothing to do here. This records what exists so later cards can refer to it.

| Piece | Where | Notes |
|---|---|---|
| Framework and language settings | `dotnet/Directory.Build.props` | `GuitNetVersion` is the only place the .NET version appears. Warnings are errors. AOT/trim analyzers on for every project. |
| Package versions | `dotnet/Directory.Packages.props` | Central package management. |
| HTTP configuration | `dotnet/src/Guit.Api/Http/GuitHttp.cs` | One `SocketsHttpHandler` setup; GitHub headers (Accept, API version, User-Agent). |
| Refit configuration | `dotnet/src/Guit.Api/Http/GitHubRefit.cs` | JSON context, lowercase booleans in queries. Used by app, tests and smoke test. |
| JSON metadata | `dotnet/src/Guit.Api/GitHubJsonContext.cs` | snake_case, web defaults. Every top-level request/reply type is listed. |
| DI registration | `GuitApiServiceCollectionExtensions`, `GuitCoreServiceCollectionExtensions`, `App.ConfigureServices` | `App -> Core -> Api`. |
| Test helpers | `dotnet/tests/Guit.Api.Tests/Stub.cs`, `dotnet/tests/Guit.Core.Tests/Fakes.cs` | `Stub.Client<T>(...)`, `RecordingHandler`, hand-written fakes. |
| AOT truth test | `dotnet/tests/Guit.AotSmoke/Program.cs` | Real DI graph, canned HTTP, published with Native AOT and executed in CI. |
| CI | `.github/workflows/ci.yml` | Linux: tests + smoke. Windows: build, AOT publish, size report, launch the exe and require a window. |
| OpenAPI lookup | `tools/ghapi.py` | `find`, `op`, `schema`. |
