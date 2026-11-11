using Guit.Core.Text;

namespace Guit.Core.Tests;

public sealed class ActionsLogTests
{
    [Test]
    public async Task Timestamps_colour_codes_and_workflow_commands_are_cleaned()
    {
        string raw =
            "﻿2026-01-02T00:00:10.1234567Z ##[group]Run dotnet build\n" +
            "2026-01-02T00:00:11.1234567Z \u001B[36;1mdotnet build\u001B[0m\r\n" +
            "2026-01-02T00:00:12.1234567Z ##[endgroup]\n" +
            "2026-01-02T00:00:13.1234567Z ##[error]Build failed\n" +
            "2026-01-02T00:00:14.1234567Z ##[warning]Old node\n" +
            "no timestamp here\n";

        string[] lines = ActionsLog.ToLines(raw, 100, out bool truncated);

        await Assert.That(truncated).IsFalse();
        await Assert.That(string.Join("|", lines)).IsEqualTo("▸ Run dotnet build|dotnet build|✖ Build failed|⚠ Old node|no timestamp here");
    }

    [Test]
    public async Task Only_the_last_lines_are_kept()
    {
        string raw = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i}"));

        string[] lines = ActionsLog.ToLines(raw, 3, out bool truncated);

        await Assert.That(truncated).IsTrue();
        await Assert.That(string.Join("|", lines)).IsEqualTo("line 8|line 9|line 10");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task No_log_gives_no_lines(string? raw)
    {
        await Assert.That(ActionsLog.ToLines(raw, 10, out _).Length).IsEqualTo(0);
    }
}
