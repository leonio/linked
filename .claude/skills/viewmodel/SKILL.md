---
name: viewmodel
description: Rules and templates for view models, item models and services in src/Triage.Core (CommunityToolkit.Mvvm source generators, async commands, collections, fakes, tests). Load whenever a task touches Triage.Core.
---

# View models (Triage.Core)

Worked examples to copy: `ViewModels/SignInViewModel.cs`, `ViewModels/ShellViewModel.cs`, `Services/Session.cs`,
and their tests in `tests/Triage.Core.Tests/`.

Triage.Core must not reference WinUI. No `Microsoft.UI.*`, no `Windows.*`, no `DispatcherQueue`, no `Visibility`,
no `BitmapImage`, no colours or brushes. If a view model needs something only Windows can do (open a browser,
clipboard, navigate, show a dialog), add a method to an interface in `Services/` and implement it in `Triage.App`.

## Template: page view model

```csharp
public sealed partial class InboxViewModel(INotificationsApi notifications) : ObservableObject
{
    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            NotificationThread[] page = await notifications.ListAsync(all: false, participating: false, page: 1, perPage: 50, cancellationToken);
            Items.Clear();
            foreach (NotificationThread thread in page)
            {
                Items.Add(NotificationItem.From(thread));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiExceptionBase ex)
        {
            ErrorMessage = ApiErrors.Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

## Rules

**Generators**
- Class is `public sealed partial` and derives from `ObservableObject`.
- State: `[ObservableProperty] public partial T Name { get; private set; }`. Always a **partial property**, never a field. Use `private set` unless XAML writes to it with a TwoWay binding.
- A computed property (`HasError`) needs `[NotifyPropertyChangedFor(nameof(HasError))]` on every property it reads.
- Actions: `[RelayCommand]` on a `private` method. `LoadAsync` generates `LoadCommand`. Async methods take a `CancellationToken`. `[RelayCommand(IncludeCancelCommand = true)]` also generates `LoadCancelCommand`.
- `[RelayCommand(CanExecute = nameof(CanSend))]` plus `[NotifyCanExecuteChangedFor(nameof(SendCommand))]` on the properties `CanSend` reads.
- No hand-written `INotifyPropertyChanged`, no `OnPropertyChanged("literal")`.

**Async and threads**
- View model code runs on the UI thread and stays there: **never write `ConfigureAwait(false)` in Triage.Core**, never `Task.Run` around an API call (HTTP is already async), never `.Result` / `.Wait()`.
- Only touch `ObservableCollection` and `[ObservableProperty]` values from view model methods (UI thread).
- Every command that calls the API has exactly these two catches: `catch (OperationCanceledException) { }` and
  `catch (ApiExceptionBase ex) { ErrorMessage = ApiErrors.Describe(ex); }`.
  Refit throws `Refit.ApiExceptionBase` for every failed call (error status **and** no connection). It does **not**
  derive from `HttpRequestException`, so `catch (HttpRequestException)` catches nothing. Never let an exception
  escape a command (it would crash the app); never catch bare `Exception`.

**Data**
- XAML never binds to `Triage.Api` DTOs. Map each DTO to an **item model** in `Triage.Core/Models/`:
  ```csharp
  public sealed partial class NotificationItem(string id, string title, string repository, DateTimeOffset updatedAt) : ObservableObject
  {
      public string Id { get; } = id;
      public string Title { get; } = title;
      public string Repository { get; } = repository;
      public DateTimeOffset UpdatedAt { get; } = updatedAt;

      [ObservableProperty]
      public partial bool IsUnread { get; set; }

      public static NotificationItem From(NotificationThread thread) => new(...);
  }
  ```
  Immutable values are plain get-only properties (cheapest to bind). Only values that change after creation are `[ObservableProperty]`.
- Pre-compute display strings in the item model (`"octocat/hello #7"`, `"3h ago"` via a helper), so the list template has no converters.
- Lists are `ObservableCollection<T>` exposed as a get-only property, created once, never replaced. Refresh = `Clear()` + `Add`. Paging = `Add` more.
- No LINQ in code that runs per item or per frame (`foreach` instead). LINQ is fine in tests.

**Construction**
- Constructor injection with a primary constructor. Depend on interfaces (`INotificationsApi`, `ILauncher`) or Core classes (`Session`).
- Register in `TriageCoreServiceCollectionExtensions.AddTriageCore`: page view models `AddTransient`, shell-level `AddSingleton`.
- No service locator, no static state, no `IServiceProvider` parameter.
- Time: take `TimeProvider`, never `DateTime.Now` / `Task.Delay(x)` without it.

## Tests

File: `tests/Triage.Core.Tests/<Name>ViewModelTests.cs`.

- Fakes are hand-written classes in `tests/Triage.Core.Tests/Fakes.cs`, one per interface, reused by every test. Add the new interface's fake there. No mocking library.
- Run commands with `await viewModel.LoadCommand.ExecuteAsync(null);`.
- A fake simulates a failed call with `Task.FromException<T>(Failure.Status(HttpStatusCode.NotFound))` (`Failure` is in `Fakes.cs`).
- Minimum tests per view model: success path fills the state; API failure sets `ErrorMessage` and leaves `IsLoading` false; each command that changes state.
- Then add a `Check(...)` for the view model to `tests/Triage.AotSmoke/Program.cs`.

## Done when

- [ ] No `Microsoft.UI` / `Windows.` usings in Triage.Core
- [ ] Every class is `partial`; every observable value is a partial property
- [ ] Registered in `AddTriageCore`
- [ ] Tests pass; AOT smoke passes (see `aot-rules`)
