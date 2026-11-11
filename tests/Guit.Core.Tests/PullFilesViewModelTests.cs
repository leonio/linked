using System.Net;
using Guit.Api.Pulls;
using Guit.Core.Models;
using Guit.Core.ViewModels;

namespace Guit.Core.Tests;

public sealed class PullFilesViewModelTests
{
    private static PullFilesViewModel Create(FakePullsApi pulls, FakeNavigator? navigator = null, FakeLauncher? launcher = null)
        => new(pulls, navigator ?? new FakeNavigator(), launcher ?? new FakeLauncher());

    /// <summary>One row as "K old new|text", for comparing whole diffs at a glance.</summary>
    private static string Dump(IEnumerable<DiffRow> rows)
        => string.Join("\n", rows.Select(row => $"{row.Kind.ToString()[0]} {row.OldNumber,2} {row.NewNumber,2}|{row.Text}{(row.Detail.Length > 0 ? " [" + row.Detail + "]" : "")}"));

    private static PullFile File(string name, string patch = "@@ -1 +1 @@\n-a\n+b") => new(name, "modified", 1, 1, patch);

    [Test]
    public async Task A_patch_becomes_numbered_rows()
    {
        PullFile file = new("src/a.cs", "modified", 2, 1, "@@ -10,4 +10,5 @@ class A\n one\n-two\n+2\n+2b\n \tthree\n\\ No newline at end of file\n@@ -40,2 +41,2 @@\n x\n y");
        PullFilesViewModel viewModel = Create(new FakePullsApi().WithFiles([file]));

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(Dump(viewModel.Rows)).IsEqualTo(
            "F      |src/a.cs [modified · +2 −1]\n" +
            "H      |@@ -10,4 +10,5 @@ class A\n" +
            "C 10 10|one\n" +
            "R 11   |two\n" +
            "A    11|2\n" +
            "A    12|2b\n" +
            "C 12 13|    three\n" +
            "N      |No newline at end of file\n" +
            "H      |@@ -40,2 +41,2 @@\n" +
            "C 40 41|x\n" +
            "C 41 42|y");
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello #7 · 1 file");
        await Assert.That(viewModel.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Renames_and_files_without_a_patch_get_a_heading_and_a_note()
    {
        PullFilesViewModel viewModel = Create(new FakePullsApi().WithFiles(
        [
            new PullFile("src/new.cs", "renamed", 0, 0, null, "src/old.cs"),
            new PullFile("logo.png", "added", 0, 0),
            new PullFile("big.json", "modified", 9000, 100),
        ]));

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");

        await Assert.That(Dump(viewModel.Rows)).IsEqualTo(
            "F      |src/old.cs → src/new.cs [renamed · +0 −0]\n" +
            "N      |No text changes.\n" +
            "F      |logo.png [added · +0 −0]\n" +
            "N      |No text changes.\n" +
            "F      |big.json [modified · +9000 −100]\n" +
            "N      |Diff not available (binary or too large). Open on GitHub to see it.");
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello #7 · 3 files");
    }

    [Test]
    public async Task LoadMore_appends_the_next_hundred_files()
    {
        PullFile[] first = [.. Enumerable.Range(1, 100).Select(i => File($"f{i}.cs"))];
        FakePullsApi pulls = new FakePullsApi().WithFiles(first, [File("last.cs")]);
        PullFilesViewModel viewModel = Create(pulls);
        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");
        await Assert.That(viewModel.HasMore).IsTrue();
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello #7 · 100+ files");

        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(viewModel.Rows.Count(row => row.Kind == DiffRowKind.File)).IsEqualTo(101);
        await Assert.That(viewModel.Rows[^4].Text).IsEqualTo("last.cs");
        await Assert.That(pulls.FileCalls.Count).IsEqualTo(2);
        await Assert.That(pulls.FileCalls[1]).IsEqualTo(("octocat", "hello", 7, 2));
        await Assert.That(viewModel.Subtitle).IsEqualTo("octocat/hello #7 · 101 files");
    }

    [Test]
    public async Task Failures_and_bad_links_are_reported()
    {
        PullFilesViewModel failing = Create(new FakePullsApi { FailNextWith = HttpStatusCode.NotFound });
        await failing.LoadCommand.ExecuteAsync("octocat/hello!7");
        await Assert.That(failing.ErrorMessage).IsNotNull();
        await Assert.That(failing.IsLoading).IsFalse();

        FakePullsApi pulls = new();
        PullFilesViewModel bad = Create(pulls);
        await bad.LoadCommand.ExecuteAsync("nope");
        await Assert.That(bad.ErrorMessage).IsEqualTo("This link is not valid.");
        await Assert.That(pulls.FileCalls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_pull_request_with_no_files_is_empty_and_the_page_opens_on_github()
    {
        FakeLauncher launcher = new();
        PullFilesViewModel viewModel = Create(new FakePullsApi(), launcher: launcher);

        await viewModel.LoadCommand.ExecuteAsync("octocat/hello!7");
        await viewModel.OpenInBrowserCommand.ExecuteAsync(null);

        await Assert.That(viewModel.IsEmpty).IsTrue();
        await Assert.That(launcher.Opened).IsEqualTo(new Uri("https://github.com/octocat/hello/pull/7/files"));
    }
}
