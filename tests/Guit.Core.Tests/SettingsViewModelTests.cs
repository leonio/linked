using Guit.Core.Services;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class SettingsViewModelTests
{
    private static SettingsViewModel Create(FakeSettingsStore store, FakeThemeService themes, Session? session = null, FakeLauncher? launcher = null)
    {
        session ??= new Session(new FakeTokenStore("gho_abc"), new ImmediateDispatcher());
        return new SettingsViewModel(store, themes, session, new ShellViewModel(session, new FakeUsersApi(Sample.Octocat)), launcher ?? new FakeLauncher(), new AppInfo("1.2.3"));
    }

    [Test]
    public async Task The_saved_theme_is_shown_without_being_applied_again()
    {
        FakeSettingsStore store = new();
        store.Values[SettingKeys.Theme] = "Dark";
        FakeThemeService themes = new();

        SettingsViewModel viewModel = Create(store, themes);

        await Assert.That(viewModel.ThemeIndex).IsEqualTo((int)AppTheme.Dark);
        await Assert.That(themes.Applied.Count).IsEqualTo(0);
        await Assert.That(viewModel.Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task Choosing_a_theme_saves_and_applies_it()
    {
        FakeSettingsStore store = new();
        FakeThemeService themes = new();
        SettingsViewModel viewModel = Create(store, themes);

        viewModel.ThemeIndex = (int)AppTheme.Light;

        await Assert.That(store.Values[SettingKeys.Theme]).IsEqualTo("Light");
        await Assert.That(themes.Applied).Contains(AppTheme.Light);
    }

    [Test]
    public async Task A_selection_of_minus_one_from_the_control_is_ignored()
    {
        FakeSettingsStore store = new();
        FakeThemeService themes = new();
        SettingsViewModel viewModel = Create(store, themes);

        viewModel.ThemeIndex = -1;

        await Assert.That(store.Values.Count).IsEqualTo(0);
        await Assert.That(themes.Applied.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(null, AppTheme.System)]
    [Arguments("dark", AppTheme.Dark)]
    [Arguments("Light", AppTheme.Light)]
    [Arguments("purple", AppTheme.System)]
    [Arguments("7", AppTheme.System)]
    public async Task ReadTheme_falls_back_to_System(string? saved, AppTheme expected)
    {
        FakeSettingsStore store = new();
        if (saved is not null)
        {
            store.Values[SettingKeys.Theme] = saved;
        }

        await Assert.That(SettingKeys.ReadTheme(store)).IsEqualTo(expected);
    }

    [Test]
    public async Task SignOut_and_the_project_link_work()
    {
        Session session = new(new FakeTokenStore("gho_abc"), new ImmediateDispatcher());
        FakeLauncher launcher = new();
        SettingsViewModel viewModel = Create(new FakeSettingsStore(), new FakeThemeService(), session, launcher);

        await viewModel.OpenProjectCommand.ExecuteAsync(null);
        viewModel.SignOutCommand.Execute(null);

        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/leonio/linked"));
        await Assert.That(session.IsSignedIn).IsFalse();
    }
}
