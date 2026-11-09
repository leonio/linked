using Triage.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Triage.App.Views;

public sealed partial class SignInView : UserControl
{
    public SignInView()
    {
        ViewModel = App.Services.GetRequiredService<SignInViewModel>();
        InitializeComponent();
    }

    public SignInViewModel ViewModel { get; }
}
