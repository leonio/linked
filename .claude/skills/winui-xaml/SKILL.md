---
name: winui-xaml
description: Rules, templates and known compiler traps for XAML and code-behind in dotnet/src/Guit.App (WinUI 3, x:Bind only, Fluent design resources, lists, navigation, AOT-safe patterns). Load whenever a task touches Guit.App.
---

# WinUI XAML (Guit.App)

Worked examples to copy: `Views/InboxPage.xaml` (+ `.xaml.cs`: the full list page), `Views/ShellView.xaml`,
`Views/SignInView.xaml`, `Xaml/Bind.cs`, `MainWindow.xaml`.

Guit.App only builds on Windows. If you are elsewhere, write the XAML carefully against this skill, push,
and read the "WinUI app" job in CI. Say in your report that it was verified by CI only (or not at all).

## The rules

1. **`{x:Bind}` only. Never `{Binding}`.** `{Binding}` uses reflection and breaks under Native AOT.
   `{StaticResource}` and `{ThemeResource}` are fine.
2. **Every `DataTemplate` has `x:DataType`.** Without it `x:Bind` does not compile.
3. **No `IValueConverter`.** Use a function from `Guit.App.Xaml.Bind`: `{x:Bind xaml:Bind.VisibleIf(ViewModel.IsEmpty), Mode=OneWay}`.
4. **No logic in code-behind.** Code-behind may: get the view model from DI, call `InitializeComponent`, forward an
   event to a view model command, and do things only a view can do (focus, scroll into view, navigation frame).
5. **Fluent by default.** Use WinUI controls with their default styles, the built-in text styles, and theme brushes.
   No hard-coded colours, font sizes or font families (monospace code text is the one exception).
6. **No third-party control libraries** beyond what the `dependencies` skill approves.

## x:Bind cheat sheet

| Need | Write |
|---|---|
| Value that never changes | `{x:Bind ViewModel.Title}` (default mode is OneTime - the cheapest) |
| Value that changes | `{x:Bind ViewModel.Title, Mode=OneWay}` |
| Text box / toggle the user edits | `{x:Bind ViewModel.Draft, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}` |
| Command | `Command="{x:Bind ViewModel.LoadCommand}"` |
| bool -> Visibility | `Visibility="{x:Bind ViewModel.IsBusy, Mode=OneWay}"` (a plain bool property converts by itself) |
| inverted / computed visibility | `Visibility="{x:Bind xaml:Bind.VisibleIfNot(ViewModel.IsBusy), Mode=OneWay}"` |
| string? -> bool | `IsOpen="{x:Bind xaml:Bind.IsNotEmpty(ViewModel.ErrorMessage), Mode=OneWay}"` |
| image from URL | `Source="{x:Bind xaml:Bind.Image(AvatarUrl, 24)}"` (decodes at display size) |
| Inside a `DataTemplate` | paths start at the item: `{x:Bind Title}`, not `{x:Bind ViewModel...}` |

Add `xmlns:xaml="using:Guit.App.Xaml"` to use `Bind`. New helpers go in `Xaml/Bind.cs` as `public static` pure functions.

## Compiler traps (each of these has already cost a CI run)

- **A `Bind` function must return the exact type of the target property.** Returning `bool` into `Visibility` compiles a
  broken `.g.cs` (`error CS0103: The name 'obj' does not exist`, plus `WMC1506`). Return `Visibility` (`Bind.VisibleIf`).
- **`x:Load` and `x:Bind` do not work directly inside `<Window>`.** A `Window` is not a `FrameworkElement`
  (`error CS1061: 'MainWindow' does not contain a definition for 'FindName'`). `MainWindow.xaml` holds only the title bar
  and one root view; everything else lives in a `UserControl` or `Page`.
- **An element with `x:Load` needs `x:Name`.**
- **Event handlers that do not use `this`** fail the build with CA1822. Use a named element or `ViewModel` in the handler
  (that is what handlers are for), or make the handler `static`.
- **`x:Bind` inside a `DataTemplate` cannot see the page.** To run a page-level command from an item, use a `Click`
  handler in code-behind and pass the item with `CommandParameter="{x:Bind}"`:
  `ViewModel.MarkAsDoneCommand.Execute(((MenuFlyoutItem)sender).CommandParameter as NotificationItem);`
- **`WMC1506` ("OneWay bindings require ... notifications")** means the path has no property that raises change
  notifications. Either the property should be `[ObservableProperty]`, or the binding should not be `OneWay`.
- XAML compiler errors point at `obj\...\*.g.cs`. The cause is always in the `.xaml` file of the same name;
  look at the binding on the element the nearby warning names.

