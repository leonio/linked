using Linked.Api.Identity;
using Linked.Core.Services;
using Linked.Core.ViewModels;

namespace Linked.Core.Tests;

public sealed class ShellViewModelTests
{
    [Test]
    public async Task SignIn_loads_the_member_and_raises_property_changes()
    {
        FakeSession session = new() { SignInResult = true };
        ShellViewModel viewModel = new(session, new FakeIdentity(new UserInfo("id", Name: "Ada Lovelace")));
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.SignInCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsSignedIn).IsTrue();
        await Assert.That(viewModel.IsSignedOut).IsFalse();
        await Assert.That(viewModel.DisplayName).IsEqualTo("Ada Lovelace");
        await Assert.That(changed).Contains(nameof(ShellViewModel.IsSignedOut));
    }

    [Test]
    public async Task SignIn_cancelled_by_the_member_leaves_the_shell_signed_out()
    {
        ShellViewModel viewModel = new(new FakeSession { SignInResult = false }, new FakeIdentity(new UserInfo("id")));

        await viewModel.SignInCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsSignedIn).IsFalse();
    }

    [Test]
    public async Task A_failed_profile_request_surfaces_an_error_message()
    {
        ShellViewModel viewModel = new(new FakeSession { SignInResult = true }, new FakeIdentity(null));

        await viewModel.SignInCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsSignedIn).IsFalse();
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
    }

    private sealed class FakeSession : ISessionService
    {
        public bool SignInResult { get; init; }

        public bool IsSignedIn { get; private set; }

        public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken) => new("token");

        public Task<bool> SignInAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = SignInResult;
            return Task.FromResult(SignInResult);
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdentity(UserInfo? member) : IIdentityApi
    {
        public Task<UserInfo> GetUserInfoAsync(CancellationToken cancellationToken)
            => member is null
                ? Task.FromException<UserInfo>(new HttpRequestException("offline"))
                : Task.FromResult(member);
    }
}
