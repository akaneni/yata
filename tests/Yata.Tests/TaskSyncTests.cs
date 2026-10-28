using System.Text.Json;
using Yata.Services;

namespace Yata.Tests;

/// <summary>Google Tasks sync: JSON in and out, merging Google's list into the tab, local edits winning.</summary>
public class TaskSyncTests
{
    private readonly DateTime _now = new(2026, 10, 26, 14, 0, 0);

    private TaskList NewList() => new(AppStore.InMemory(), () => _now);

    private static RemoteTask? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return TaskSync.Parse(doc.RootElement);
    }

    [Fact]
    public void Parses_a_google_task()
    {
        var task = Parse("""{"id":"t1","title":" Comprar pão ","status":"completed","due":"2026-10-28T00:00:00.000Z"}""")!;
        Assert.Equal(new RemoteTask("t1", "Comprar pão", true, new DateTime(2026, 10, 28)), task);
    }

    [Theory]
    [InlineData("""{"id":"t1","title":"   "}""")]
    [InlineData("""{"id":"t1","title":"x","deleted":true}""")]
    [InlineData("""{"title":"sem id"}""")]
    public void Skips_blank_deleted_and_broken_tasks(string json) => Assert.Null(Parse(json));

    [Fact]
    public void Due_keeps_the_day_whatever_the_time_zone()
    {
        Assert.Equal(new DateTime(2026, 10, 28), TaskSync.ParseDue("2026-10-28T00:00:00.000Z"));
        Assert.Null(TaskSync.ParseDue("amanhã"));
        Assert.Null(TaskSync.ParseDue(null));
    }

    [Fact]
    public void Lists_fall_back_to_a_name()
    {
        using var doc = JsonDocument.Parse("""{"items":[{"id":"a","title":"Minhas tarefas"},{"id":"b","title":""}]}""");
        Assert.Equal(new[] { new TaskListInfo("a", "Minhas tarefas"), new TaskListInfo("b", "Sem nome") },
            TaskSync.ParseLists(doc.RootElement));
    }

    [Fact]
    public void Body_sends_only_the_day_and_clears_completion_when_reopened()
    {
        var open = new TaskItem { Text = "Reunião", Due = new DateTime(2026, 10, 26, 15, 30, 0) };
        using var a = JsonDocument.Parse(TaskSync.Body(open));
        Assert.Equal("Reunião", a.RootElement.GetProperty("title").GetString());
        Assert.Equal("needsAction", a.RootElement.GetProperty("status").GetString());
        Assert.Equal("2026-10-26T00:00:00.000Z", a.RootElement.GetProperty("due").GetString());
        Assert.Equal(JsonValueKind.Null, a.RootElement.GetProperty("completed").ValueKind);

        using var b = JsonDocument.Parse(TaskSync.Body(new TaskItem { Text = "x", Done = true }));
        Assert.Equal("completed", b.RootElement.GetProperty("status").GetString());
        Assert.False(b.RootElement.TryGetProperty("completed", out _));
        Assert.Equal(JsonValueKind.Null, b.RootElement.GetProperty("due").ValueKind);
    }

    [Fact]
    public void Remote_tasks_come_in_open_first_and_local_ones_stay()
    {
        var list = NewList();
        list.Add("Só aqui");
        list.ApplyRemote(new[]
        {
            new RemoteTask("g1", "Feita no celular", true, null),
            new RemoteTask("g2", "Do Google", false, new DateTime(2026, 10, 27)),
        });

        Assert.Equal(new[] { "Só aqui", "Do Google", "Feita no celular" }, list.Items.Select(t => t.Text));
        var google = list.Items.Single(t => t.GoogleId == "g2");
        Assert.True(google.DueAllDay);
        Assert.Equal(new DateTime(2026, 10, 27), google.Due);
    }

    [Fact]
    public void Google_changes_update_clean_tasks_and_absent_ones_go()
    {
        var list = NewList();
        list.ApplyRemote(new[] { new RemoteTask("g1", "A", false, null), new RemoteTask("g2", "B", false, null) });

        list.ApplyRemote(new[] { new RemoteTask("g1", "A renomeada", true, null) });

        var only = Assert.Single(list.Items);
        Assert.Equal("A renomeada", only.Text);
        Assert.True(only.Done);
    }

    [Fact]
    public void A_local_edit_not_yet_sent_wins_over_google()
    {
        var list = NewList();
        list.ApplyRemote(new[] { new RemoteTask("g1", "A", false, null) });
        var item = list.Items.Single();
        list.Toggle(item);
        Assert.True(item.Dirty);

        list.ApplyRemote(Array.Empty<RemoteTask>()); // Even if Google no longer returns it.

        Assert.Same(item, list.Items.Single());
        Assert.True(item.Done);
    }

    [Fact]
    public void A_time_set_here_survives_while_the_day_matches()
    {
        var list = NewList();
        list.Add("Reunião 15:30");
        var item = list.Items.Single();
        list.Sent(item, "g1", item.Version);

        list.ApplyRemote(new[] { new RemoteTask("g1", "Reunião", false, _now.Date) });
        Assert.Equal(_now.Date.AddHours(15.5), item.Due);
        Assert.False(item.DueAllDay);

        // Moved to another day in Google: the time can't be known any more.
        list.ApplyRemote(new[] { new RemoteTask("g1", "Reunião", false, _now.Date.AddDays(2)) });
        Assert.Equal(_now.Date.AddDays(2), item.Due);
        Assert.True(item.DueAllDay);
    }

    [Fact]
    public void Removing_a_synced_task_queues_its_deletion_and_blocks_it_coming_back()
    {
        var list = NewList();
        list.ApplyRemote(new[] { new RemoteTask("g1", "A", false, null) });
        list.Remove(list.Items.Single());

        Assert.Equal(new[] { "g1" }, list.PendingDeletes);
        list.ApplyRemote(new[] { new RemoteTask("g1", "A", false, null) }); // Deletion not sent yet.
        Assert.Empty(list.Items);

        list.Deleted("g1");
        Assert.Empty(list.PendingDeletes);
    }

    [Fact]
    public void An_edit_made_while_sending_stays_dirty()
    {
        var list = NewList();
        list.Add("Nova");
        var item = list.Items.Single();
        int version = item.Version;
        list.Toggle(item); // While the create was in flight.

        list.Sent(item, "g1", version);

        Assert.Equal("g1", item.GoogleId);
        Assert.True(item.Dirty);
    }

    [Fact]
    public void A_task_removed_while_being_created_is_deleted_in_google()
    {
        var list = NewList();
        list.Add("Nova");
        var item = list.Items.Single();
        list.Remove(item);
        Assert.Empty(list.PendingDeletes); // It had no Google id yet.

        list.Sent(item, "g1", item.Version);

        Assert.Equal(new[] { "g1" }, list.PendingDeletes);
    }

    [Fact]
    public void Dropping_synced_tasks_keeps_local_ones()
    {
        var list = NewList();
        list.Add("Só aqui");
        list.ApplyRemote(new[] { new RemoteTask("g1", "Do Google", false, null) });
        list.Remove(list.Items.Single(t => t.GoogleId == "g1"));

        list.DropSynced();

        Assert.Equal("Só aqui", Assert.Single(list.Items).Text);
        Assert.Empty(list.PendingDeletes);
    }

    [Fact]
    public void Sync_state_survives_a_restart()
    {
        var store = AppStore.InMemory();
        var list = new TaskList(store, () => _now);
        list.ApplyRemote(new[] { new RemoteTask("g1", "A", false, new DateTime(2026, 10, 30)) });
        list.Toggle(list.Items.Single());

        var item = new TaskList(store, () => _now).Items.Single();

        Assert.Equal("g1", item.GoogleId);
        Assert.True(item.Dirty);
        Assert.True(item.DueAllDay);
    }

    [Fact]
    public void Day_only_dues_have_no_reminder()
    {
        var messages = new List<HudMessage>();
        var list = new TaskList(AppStore.InMemory(), () => _now);
        list.Message += messages.Add;
        list.ApplyRemote(new[] { new RemoteTask("g1", "Hoje", false, _now.Date) });

        list.Poll();

        Assert.Empty(messages);
        Assert.False(list.Items.Single().Reminded);
    }
}
