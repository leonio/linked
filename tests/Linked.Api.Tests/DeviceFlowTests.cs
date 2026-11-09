using Linked.Api.Auth;
using Refit.Testing;

namespace Linked.Api.Tests;

public sealed class DeviceFlowTests
{
    private static readonly DeviceCode Code = new("dev", "WDJB-MJHT", "https://github.com/login/device", ExpiresIn: 900, Interval: 0);

    [Test]
    public async Task RequestCode_reads_the_snake_case_reply()
    {
        using StubHttp http = new()
        {
            {
                Route.Post("/login/device/code"),
                Reply.Json("""{"device_code":"dev","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""")
            },
        };
        IGitHubDeviceFlowApi api = Stub.Client<IGitHubDeviceFlowApi>(http);

        DeviceCode code = await api.RequestCodeAsync(new DeviceCodeRequest { ClientId = "id", Scope = "repo" }, CancellationToken.None);

        await Assert.That(code.Code).IsEqualTo("dev");
        await Assert.That(code.UserCode).IsEqualTo("WDJB-MJHT");
        await Assert.That(code.Interval).IsEqualTo(5);
        http.VerifyAllCalled();
    }

    [Test]
    public async Task PollToken_sends_the_device_grant_as_a_form()
    {
        using RecordingHandler http = new("""{"error":"authorization_pending"}""");
        IGitHubDeviceFlowApi api = Stub.Client<IGitHubDeviceFlowApi>(http);

        DeviceTokenResponse reply = await api.PollTokenAsync(new DeviceTokenRequest { ClientId = "id", DeviceCode = "dev" }, CancellationToken.None);

        await Assert.That(reply.Error).IsEqualTo("authorization_pending");
        await Assert.That(http.Path).IsEqualTo("/login/oauth/access_token");
        await Assert.That(http.Body).Contains("client_id=id");
        await Assert.That(http.Body).Contains("device_code=dev");
        await Assert.That(http.Body).Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code");
    }

    [Test]
    public async Task WaitForToken_keeps_polling_through_pending_and_slow_down()
    {
        using StubHttp http = new()
        {
            { Route.Post("/login/oauth/access_token"), Reply.Json("""{"error":"authorization_pending"}""") },
            { Route.Post("/login/oauth/access_token"), Reply.Json("""{"error":"slow_down","interval":0}""") },
            { Route.Post("/login/oauth/access_token"), Reply.Json("""{"access_token":"gho_abc","token_type":"bearer","scope":"repo"}""") },
        };

        DeviceFlowResult result = await DeviceFlow.WaitForTokenAsync(
            Stub.Client<IGitHubDeviceFlowApi>(http), "id", Code, TimeProvider.System, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DeviceFlowStatus.Authorized);
        await Assert.That(result.AccessToken).IsEqualTo("gho_abc");
        http.VerifyAllCalled();
    }

    [Test]
    [Arguments("access_denied", DeviceFlowStatus.Denied)]
    [Arguments("expired_token", DeviceFlowStatus.Expired)]
    public async Task WaitForToken_maps_terminal_errors(string error, DeviceFlowStatus expected)
    {
        using StubHttp http = new()
        {
            { Route.Post("/login/oauth/access_token"), Reply.Json($$"""{"error":"{{error}}"}""") },
        };

        DeviceFlowResult result = await DeviceFlow.WaitForTokenAsync(
            Stub.Client<IGitHubDeviceFlowApi>(http), "id", Code, TimeProvider.System, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(expected);
        await Assert.That(result.AccessToken).IsNull();
    }
}
