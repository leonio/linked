---
name: dependencies
description: Which NuGet packages this repo uses and the rule for adding, replacing or upgrading one. Load before adding any PackageReference, PackageVersion, or new tool.
---

# Dependencies

Policy: use what Microsoft ships and a few large, maintained, source-generator-based community libraries.
No small utility packages. If something is under ~100 lines, write it.

## Approved

| Package | Used for | Where |
|---|---|---|
| `Refit`, `Refit.HttpClientFactory` | HTTP clients generated from interfaces | Guit.Api |
| `Microsoft.Extensions.Http` | `IHttpClientFactory` | Guit.Api |
| `Microsoft.Extensions.DependencyInjection` | DI container | Core, App |
| `CommunityToolkit.Mvvm` | `[ObservableProperty]`, `[RelayCommand]` | Core, App |
| `Microsoft.WindowsAppSDK` | WinUI 3 | Guit.App |
| `Microsoft.Windows.SDK.BuildTools` | Windows SDK build tools | Guit.App |
| `TUnit` | Tests (source-generated, runs under AOT) | tests |
| `Refit.Testing` | `StubHttp` | tests |
| `System.Text.Json` (in the framework) | JSON with source generation | Guit.Api |

Pre-approved if a task needs them (add the version to `dotnet/Directory.Packages.props` first):
`CommunityToolkit.WinUI.*` packages from the Windows Community Toolkit (controls, collections, animations, helpers),
`Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Logging` (with `[LoggerMessage]` source generation only).

## Never

`Newtonsoft.Json`, `Refit.Reflection`, `Refit.Newtonsoft.Json`, `AutoMapper`, `MediatR`, `Moq`/`NSubstitute`/any mocking
library, `Polly` directly, `ReactiveUI`, `Prism`, `Microsoft.Extensions.Hosting`, `Microsoft.Xaml.Behaviors.*`,
any reflection-based DI container, any package whose README does not say it supports trimming / Native AOT.

## Adding something not on the list

Stop and report instead of adding it, unless the task card names the package. The report must answer:

1. What it replaces (how many lines would we write otherwise)?
2. Does its README or csproj declare `IsAotCompatible` / trimming support?
3. Is it maintained (release in the last 12 months) and widely used?
4. Size effect: the exe size from CI before and after.

## Mechanics

- Versions go only in `dotnet/Directory.Packages.props` (`<PackageVersion Include="X" Version="1.2.3" />`).
  The csproj has `<PackageReference Include="X" />` with **no version**.
- Latest stable version at the time of adding. Check: `https://api.nuget.org/v3-flatcontainer/<lowercase-id>/index.json`.
  No preview/rc packages unless the task card says so.
- The target framework is set once in `dotnet/Directory.Build.props` (`GuitNetVersion`). Never write `net11.0` in a csproj.
- After any package change run the full verification in `aot-rules`: a package can compile and still break AOT.
