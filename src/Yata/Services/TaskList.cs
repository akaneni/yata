using System;
using System.Collections.Generic;
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
    private string _text = "";
    private DateTime? _due;
    private bool _dueAllDay;

    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Reminded { get; set; }

    /// <summary>Id in Google Tasks once synced; null for tasks that live only here.</summary>
    public string? GoogleId { get; set; }
    /// <summary>Changed here and not yet sent to Google.</summary>
    public bool Dirty { get; set; }
    /// <summary>Bumped on every local change, so a sync can tell an edit made while it was sending.</summary>
    internal int Version { get; set; }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool Done
    {
        get => _done;
        set => Set(ref _done, value);
    }

    public DateTime? Due
    {
        get => _due;
        set
        {
            if (Set(ref _due, value))
            {
                OnPropertyChanged(nameof(HasDue));
                OnPropertyChanged(nameof(DueText));
            }
        }
    }

    /// <summary>A day without a time: Google Tasks keeps only dates. No reminder for these.</summary>
    public bool DueAllDay
    {
        get => _dueAllDay;
        set
        {
            if (Set(ref _dueAllDay, value))
                OnPropertyChanged(nameof(DueText));
        }
    }

    public bool HasDue => Due.HasValue;

    /// <summary>"15:30", or "amanhã 09:00" when it isn't today; "hoje"/"amanhã"/"28/10" without a time.</summary>
    public string DueText => Due is not DateTime due ? ""
        : DueAllDay ? (due.Date == DateTime.Today ? "hoje"
            : due.Date == DateTime.Today.AddDays(1) ? "amanhã"
            : due.ToString("dd/MM"))
        : due.Date == DateTime.Today ? due.ToString("HH:mm")
        : due.Date == DateTime.Today.AddDays(1) ? "amanhã " + due.ToString("HH:mm")
        : due.ToString("dd/MM HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A task as Google Tasks has it. Due is a day only: the API discards the time.</summary>
public sealed record RemoteTask(string Id, string Title, bool Done, DateTime? DueDate);

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

    /// <summary>Google ids of synced tasks removed here, until the deletion reaches Google.</summary>
    internal List<string> PendingDeletes => _store.Data.TaskDeletes;

    public event Action<HudMessage>? Message;

    /// <summary>Something was added, ticked or removed here (not by a sync).</summary>
    public event Action? LocalChanged;

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
                DueAllDay = data.DueAllDay,
                Reminded = data.Reminded,
                GoogleId = data.GoogleId,
                Dirty = data.Dirty,
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
        LocalChanged?.Invoke();
    }

    public void Toggle(TaskItem item)
    {
        item.Done = !item.Done;
        Touch(item);
        Items.Remove(item);
        int index = item.Done ? Items.Count : Items.TakeWhile(t => !t.Done).Count();
        Items.Insert(index, item);
        Save();
        LocalChanged?.Invoke();
    }

    /// <summary>Takes a task off the list; returns where it was, for <see cref="Restore"/>.</summary>
    public int Remove(TaskItem item)
    {
        int index = Items.IndexOf(item);
        Items.Remove(item);
        if (item.GoogleId != null)
            PendingDeletes.Add(item.GoogleId);
        Save();
        LocalChanged?.Invoke();
        return index;
    }

    /// <summary>
    /// Undoes <see cref="Remove"/>. If the deletion hasn't gone out yet it's just called off; if
    /// Google already deleted the task, it goes back as a new one there.
    /// </summary>
    public void Restore(TaskItem item, int index)
    {
        if (item.GoogleId != null && !PendingDeletes.Remove(item.GoogleId))
        {
            item.GoogleId = null;
            item.Dirty = false;
        }
        Items.Insert(Math.Clamp(index, 0, Items.Count), item);
        Save();
        LocalChanged?.Invoke();
    }

    /// <summary>Sync turned off or the account disconnected: drop the mirrored tasks, keep the local ones.</summary>
    public void DropSynced()
    {
        foreach (var item in Items.Where(t => t.GoogleId != null).ToList())
            Items.Remove(item);
        PendingDeletes.Clear();
        Save();
    }

    /// <summary>
    /// Brings in the list as Google has it (open tasks plus those completed recently). Tasks with
    /// changes not yet sent keep the local version; synced tasks Google no longer returns are dropped.
    /// </summary>
    internal void ApplyRemote(IReadOnlyCollection<RemoteTask> remote)
    {
        var byId = remote.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var known = new HashSet<string>();

        foreach (var item in Items.ToList())
        {
            if (item.GoogleId == null)
                continue;
            known.Add(item.GoogleId);
            if (item.Dirty)
                continue;
            if (!byId.TryGetValue(item.GoogleId, out var task))
            {
                Items.Remove(item); // Deleted in Google, or completed long enough ago to drop off.
                continue;
            }
            item.Text = task.Title;
            item.Done = task.Done;
            SetDueFromGoogle(item, task.DueDate);
        }

        foreach (var task in remote)
        {
            if (known.Contains(task.Id) || PendingDeletes.Contains(task.Id))
                continue;
            known.Add(task.Id);
            var item = new TaskItem { GoogleId = task.Id, Text = task.Title, Done = task.Done };
            SetDueFromGoogle(item, task.DueDate);
            Items.Add(item);
        }

        // Open first, done last, keeping the order within each group.
        var ordered = Items.Where(t => !t.Done).Concat(Items.Where(t => t.Done)).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int at = Items.IndexOf(ordered[i]);
            if (at != i)
                Items.Move(at, i);
        }
        Save();
    }

    /// <summary>A sync sent this item; it's clean unless it changed again meanwhile.</summary>
    internal void Sent(TaskItem item, string googleId, int version)
    {
        item.GoogleId = googleId;
        item.Dirty = item.Version != version;
        if (!Items.Contains(item))
            PendingDeletes.Add(googleId); // Removed here while it was being created in Google.
        Save();
    }

    internal void Deleted(string googleId)
    {
        PendingDeletes.Remove(googleId);
        Save();
    }

    /// <summary>Google keeps only the day. A time set here survives as long as the day matches.</summary>
    private static void SetDueFromGoogle(TaskItem item, DateTime? date)
    {
        if (date is not DateTime day)
        {
            item.Due = null;
            item.DueAllDay = false;
        }
        else if (item.Due?.Date != day.Date)
        {
            item.Due = day.Date;
            item.DueAllDay = true;
            item.Reminded = false;
        }
    }

    private static void Touch(TaskItem item)
    {
        item.Version++;
        if (item.GoogleId != null)
            item.Dirty = true;
    }

    /// <summary>One reminder check, as the timer does; for tests.</summary>
    internal void Poll() => CheckReminders();

    private void CheckReminders()
    {
        var now = _clock();
        var due = Items.Where(t => !t.Done && !t.Reminded && !t.DueAllDay && t.Due <= now).ToList();
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

    internal void Save()
    {
        _store.Data.Tasks = Items.Select(t => new TaskData
        {
            Id = t.Id,
            Text = t.Text,
            Done = t.Done,
            Due = t.Due,
            DueAllDay = t.DueAllDay,
            Reminded = t.Reminded,
            GoogleId = t.GoogleId,
            Dirty = t.Dirty,
        }).ToList();
        _store.SaveSoon();
    }
}
