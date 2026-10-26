using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public sealed class TaskItem : INotifyPropertyChanged
{
    private bool _done;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public DateTime? Due { get; init; }
    public bool Reminded { get; set; }

    public bool Done
    {
        get => _done;
        set
        {
            if (_done == value)
                return;
            _done = value;
            OnPropertyChanged();
        }
    }

    public bool HasDue => Due.HasValue;

    /// <summary>"15:30", or "amanhã 09:00" when it isn't today.</summary>
    public string DueText => Due is not DateTime due ? ""
        : due.Date == DateTime.Today ? due.ToString("HH:mm")
        : due.Date == DateTime.Today.AddDays(1) ? "amanhã " + due.ToString("HH:mm")
        : due.ToString("dd/MM HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>To-do list. A trailing time ("Reunião 15:30", "ligar às 9h00") becomes a reminder.</summary>
public sealed class TaskList
{
    private static readonly Regex TrailingTime = new(@"^(.*?)\s+(?:às\s+|as\s+)?(\d{1,2})(?::|h)(\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppStore _store;
    private readonly Func<DateTime> _clock;
    private readonly Action _chime;
    private readonly DispatcherTimer _reminder = new() { Interval = TimeSpan.FromSeconds(15) };

    public ObservableCollection<TaskItem> Items { get; } = new();

    public event Action<HudMessage>? Message;

    /// <param name="clock">Current time; tests pass a fake one.</param>
    /// <param name="chime">Reminder sound; tests pass a silent one.</param>
    public TaskList(AppStore store, Func<DateTime>? clock = null, Action? chime = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.Now);
        _chime = chime ?? Chime.Play;
        foreach (var data in store.Data.Tasks)
        {
            Items.Add(new TaskItem
            {
                Id = data.Id,
                Text = data.Text,
                Done = data.Done,
                Due = data.Due,
                Reminded = data.Reminded,
            });
        }
        _reminder.Tick += (_, _) => CheckReminders();
    }

    public void Start()
    {
        _reminder.Start();
        CheckReminders();
    }

    public void Stop() => _reminder.Stop();

    public void Add(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0)
            return;

        DateTime? due = null;
        var match = TrailingTime.Match(text);
        if (match.Success && int.Parse(match.Groups[2].Value) is int hour and < 24 &&
            int.Parse(match.Groups[3].Value) is int minute and < 60)
        {
            text = match.Groups[1].Value.Trim();
            var now = _clock();
            var at = now.Date.AddHours(hour).AddMinutes(minute);
            due = at > now ? at : at.AddDays(1);
        }

        // Open tasks first, so new ones go right after the last open one.
        int index = Items.TakeWhile(t => !t.Done).Count();
        Items.Insert(index, new TaskItem { Text = text, Due = due });
        Save();
    }

    public void Toggle(TaskItem item)
    {
        item.Done = !item.Done;
        Items.Remove(item);
        int index = item.Done ? Items.Count : Items.TakeWhile(t => !t.Done).Count();
        Items.Insert(index, item);
        Save();
    }

    public void Remove(TaskItem item)
    {
        Items.Remove(item);
        Save();
    }

    /// <summary>One reminder check, as the timer does; for tests.</summary>
    internal void Poll() => CheckReminders();

    private void CheckReminders()
    {
        var now = _clock();
        var due = Items.Where(t => !t.Done && !t.Reminded && t.Due <= now).ToList();
        foreach (var item in due)
        {
            item.Reminded = true;
            _chime();
            Message?.Invoke(new HudMessage(Glyphs.Bell, item.Text, item.Due!.Value.ToString("HH:mm"),
                DurationMs: 6000, Important: true));
        }

        if (due.Count > 0)
            Save();
    }

    private void Save()
    {
        _store.Data.Tasks = Items.Select(t => new TaskData
        {
            Id = t.Id,
            Text = t.Text,
            Done = t.Done,
            Due = t.Due,
            Reminded = t.Reminded,
        }).ToList();
        _store.SaveSoon();
    }
}
