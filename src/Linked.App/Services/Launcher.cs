using Linked.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Linked.App.Services;

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
