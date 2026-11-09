using Guit.Api.Users;
using Guit.Core.Services;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class ShellViewModelTests
{
    private static readonly GitHubUser Octocat = new(1, "octocat", "https://avatars/1", "https://github.com/octocat");

    [Test]
    public async Task A_stored_token_starts_the_shell_signed_in()
    {
        ShellViewModel viewModel = new(new Session(new FakeTokenStore("gho_abc")), new FakeUsersApi(Octocat));

        await viewModel.LoadUserCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsSignedIn).IsTrue();
        await Assert.That(viewModel.Login).IsEqualTo("octocat");
    }

    [Test]
    public async Task Signing_in_and_out_updates_the_shell_and_raises_IsSignedOut()
    {
        Session session = new(new FakeTokenStore());
        ShellViewModel viewModel = new(session, new FakeUsersApi(Octocat));
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        session.SignIn("gho_abc");
        await Assert.That(viewModel.IsSignedIn).IsTrue();
        await Assert.That(viewModel.Login).IsEqualTo("octocat");
        await Assert.That(changed).Contains(nameof(ShellViewModel.IsSignedOut));

        viewModel.SignOutCommand.Execute(null);
        await Assert.That(viewModel.IsSignedIn).IsFalse();
        await Assert.That(viewModel.Login).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task A_failed_user_request_does_not_sign_the_member_out()
    {
        ShellViewModel viewModel = new(new Session(new FakeTokenStore("gho_abc")), new FakeUsersApi(null));

        await viewModel.LoadUserCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsSignedIn).IsTrue();
        await Assert.That(viewModel.Login).IsEqualTo(string.Empty);
    }
}
