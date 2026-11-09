using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Linked.Api.Auth;
using Linked.Core.Services;
using Refit;

namespace Linked.Core.ViewModels;

/// <summary>
/// Drives the GitHub device flow: ask for a code, show it, open the browser,
/// wait for approval, then hand the token to <see cref="Session"/>.
/// </summary>
public sealed partial class SignInViewModel(
    IGitHubDeviceFlowApi deviceFlow,
    Session session,
    GitHubOptions options,
    ILauncher launcher,
    TimeProvider time) : ObservableObject
{
    private Uri? _verificationUri;

    /// <summary>The code to type on github.com. Empty until the flow has started.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserCode))]
    public partial string UserCode { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public bool HasUserCode => UserCode.Length > 0;

    /// <summary>Runs the whole flow. Cancel with <c>SignInCancelCommand</c>.</summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            DeviceCode code = await deviceFlow.RequestCodeAsync(
                new DeviceCodeRequest { ClientId = options.ClientId, Scope = options.Scopes },
                cancellationToken);

            _verificationUri = new Uri(code.VerificationUri);
            UserCode = code.UserCode;
            launcher.CopyToClipboard(code.UserCode);
            await launcher.OpenAsync(_verificationUri);

            DeviceFlowResult result = await DeviceFlow.WaitForTokenAsync(deviceFlow, options.ClientId, code, time, cancellationToken);
            switch (result.Status)
            {
                case DeviceFlowStatus.Authorized:
                    session.SignIn(result.AccessToken!);
                    break;
                case DeviceFlowStatus.Denied:
                    ErrorMessage = "Sign-in was cancelled on github.com.";
                    break;
                default:
                    ErrorMessage = "The code expired. Try again.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The member pressed Cancel. Nothing to report.
        }
        catch (ApiExceptionBase ex)
        {
            ErrorMessage = ApiErrors.Describe(ex);
        }
        catch (InvalidOperationException ex)
        {
            // DeviceFlow throws this for an error code it does not know.
            ErrorMessage = ex.Message;
        }
        finally
        {
            UserCode = string.Empty;
        }
    }

    [RelayCommand]
    private Task OpenBrowserAsync() => _verificationUri is null ? Task.CompletedTask : launcher.OpenAsync(_verificationUri);
}
