using Guit.Core.Services;

namespace Guit.Core.Tests;

public sealed class PerfTraceTests
{
    [Test]
    public async Task Append_writes_one_line_of_name_and_unix_milliseconds()
    {
        string path = Path.Combine(Path.GetTempPath(), $"guit-trace-{Guid.NewGuid():N}.txt");
        try
        {
            PerfTrace.Append(path, "inbox", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123));
            PerfTrace.Append(path, "inbox", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_456));

            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("inbox 1700000000123\ninbox 1700000000456\n");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task A_path_that_cannot_be_written_is_ignored()
    {
        string path = Path.Combine(Path.GetTempPath(), $"no-such-folder-{Guid.NewGuid():N}", "trace.txt");

        PerfTrace.Append(path, "inbox", DateTimeOffset.UtcNow);

        await Assert.That(File.Exists(path)).IsFalse();
    }
}
