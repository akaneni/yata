using Yata.Services;

namespace Yata.Tests;

/// <summary>Version comparison and announcing a new version once. No network involved.</summary>
public class UpdateCheckerTests
{
    private static readonly Version Current = new(0, 1, 0);

    [Theory]
    [InlineData("v0.2.0", true)]
    [InlineData("0.1.1", true)]
    [InlineData("V1.0", true)]
    [InlineData("v0.1.0", false)]
    [InlineData("0.0.9", false)]
    [InlineData("nightly", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_higher_parseable_version_is_newer(string? tag, bool newer)
    {
        Assert.Equal(newer, UpdateChecker.IsNewer(tag, Current));
    }

    [Fact]
    public async Task Does_nothing_while_switched_off()
    {
        var store = AppStore.InMemory(); // UpdateCheckEnabled is off by default
        var checker = new UpdateChecker(store);

        await checker.CheckIfDueAsync();

        Assert.Null(store.Data.Updates.LastCheck);
    }

    [Fact]
    public async Task Waits_a_day_between_checks()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0);
        var store = AppStore.InMemory();
        store.Settings.UpdateCheckEnabled = true;
        store.Data.Updates.LastCheck = now.AddHours(-23);
        var checker = new UpdateChecker(store, () => now);

        await checker.CheckIfDueAsync(); // returns before any request

        Assert.Equal(now.AddHours(-23), store.Data.Updates.LastCheck);
    }

    [Fact]
    public async Task A_check_without_an_answer_doesnt_count_as_the_days_check()
    {
        var now = new DateTime(2026, 10, 26, 8, 0, 0);
        var store = AppStore.InMemory();
        store.Settings.UpdateCheckEnabled = true;
        var checker = new UpdateChecker(store, () => now, () => Task.FromResult<string?>(null));

        await checker.CheckIfDueAsync(); // offline

        Assert.Null(store.Data.Updates.LastCheck);
    }

    [Fact]
    public void Announces_a_new_version_once()
    {
        var store = AppStore.InMemory();
        store.Data.Updates.Latest = "99.0.0";
        var checker = new UpdateChecker(store);
        var messages = new List<HudMessage>();
        checker.Message += messages.Add;

        checker.Announce();
        checker.Announce();

        var message = Assert.Single(messages);
        Assert.Equal("99.0.0", message.Value);
        Assert.Equal("99.0.0", checker.Available);
    }
}
