namespace Guit.Api.Auth;

public enum DeviceFlowStatus
{
    Authorized,
    Denied,
    Expired,
}

public readonly record struct DeviceFlowResult(DeviceFlowStatus Status, string? AccessToken = null);

public static class DeviceFlow
{
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Polls until the member approves or denies the request in their browser, or the code expires.
    /// Honours the server's polling interval, including <c>slow_down</c>.
    /// </summary>
    public static async Task<DeviceFlowResult> WaitForTokenAsync(
        IGitHubDeviceFlowApi api,
        string clientId,
        DeviceCode code,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(time);

        DeviceTokenRequest request = new() { ClientId = clientId, DeviceCode = code.Code };
        TimeSpan interval = TimeSpan.FromSeconds(code.Interval);
        TimeSpan lifetime = TimeSpan.FromSeconds(code.ExpiresIn);
        long started = time.GetTimestamp();

        while (true)
        {
            await Task.Delay(interval, time, cancellationToken);
            if (time.GetElapsedTime(started) > lifetime)
            {
                return new DeviceFlowResult(DeviceFlowStatus.Expired);
            }

            DeviceTokenResponse reply = await api.PollTokenAsync(request, cancellationToken);
            if (!string.IsNullOrEmpty(reply.AccessToken))
            {
                return new DeviceFlowResult(DeviceFlowStatus.Authorized, reply.AccessToken);
            }

            switch (reply.Error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval = reply.Interval is int seconds ? TimeSpan.FromSeconds(seconds) : interval + SlowDownStep;
                    break;
                case "access_denied":
                    return new DeviceFlowResult(DeviceFlowStatus.Denied);
                case "expired_token":
                    return new DeviceFlowResult(DeviceFlowStatus.Expired);
                default:
                    throw new InvalidOperationException(reply.ErrorDescription ?? reply.Error ?? "The device flow reply had neither a token nor an error.");
            }
        }
    }
}
