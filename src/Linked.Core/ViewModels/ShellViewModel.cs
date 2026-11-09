using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Linked.Api.Identity;
using Linked.Core.Services;

namespace Linked.Core.ViewModels;

/// <summary>State for the main window: who is signed in, and the sign-in / sign-out commands.</summary>
public sealed partial class ShellViewModel(ISessionService session, IIdentityApi identity) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    public partial bool IsSignedIn { get; private set; } = session.IsSignedIn;

    [ObservableProperty]
    public partial string DisplayName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? PictureUrl { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public bool IsSignedOut => !IsSignedIn;

    /// <summary>Call once when the window opens. Restores the member if a session already exists.</summary>
    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (session.IsSignedIn)
        {
            await LoadMemberAsync(cancellationToken);
        }
    }

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        if (await session.SignInAsync(cancellationToken))
        {
            await LoadMemberAsync(cancellationToken);
        }
    }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await session.SignOutAsync(cancellationToken);
        IsSignedIn = false;
        DisplayName = string.Empty;
        PictureUrl = null;
    }

    private async Task LoadMemberAsync(CancellationToken cancellationToken)
    {
        try
        {
            UserInfo member = await identity.GetUserInfoAsync(cancellationToken);
            DisplayName = member.Name ?? string.Empty;
            PictureUrl = member.Picture;
            IsSignedIn = true;
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
