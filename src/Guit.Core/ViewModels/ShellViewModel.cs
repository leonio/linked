using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guit.Api.Users;
using Guit.Core.Services;
using Refit;

namespace Guit.Core.ViewModels;

/// <summary>State for the main window: whether someone is signed in, and who.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Session _session;
    private readonly IUsersApi _users;

    public ShellViewModel(Session session, IUsersApi users)
    {
        _session = session;
        _users = users;
        IsSignedIn = session.IsSignedIn;
        session.Changed += OnSessionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    public partial bool IsSignedIn { get; private set; }

    [ObservableProperty]
    public partial string Login { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? AvatarUrl { get; private set; }

    public bool IsSignedOut => !IsSignedIn;

    /// <summary>Loads the signed-in user. Run once when the window opens, and again after each sign-in.</summary>
    [RelayCommand]
    private async Task LoadUserAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsSignedIn)
        {
            return;
        }

        try
        {
            GitHubUser user = await _users.GetAuthenticatedAsync(cancellationToken);
            Login = user.Login;
            AvatarUrl = user.AvatarUrl;
        }
        catch (ApiExceptionBase)
        {
            // Offline or rate limited: the shell still works, it just has no name to show yet.
        }
    }

    [RelayCommand]
    private void SignOut() => _session.SignOut();

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        IsSignedIn = _session.IsSignedIn;
        if (IsSignedIn)
        {
            LoadUserCommand.Execute(null);
        }
        else
        {
            Login = string.Empty;
            AvatarUrl = null;
        }
    }
}
