using Guit.Api.Pulls;
using Guit.Core.Models;

namespace Guit.Core.Tests;

/// <summary>ListView reads each row to a screen reader through ToString, so these strings are user-facing.</summary>
public sealed class AccessibleNameTests
{
    [Test]
    public async Task Rows_describe_themselves()
    {
        await Assert.That(NotificationItem.From(Sample.Thread("1"), Sample.Now).ToString())
            .IsEqualTo("Fix it, octocat/hello, review requested, 3h, unread");
        await Assert.That(PullRequestItem.From(Sample.Found(7, draft: true), Sample.Now).ToString())
            .IsEqualTo("Fix it, octocat/hello #7, by octocat, 3h, draft");
        await Assert.That(RunItem.From(Sample.Run(12, "completed", "failure"), Sample.Now).ToString())
            .IsEqualTo("Fix it, failure, CI #12 · push · main, 3h");
        await Assert.That(JobItem.From(Sample.Job(1, seconds: 125)).ToString()).IsEqualTo("Job build, success, 2m 05s");
        await Assert.That(JobItem.FromStep(Sample.Job(1), Sample.Step(2, "Build", "in_progress", null, null)).ToString())
            .IsEqualTo("Step Build, running");
        await Assert.That(CommentItem.From(Sample.Comment(11), Sample.Now).ToString()).IsEqualTo("Comment by octocat, 3h. Looks good");
    }

    [Test]
    public async Task Diff_rows_say_what_changed()
    {
        List<DiffRow> rows = [];
        DiffRow.AppendFile(rows, new PullFile("src/a.cs", "modified", 1, 1, "@@ -4 +4 @@\n-old\n+new\n same"));

        await Assert.That(rows[0].ToString()).IsEqualTo("File src/a.cs, modified · +1 −1");
        await Assert.That(rows[2].ToString()).IsEqualTo("Removed, line 4: old");
        await Assert.That(rows[3].ToString()).IsEqualTo("Added, line 4: new");
        await Assert.That(rows[4].ToString()).IsEqualTo("Line 5: same");
    }
}