## Page skeleton

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Guit.App.Views.InboxPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:models="using:Guit.Core.Models"
    xmlns:xaml="using:Guit.App.Xaml">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />
      <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <TextBlock Margin="24,16" Style="{StaticResource TitleTextBlockStyle}" Text="Inbox" />

    <ListView
        Grid.Row="1"
        IsItemClickEnabled="True"
        ItemClick="OnItemClick"
        ItemsSource="{x:Bind ViewModel.Items}"
        SelectionMode="None">
      <ListView.ItemTemplate>
        <DataTemplate x:DataType="models:NotificationItem">
          <Grid Height="56" ColumnSpacing="12">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto" />
              <ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <FontIcon VerticalAlignment="Center" FontSize="16" Glyph="{x:Bind Glyph}" />
            <StackPanel Grid.Column="1" VerticalAlignment="Center">
              <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind Title}" TextTrimming="CharacterEllipsis" />
              <TextBlock
                  Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                  Style="{StaticResource CaptionTextBlockStyle}"
                  Text="{x:Bind Repository}" />
            </StackPanel>
            <Image Grid.Column="2" Width="20" Height="20" x:Phase="1" Source="{x:Bind xaml:Bind.Image(AvatarUrl, 20)}" />
          </Grid>
        </DataTemplate>
      </ListView.ItemTemplate>
    </ListView>

    <ProgressRing Grid.Row="1" IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
    <InfoBar
        Grid.Row="1"
        VerticalAlignment="Top"
        IsClosable="False"
        IsOpen="{x:Bind xaml:Bind.IsNotEmpty(ViewModel.ErrorMessage), Mode=OneWay}"
        Message="{x:Bind ViewModel.ErrorMessage, Mode=OneWay}"
        Severity="Error" />
  </Grid>
</Page>
```

```csharp
public sealed partial class InboxPage : Page
{
    public InboxPage()
    {
        ViewModel = App.Services.GetRequiredService<InboxViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public InboxViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (ViewModel.Items.Count == 0)
        {
            ViewModel.LoadCommand.Execute(null);
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
        => ViewModel.OpenCommand.Execute((NotificationItem)e.ClickedItem);
}
```

- The class is `public sealed partial`. The view model is a get-only property named `ViewModel`, set **before** `InitializeComponent()`.
- `App.Services.GetRequiredService<T>()` is allowed only in a view's constructor.
- Pages receive at most one `string` argument (`e.Parameter as string`). Never pass objects through `Frame.Navigate`.
- Navigate with `typeof(SomePage)`, via `Navigator`. Never by type name string.

## Fluent building blocks (use these, do not invent)

| Need | Use |
|---|---|
| Text | `TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyStrongTextBlockStyle`, `BodyTextBlockStyle`, `CaptionTextBlockStyle` |
| Secondary text colour | `{ThemeResource TextFillColorSecondaryBrush}` |
| Card surface | `Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"`, `BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"`, `BorderThickness="1"`, `CornerRadius="{ThemeResource ControlCornerRadius}"` |
| Primary button | `Style="{StaticResource AccentButtonStyle}"` |
| Status colours | `SystemFillColorSuccessBrush`, `SystemFillColorCautionBrush`, `SystemFillColorCriticalBrush`, `SystemFillColorAttentionBrush`, `SystemFillColorNeutralBrush` |
| Icons | `FontIcon Glyph="&#xE8BD;"` (Segoe Fluent Icons) or `SymbolIcon` |
| Error / info message | `InfoBar` |
| Busy | `ProgressRing` (whole area), `ProgressBar IsIndeterminate="True"` (thin, inline) |
| Sections / filters | `SelectorBar` |
| Menus | `MenuFlyout`, `CommandBarFlyout` |
| Avatar | `PersonPicture` or an `Image` with `Bind.Image` |
| App chrome | `TitleBar` control, `MicaBackdrop`, `NavigationView` (already in place) |
| Spacing | multiples of 4: 4, 8, 12, 16, 24. Page margin 24. Use `Spacing`/`ColumnSpacing`, not margins on every child. |

Always `{ThemeResource}` for brushes (they follow light/dark/high contrast); `{StaticResource}` for styles.

## Lists

Read the Lists section of the `performance` skill before writing any list. In short: `ListView`, never inside a
`ScrollViewer` or `StackPanel`; shallow templates; `x:Phase` for images; OneTime bindings unless the value changes.

## Deferring UI

- `x:Load="False"` + `FindName("Name")` when needed, or `x:Load="{x:Bind ViewModel.Flag, Mode=OneWay}"`, for panels
  that most sessions never show. The element needs an `x:Name`.
- `Visibility="Collapsed"` still creates the element tree; `x:Load` does not. Prefer `x:Load` for anything big.

## Done when

- [ ] No `{Binding}`, no converters, no hard-coded colours
- [ ] Every `DataTemplate` has `x:DataType`
- [ ] Built on Windows or by CI; say which
- [ ] Any new page is mapped in `Navigator`
