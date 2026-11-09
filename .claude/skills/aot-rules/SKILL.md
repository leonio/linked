---
name: aot-rules
description: The Native AOT and trimming rules for this repo. Load before writing or reviewing any C# here, and whenever a build shows an IL2xxx, IL3xxx, RF006, CsWinRT or MVVMTK warning.
---

# AOT rules

The app ships as one native executable. There is no JIT and unused code is deleted at publish.
Anything found "at run time by name" does not exist. Every rule below follows from that.

## Never use

| Banned | Use instead |
|---|---|
| `JsonSerializer.Serialize(obj)` / `Deserialize<T>(json)` without metadata | `GitHubJsonContext.Default.TypeName` overloads |
| `[JsonConverter]` built on reflection, `JsonStringEnumConverter` (non-generic) | `JsonStringEnumConverter<TEnum>` via `[JsonSourceGenerationOptions(Converters = ...)]`, or keep the field a `string` |
| `RestService.For<T>`, `AddRefitClient<T>`, package `Refit.Reflection` | `RestService.ForGenerated<T>`, `AddRefitGeneratedClient<T>` |
| `typeof(T).GetProperties()`, `Activator.CreateInstance`, `Type.GetType("name")`, `Assembly.GetTypes()` | Write the code out. A `switch` is fine. |
| `dynamic`, `System.Linq.Expressions`, `Expression.Compile` | Ordinary code or a source generator |
| `{Binding ...}` in XAML | `{x:Bind ...}` (see `winui-xaml`) |
| `IValueConverter` | An `x:Bind` function in `Guit.App.Xaml.Bind` |
| `[DllImport]` | `[LibraryImport]` on a `static partial` method |
| `Marshal.GetDelegateForFunctionPointer`, `ComImport` interfaces | `[GeneratedComInterface]` / function pointers |
| `Enum.GetValues(typeof(T))`, `Enum.Parse(typeof(T), s)` | `Enum.GetValues<T>()`, `Enum.Parse<T>(s)` |
| `string` resource/Type names passed to navigation or DI | `typeof(X)` and generic `GetRequiredService<X>()` |
| Field-based `[ObservableProperty] private string _name;` | `[ObservableProperty] public partial string Name { get; set; }` |

## Always do

- **`partial`** on every type a source generator or WinUI touches: view models, item models shown in lists,
  windows, pages, user controls, JSON contexts. If in doubt, make it `partial`.
- **Register every JSON type** in `GitHubJsonContext` (`src/Guit.Api/GitHubJsonContext.cs`): each request type,
  each reply type, and `T[]` when a method returns an array. A missing entry compiles and then throws
  `NotSupportedException` at run time.
- **Add a `Check(...)` to `tests/Guit.AotSmoke/Program.cs`** for each new Refit interface and each new view model,
  and a canned reply for each new path in `CannedHandler`. This is the only test that proves the code survives trimming.
- Prefer `sealed` classes, `record` DTOs, `static` lambdas, and constructor injection.

## When a warning appears

Warnings are errors here. Do not suppress. Fix by cause:

| Warning | Meaning | Fix |
|---|---|---|
| IL2026 / IL3050 `RequiresUnreferencedCode` / `RequiresDynamicCode` | You called a reflection-based API | Find the source-generated or generic overload (table above) |
| IL2070–IL2091 | A `Type` flows somewhere that reflects on it | Remove the reflection; do not add `[DynamicallyAccessedMembers]` to app code |
| RF006 | Refit cannot generate this method and would fall back to reflection | Change the method signature as the message says (concrete query types, no `object`) |
| CsWinRT1028 / CsWinRT1030 | A type passed to WinUI is not `partial` or has no generated vtable | Make the class `partial`; make sure the generic collection type is created in Core or App, not Api |
| MVVMTK0045 | Field-based `[ObservableProperty]` | Convert to a `partial` property |
| SYSLIB1220-ish JSON generator errors | Unsupported type in the JSON context | Use a concrete type; no `object`, no interfaces as DTO members |

If you cannot fix a warning with this table, stop and report it. Do not work around it.

## Verify

```bash
dotnet test --project tests/Guit.Api.Tests
dotnet test --project tests/Guit.Core.Tests
dotnet publish tests/Guit.AotSmoke -c Release -r linux-x64
./artifacts/publish/Guit.AotSmoke/release_linux-x64/Guit.AotSmoke
```

The publish step must print no `warning`. The binary must end with `AOT smoke: all checks passed`.
A normal `dotnet build` passing proves nothing about AOT.
