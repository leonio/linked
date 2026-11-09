using Triage.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Triage.App.Services;

internal sealed class Launcher : ILauncher
{
    public async Task OpenAsync(Uri uri) => await Windows.System.Launcher.LaunchUriAsync(uri);

    public void CopyToClipboard(string text)
    {
        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
