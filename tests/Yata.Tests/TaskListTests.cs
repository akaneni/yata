using Yata.Services;

namespace Yata.Tests;

/// <summary>Adding tasks, the trailing-time reminder syntax, ordering and reminders.</summary>
public class TaskListTests
{
    private DateTime _now = new(2026, 10, 26, 14, 0, 0);
    private readonly List<HudMessage> _messages = new();
    private int _chimes;

    private TaskList NewList()
    {
        var list = new TaskList(AppStore.InMemory(), () => _now, () => _chimes++);
        list.Message += _messages.Add;
        return list;
    }

    [Theory]
    [InlineData("Reunião 15:30", "Reunião", 15, 30)]
    [InlineData("ligar às 9h05", "ligar", 9, 5)]
    [InlineData("pagar conta as 18:00", "pagar conta", 18, 0)]
    public void A_trailing_time_becomes_the_due_time(string input, string text, int hour, int minute)
    {
        var list = NewList();
        list.Add(input);

        var task = Assert.Single(list.Items);
        Assert.Equal(text, task.Text);
        Assert.NotNull(task.Due);
        Assert.Equal(new TimeSpan(hour, minute, 0), task.Due!.Value.TimeOfDay);
    }

    [Fact]
    public void A_time_already_past_today_means_tomorrow()
    {
        var list = NewList();
        list.Add("Reunião 15:30");
        list.Add("Café 9:00");

        Assert.Equal(_now.Date.AddHours(15.5), list.Items.Single(t => t.Text == "Reunião").Due);
        Assert.Equal(_now.Date.AddDays(1).AddHours(9), list.Items.Single(t => t.Text == "Café").Due);
    }

    [Theory]
    [InlineData("Ler capítulo 25")]
    [InlineData("Treino 25:00")]   // not a time of day
    [InlineData("Ver 10:75")]
    public void Text_without_a_valid_trailing_time_has_no_due(string input)
    {
        var list = NewList();
        list.Add(input);

        var task = Assert.Single(list.Items);
        Assert.Equal(input, task.Text);
        Assert.Null(task.Due);
    }

    [Fact]
    public void Blank_input_is_ignored()
    {
        var list = NewList();
        list.Add("   ");

        Assert.Empty(list.Items);
    }

    [Fact]
    public void New_tasks_go_after_open_ones_and_before_done_ones()
    {
        var list = NewList();
        list.Add("A");
        list.Add("B");
        list.Toggle(list.Items[0]); // A done → moves to the end
        list.Add("C");

        Assert.Equal(new[] { "B", "C", "A" }, list.Items.Select(t => t.Text));
        list.Toggle(list.Items.Single(t => t.Text == "A")); // reopened → back after the open ones
        Assert.Equal(new[] { "B", "C", "A" }, list.Items.Select(t => t.Text));
        Assert.False(list.Items[2].Done);
    }

    [Fact]
    public void Reminds_once_when_due_with_sound()
    {
        var list = NewList();
        list.Add("Reunião 14:30");

        list.Poll();
        Assert.Empty(_messages);

        _now = _now.AddMinutes(31);
        list.Poll();
        list.Poll();

        var message = Assert.Single(_messages);
        Assert.Equal("Reunião", message.Text);
        Assert.Equal("14:30", message.Value);
        Assert.True(message.Important);
        Assert.Equal(1, _chimes);
    }

    [Fact]
    public void Done_tasks_are_not_reminded()
    {
        var list = NewList();
        list.Add("Reunião 14:30");
        list.Toggle(list.Items[0]);

        _now = _now.AddHours(1);
        list.Poll();

        Assert.Empty(_messages);
    }
}
