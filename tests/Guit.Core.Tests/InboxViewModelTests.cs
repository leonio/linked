using System.Net;
using Guit.Core.Models;
using Guit.Core.Services;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class InboxViewModelTests
{
    private static readonly FixedTime Time = new(Sample.Now);

    private static InboxViewModel Create(FakeNotificationsApi api, FakeLauncher? launcher = null, FakeNavigator? navigator = null)
        => new(api, navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher(), new ImmediateDispatcher(), Time);

    [Test]
    public async Task Load_fills_the_items_in_order()
    {
        FakeNotificationsApi api = new([Sample.Thread("1", "First"), Sample.Thread("2", "Second")]);
        InboxViewModel viewModel = Create(api);

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
        InboxViewModel viewModel = Create(new FakeNotificationsApi(Sample.Threads(2), Sample.Threads(2)));

        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_failed_load_sets_the_error_and_stops_loading()
    {
        InboxViewModel viewModel = Create(new FakeNotificationsApi { FailNextWith = HttpStatusCode.BadGateway });

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("GitHub is having problems. Try again shortly.");
        await Assert.That(viewModel.Items.Count).IsEqualTo(0);
        await Assert.That(viewModel.IsLoading).IsFalse();
        await Assert.That(viewModel.IsEmpty).IsFalse();
    }

    [Test]
    public async Task An_empty_reply_is_reported_as_empty()
    {
        InboxViewModel viewModel = Create(new FakeNotificationsApi());
        await Assert.That(viewModel.IsEmpty).IsTrue();

        await viewModel.LoadCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsEmpty).IsTrue();
        await Assert.That(viewModel.ErrorMessage).IsNull();
    }

    [Test]
    public async Task LoadMore_appends_the_next_page_in_order()
    {
        FakeNotificationsApi api = new(Sample.Threads(50), Sample.Threads(3, firstId: 51));
        InboxViewModel viewModel = Create(api);
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
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(api.ListCalls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_failed_LoadMore_keeps_the_items_and_retries_the_same_page()
    {
        FakeNotificationsApi api = new(Sample.Threads(50));
        InboxViewModel viewModel = Create(api);
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
        InboxViewModel viewModel = Create(api);

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
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        viewModel.Filter = InboxFilter.All;

        await Assert.That(viewModel.Items.Count).IsEqualTo(1);
        await Assert.That(viewModel.Items[0].Id).IsEqualTo("900");
        await Assert.That(api.ListCalls[^1]).IsEqualTo((true, false, 1, 50));
        await Assert.That(viewModel.HasMore).IsFalse();
    }

    [Test]
    public async Task A_newer_load_discards_the_reply_of_an_older_one()
    {
        TaskCompletionSource slow = new();
        FakeNotificationsApi api = new([Sample.Thread("1", "Stale unread")], [Sample.Thread("2", "Fresh all")]) { HoldNextList = slow };
        InboxViewModel viewModel = Create(api);

        Task first = viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Filter = InboxFilter.All;
        slow.SetResult();
        await first;

        await Assert.That(viewModel.Items.Count).IsEqualTo(1);
        await Assert.That(viewModel.Items[0].Title).IsEqualTo("Fresh all");
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task MarkAsRead_updates_the_row_and_removes_it_from_the_unread_view()
    {
        FakeNotificationsApi api = new(Sample.Threads(2));
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);
        NotificationItem item = viewModel.Items[0];

        await viewModel.MarkAsReadCommand.ExecuteAsync(item);

        await Assert.That(item.IsUnread).IsFalse();
        await Assert.That(api.MarkedRead).Contains("1");
        await Assert.That(viewModel.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MarkAsRead_keeps_the_row_when_showing_all()
    {
        FakeNotificationsApi api = new(Sample.Threads(2));
        InboxViewModel viewModel = Create(api);
        viewModel.Filter = InboxFilter.All;

        await viewModel.MarkAsReadCommand.ExecuteAsync(viewModel.Items[0]);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
        await Assert.That(viewModel.Items[0].IsUnread).IsFalse();
    }

    [Test]
    public async Task A_failed_MarkAsRead_puts_the_unread_state_back()
    {
        FakeNotificationsApi api = new(Sample.Threads(1));
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);
        api.FailNextWith = HttpStatusCode.BadGateway;

        await viewModel.MarkAsReadCommand.ExecuteAsync(viewModel.Items[0]);

        await Assert.That(viewModel.Items[0].IsUnread).IsTrue();
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task MarkAsDone_removes_the_row()
    {
        FakeNotificationsApi api = new(Sample.Threads(3));
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkAsDoneCommand.ExecuteAsync(viewModel.Items[1]);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
        await Assert.That(viewModel.Items[1].Id).IsEqualTo("3");
        await Assert.That(api.MarkedDone).Contains("2");
    }

    [Test]
    public async Task A_failed_MarkAsDone_restores_the_row_at_its_original_position()
    {
        FakeNotificationsApi api = new(Sample.Threads(3));
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);
        api.FailNextWith = HttpStatusCode.BadGateway;

        await viewModel.MarkAsDoneCommand.ExecuteAsync(viewModel.Items[1]);

        await Assert.That(viewModel.Items.Count).IsEqualTo(3);
        await Assert.That(viewModel.Items[1].Id).IsEqualTo("2");
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task Open_navigates_to_the_conversation_and_marks_the_row_as_read()
    {
        FakeNotificationsApi api = new(Sample.Threads(1));
        FakeNavigator navigator = new();
        InboxViewModel viewModel = Create(api, navigator: navigator);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.OpenCommand.ExecuteAsync(viewModel.Items[0]);

        await Assert.That(navigator.Navigations[0]).IsEqualTo((Route.Conversation, (string?)"octocat/hello!7"));
        await Assert.That(api.MarkedRead).Contains("1");
    }

    [Test]
    public async Task Open_falls_back_to_the_repository_for_other_kinds()
    {
        FakeNotificationsApi api = new([Sample.Thread("1", type: "CheckSuite", url: null)]);
        FakeLauncher launcher = new();
        InboxViewModel viewModel = Create(api, launcher);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.OpenCommand.ExecuteAsync(viewModel.Items[0]);

        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/octocat/hello"));
    }

    [Test]
    public async Task Refresh_with_unchanged_data_keeps_the_same_rows()
    {
        FakeNotificationsApi api = new(Sample.Threads(3), Sample.Threads(3));
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);
        NotificationItem first = viewModel.Items[0];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        await Assert.That(ReferenceEquals(viewModel.Items[0], first)).IsTrue();
        await Assert.That(api.ListCalls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Refresh_with_a_new_notification_replaces_the_rows()
    {
        FakeNotificationsApi api = new(Sample.Threads(2), [Sample.Thread("9", "New"), .. Sample.Threads(2)]);
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(3);
        await Assert.That(viewModel.Items[0].Title).IsEqualTo("New");
    }

    [Test]
    public async Task Refresh_notices_a_row_that_was_read_elsewhere()
    {
        FakeNotificationsApi api = new(Sample.Threads(1), [Sample.Thread("1", unread: false)]);
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items[0].IsUnread).IsFalse();
    }

    [Test]
    public async Task A_failed_refresh_keeps_the_rows_and_shows_no_error()
    {
        FakeNotificationsApi api = new(Sample.Threads(2));
        InboxViewModel viewModel = Create(api);
        await viewModel.LoadCommand.ExecuteAsync(null);
        api.FailNextWith = HttpStatusCode.BadGateway;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Items.Count).IsEqualTo(2);
        await Assert.That(viewModel.ErrorMessage).IsNull();
    }
}
