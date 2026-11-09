using Linked.Api.Auth;
using Linked.Core.Services;
using Linked.Core.ViewModels;

namespace Linked.Core.Tests;

public sealed class SignInViewModelTests
{
    private static readonly GitHubOptions Options = new("client-id");

    [Test]
    public async Task Approval_stores_the_token_and_opens_the_browser_with_the_code_on_the_clipboard()
    {
        FakeTokenStore store = new();
        Session session = new(store);
        FakeLauncher launcher = new();
        SignInViewModel viewModel = new(
            new FakeDeviceFlowApi(new DeviceTokenResponse(Error: "authorization_pending"), new DeviceTokenResponse(AccessToken: "gho_abc")),
            session, Options, launcher, TimeProvider.System);

        await viewModel.SignInCommand.ExecuteAsync(null);

        await Assert.That(session.IsSignedIn).IsTrue();
        await Assert.That(store.Token).IsEqualTo("gho_abc");
        await Assert.That(launcher.Clipboard).IsEqualTo("WDJB-MJHT");
        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/login/device"));
        await Assert.That(viewModel.ErrorMessage).IsNull();
        await Assert.That(viewModel.HasUserCode).IsFalse();
    }

    [Test]
    public async Task Denial_leaves_the_session_signed_out_and_explains_why()
    {
        Session session = new(new FakeTokenStore());
        SignInViewModel viewModel = new(
            new FakeDeviceFlowApi(new DeviceTokenResponse(Error: "access_denied")),
            session, Options, new FakeLauncher(), TimeProvider.System);

        await viewModel.SignInCommand.ExecuteAsync(null);

        await Assert.That(session.IsSignedIn).IsFalse();
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
    }
}
