using System.Net;
using Guit.Core.Models;
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

    [Test]
    public async Task LoadMore_appends_the_next_page_in_order()
    {
        FakeNotificationsApi api = new(Sample.Threads(50), Sample.Threads(3, firstId: 51));
        InboxViewModel viewModel = new(api, Time);
        await viewModel.LoadCommand.ExecuteAsync(null);
        await Assert.That(viewModel.HasMore).IsTrue();

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(53);
        await Assert.That(viewModel.Items[50].Id).IsEqualTo("51");
        await Assert.That(api.ListCalls[1].Page).IsEqualTo(2);
        await Assert.That(viewModel.HasMore).IsFalse();
    }

    [Test]
    public async Task LoadMore_does_not_call_the_api_after_a_short_page()
    {
        FakeNotificationsApi api = new(Sample.Threads(2));
        InboxViewModel viewModel = new(api, Time);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(api.ListCalls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_failed_LoadMore_keeps_the_items_and_retries_the_same_page()
    {
        FakeNotificationsApi api = new(Sample.Threads(50));
        InboxViewModel viewModel = new(api, Time);
        await viewModel.LoadCommand.ExecuteAsync(null);

        api.FailNextWith = HttpStatusCode.BadGateway;
        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        await Assert.That(viewModel.Items.Count).IsEqualTo(50);
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.IsLoadingMore).IsFalse();

        api.Enqueue(Sample.Threads(1, firstId: 51));
        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        await Assert.That(api.ListCalls[2].Page).IsEqualTo(2);
        await Assert.That(viewModel.Items.Count).IsEqualTo(51);
    }

    [Test]
    [Arguments(InboxFilter.Unread, false, false)]
    [Arguments(InboxFilter.All, true, false)]
    [Arguments(InboxFilter.Participating, false, true)]
    public async Task Each_filter_sends_its_query(InboxFilter filter, bool all, bool participating)
    {
        FakeNotificationsApi api = new(Sample.Threads(50), Sample.Threads(1, firstId: 51));
        InboxViewModel viewModel = new(api, Time);

        // Setting a different filter loads by itself; the default filter needs an explicit load.
        if (filter == InboxFilter.Unread)
        {
            await viewModel.LoadCommand.ExecuteAsync(null);
        }
        else
        {
            viewModel.Filter = filter;
        }

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(api.ListCalls[^2]).IsEqualTo((all, participating, 1, 50));
        await Assert.That(api.ListCalls[^1]).IsEqualTo((all, participating, 2, 50));
    }

    [Test]
    public async Task Changing_the_filter_replaces_the_items_and_restarts_paging()
    {
        FakeNotificationsApi api = new(Sample.Threads(50), Sample.Threads(50, firstId: 51), [Sample.Thread("900", "Read one", unread: false)]);
        InboxViewModel viewModel = new(api, Time);
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        viewModel.Filter = InboxFilter.All;

        await Assert.That(viewModel.Items.Count).IsEqualTo(1);
        await Assert.That(viewModel.Items[0].Id).IsEqualTo("900");
        await Assert.That(api.ListCalls[^1]).IsEqualTo((true, false, 1, 50));
        await Assert.That(viewModel.HasMore).IsFalse();
    }
}
