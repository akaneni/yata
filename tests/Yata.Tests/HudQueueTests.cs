using Yata.Services;

namespace Yata.Tests;

/// <summary>Which notice the closed notch shows, and that reminders are never lost.</summary>
public class HudQueueTests
{
    private static HudMessage Reminder(string text) => new("", text, Important: true);
    private static HudMessage Blip(string text) => new("", text, Kind: HudKind.Volume);

    [Fact]
    public void Reminders_that_arrive_while_hidden_all_wait_their_turn()
    {
        var queue = new HudQueue();
        Assert.Null(queue.Offer(Reminder("Water"), canShow: false));
        Assert.Null(queue.Offer(Reminder("Call Ana"), canShow: false));
        Assert.Null(queue.Offer(Blip("Volume 40%"), canShow: false));

        Assert.Equal("Water", queue.Resume()?.Text);
        Assert.Equal("Call Ana", queue.Finish()?.Text);
        Assert.Null(queue.Finish());
    }

    [Fact]
    public void A_reminder_doesnt_cut_another_one_short()
    {
        var queue = new HudQueue();
        Assert.Equal("Water", queue.Offer(Reminder("Water"), canShow: true)?.Text);
        Assert.Null(queue.Offer(Reminder("Call Ana"), canShow: true));
        Assert.Equal("Call Ana", queue.Finish()?.Text);
    }

    [Fact]
    public void A_blip_shows_at_once_and_the_interrupted_reminder_comes_back()
    {
        var queue = new HudQueue();
        queue.Offer(Reminder("Water"), canShow: true);
        Assert.Equal("Volume 40%", queue.Offer(Blip("Volume 40%"), canShow: true)?.Text);
        Assert.Equal("Water", queue.Finish()?.Text);
    }

    [Fact]
    public void A_blip_replaces_another_blip()
    {
        var queue = new HudQueue();
        queue.Offer(Blip("Volume 40%"), canShow: true);
        Assert.Equal("Volume 42%", queue.Offer(Blip("Volume 42%"), canShow: true)?.Text);
        Assert.Null(queue.Finish());
    }

    [Fact]
    public void Opening_the_notch_puts_the_reminder_back_and_drops_the_blip()
    {
        var queue = new HudQueue();
        queue.Offer(Reminder("Water"), canShow: true);
        queue.Interrupt();
        Assert.Equal("Water", queue.Resume()?.Text);

        queue.Finish();
        queue.Offer(Blip("Caps Lock"), canShow: true);
        queue.Interrupt();
        Assert.Null(queue.Resume());
    }

    [Fact]
    public void Resume_leaves_a_notice_already_up_alone()
    {
        var queue = new HudQueue();
        queue.Offer(Reminder("Water"), canShow: true);
        queue.Offer(Reminder("Call Ana"), canShow: true);
        Assert.Null(queue.Resume());
        Assert.Equal("Water", queue.Current?.Text);
    }

    [Fact]
    public void The_same_reminder_twice_is_said_once()
    {
        var queue = new HudQueue();
        queue.Offer(Reminder("Water"), canShow: false);
        queue.Offer(Reminder("Water"), canShow: false);
        Assert.Equal(1, queue.Waiting);
    }

    [Fact]
    public void A_backlog_keeps_the_newest_reminders()
    {
        var queue = new HudQueue();
        for (int i = 1; i <= HudQueue.MaxWaiting + 2; i++)
            queue.Offer(Reminder($"R{i}"), canShow: false);
        Assert.Equal(HudQueue.MaxWaiting, queue.Waiting);
        Assert.Equal("R3", queue.Resume()?.Text);
    }

    [Fact]
    public void A_full_backlog_still_keeps_the_interrupted_reminder_first()
    {
        var queue = new HudQueue();
        queue.Offer(Reminder("Now"), canShow: true);
        for (int i = 1; i <= HudQueue.MaxWaiting; i++)
            queue.Offer(Reminder($"R{i}"), canShow: true);
        queue.Interrupt();
        Assert.Equal(HudQueue.MaxWaiting, queue.Waiting);
        Assert.Equal("Now", queue.Resume()?.Text);
        Assert.Equal("R2", queue.Finish()?.Text);
    }
}
