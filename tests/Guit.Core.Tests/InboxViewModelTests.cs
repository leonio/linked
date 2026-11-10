using System.Net;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class InboxViewModelTests
{
    private static readonly FixedTime Time = new(Sample.Now);

    [Test]
    public async Task Load_fills_the_items_in_order()
    {
        FakeNotificationsApi api = new([Sample.Thread("1", "First"), Sample.Thread("2", "Second")]);
        InboxViewModel viewModel = new(api, Time);

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
        await Assert.That(viewModel.Items[0].Title).IsEqualTo("First");
        await Assert.That(viewModel.Items[1].Updated).IsEqualTo("3h");
        await Assert.That(viewModel.IsLoading).IsFalse();
        await Assert.That(viewModel.IsEmpty).IsFalse();
        await Assert.That(api.ListCalls[0]).IsEqualTo((false, false, 1, 50));
    }

    [Test]
    public async Task Loading_twice_does_not_duplicate_items()
    {
        InboxViewModel viewModel = new(new FakeNotificationsApi(Sample.Threads(2), Sample.Threads(2)), Time);

        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_failed_load_sets_the_error_and_stops_loading()
    {
        InboxViewModel viewModel = new(new FakeNotificationsApi { FailNextWith = HttpStatusCode.BadGateway }, Time);

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("GitHub is having problems. Try again shortly.");
        await Assert.That(viewModel.Items.Count).IsEqualTo(0);
        await Assert.That(viewModel.IsLoading).IsFalse();
        await Assert.That(viewModel.IsEmpty).IsFalse();
    }

    [Test]
    public async Task An_empty_reply_is_reported_as_empty()
    {
        InboxViewModel viewModel = new(new FakeNotificationsApi(), Time);
        await Assert.That(viewModel.IsEmpty).IsTrue();

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsEmpty).IsTrue();
        await Assert.That(viewModel.ErrorMessage).IsNull();
    }
}
