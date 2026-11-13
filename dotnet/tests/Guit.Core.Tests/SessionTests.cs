using Guit.Core.Services;

namespace Guit.Core.Tests;

public sealed class SessionTests
{
    [Test]
    public async Task OnUnauthorized_clears_the_token_and_raises_Changed_once()
    {
        FakeTokenStore store = new("gho_abc");
        Session session = new(store, new ImmediateDispatcher());
        int changed = 0;
        session.Changed += (_, _) => changed++;

        session.OnUnauthorized();
        session.OnUnauthorized();

        await Assert.That(session.IsSignedIn).IsFalse();
        await Assert.That(store.Token).IsNull();
        await Assert.That(changed).IsEqualTo(1);
    }

    [Test]
    public async Task OnUnauthorized_posts_the_sign_out_instead_of_running_it_on_the_calling_thread()
    {
        QueuedDispatcher dispatcher = new();
        Session session = new(new FakeTokenStore("gho_abc"), dispatcher);

        session.OnUnauthorized();
        await Assert.That(session.IsSignedIn).IsTrue();

        dispatcher.RunAll();
        await Assert.That(session.IsSignedIn).IsFalse();
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = new();

        public void Post(Action action) => _actions.Enqueue(action);

        public void RunAll()
        {
            while (_actions.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
