using System.IO;
using Yata.Helpers;

namespace Yata.Tests;

/// <summary>The local log: nothing personal in it, bounded in size and age, and crash files announced once.</summary>
public sealed class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yata-tests", Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 26, 15, 0, 0);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private LogWriter Writer(long maxBytes = 1 << 20) => new(_dir, () => _now, maxBytes);

    private string Today() => File.ReadAllText(Path.Combine(_dir, $"yata-{_now:yyyy-MM-dd}.log"));

    [Theory]
    [InlineData(@"Could not find file 'C:\Users\ariel\Documents\contrato.pdf'.", "contrato", "ariel")]
    [InlineData(@"Access to the path '\\servidor\pasta\nota.txt' is denied.", "servidor", "nota")]
    [InlineData("GET https://lrclib.net/api/search?track_name=Song&artist_name=Band failed", "Song", "Band")]
    [InlineData("Token for someone@example.com expired", "someone", "example.com")]
    public void Sanitize_blanks_paths_urls_and_emails(string message, string hidden1, string hidden2)
    {
        string clean = LogText.Sanitize(message);

        Assert.DoesNotContain(hidden1, clean);
        Assert.DoesNotContain(hidden2, clean);
    }

    [Fact]
    public void Sanitize_keeps_the_host_of_a_url_and_the_rest_of_the_message()
    {
        string clean = LogText.Sanitize("Request to https://api.open-meteo.com/v1/forecast?latitude=-22.9 timed out");

        Assert.Equal("Request to https://api.open-meteo.com/… timed out", clean);
    }

    [Fact]
    public void Exceptions_are_written_with_type_and_cleaned_message()
    {
        var writer = Writer();
        writer.Write(LogLevel.Warn, "store", "Falha ao gravar", new IOException(@"Disk full writing C:\Users\ariel\x.json"));
        Assert.True(writer.Flush(TimeSpan.FromSeconds(5)));

        string log = Today();
        Assert.Contains("WARN  [store] Falha ao gravar", log);
        Assert.Contains("System.IO.IOException", log);
        Assert.DoesNotContain("ariel", log);
    }

    [Fact]
    public void Stops_at_the_daily_limit_with_one_note()
    {
        var writer = Writer(maxBytes: 600);
        for (int i = 0; i < 50; i++)
            writer.Write(LogLevel.Info, "test", $"linha {i}");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(5)));

        string log = Today();
        Assert.True(new FileInfo(Path.Combine(_dir, $"yata-{_now:yyyy-MM-dd}.log")).Length < 800);
        Assert.Single(log.Split('\n'), l => l.Contains("limite diário"));
        Assert.DoesNotContain("linha 49", log);
    }

    [Fact]
    public void A_new_day_starts_a_new_file()
    {
        var writer = Writer();
        writer.Write(LogLevel.Info, "test", "ontem");
        _now = _now.AddDays(1);
        writer.Write(LogLevel.Info, "test", "hoje");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(5)));

        Assert.Equal(2, Directory.GetFiles(_dir, "yata-*.log").Length);
        Assert.Contains("hoje", Today());
    }

    [Fact]
    public void Files_older_than_a_week_are_removed()
    {
        Directory.CreateDirectory(_dir);
        string old = Path.Combine(_dir, "yata-2026-10-01.log");
        string recent = Path.Combine(_dir, "yata-2026-10-24.log");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.SetLastWriteTime(old, _now.AddDays(-25));
        File.SetLastWriteTime(recent, _now.AddDays(-2));

        var writer = Writer();
        writer.Write(LogLevel.Info, "test", "oi");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(5)));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void A_crash_file_carries_the_error_and_the_lines_before_it_once()
    {
        var writer = Writer();
        writer.Write(LogLevel.Info, "app", "antes da queda");

        string? first = writer.Crash(new InvalidOperationException("quebrou"));
        string? second = writer.Crash(new InvalidOperationException("de novo"));

        Assert.NotNull(first);
        Assert.Null(second);
        string text = File.ReadAllText(first!);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("antes da queda", text);
    }

    [Fact]
    public void An_unseen_crash_is_announced_only_once()
    {
        var writer = Writer();
        string crash = writer.Crash(new InvalidOperationException("quebrou"))!;

        Assert.Equal(crash, writer.TakeUnseenCrash());
        Assert.Null(writer.TakeUnseenCrash());
        Assert.Null(new LogWriter(_dir, () => _now).TakeUnseenCrash());
    }

    [Fact]
    public void Error_burst_allows_the_limit_and_forgets_old_errors()
    {
        var now = new DateTime(2026, 10, 26, 15, 0, 0);
        var burst = new ErrorBurst(limit: 2, window: TimeSpan.FromMinutes(1), () => now);

        Assert.True(burst.Record());
        Assert.True(burst.Record());
        Assert.False(burst.Record());

        now = now.AddMinutes(2);
        Assert.True(burst.Record());
    }
}
