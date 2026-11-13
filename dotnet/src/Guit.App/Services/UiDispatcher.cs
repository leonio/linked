using Guit.Core.Services;
using Microsoft.UI.Dispatching;

namespace Guit.App.Services;

/// <summary>Posts work to the UI thread's dispatcher queue. Create it on the UI thread.</summary>
internal sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();

    public void Post(Action action) => _queue.TryEnqueue(() => action());
}
