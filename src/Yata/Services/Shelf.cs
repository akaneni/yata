using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>Where a shelved path stands.</summary>
public enum ShelfState
{
    /// <summary>The file or folder is there.</summary>
    Present,
    /// <summary>Its drive or server answers, and it isn't there: deleted or moved.</summary>
    Gone,
    /// <summary>Its drive or server doesn't answer (VPN off, USB stick out): it may come back.</summary>
    Unavailable,
}

public sealed class ShelfItem
{
    /// <param name="available">False for a path whose drive or server doesn't answer: shown dimmed,
    /// with its type's icon, without touching the file.</param>
    public ShelfItem(string path, bool available = true)
    {
        FilePath = path;
        IsAvailable = available;
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        Name = name.Length > 0 ? name : path; // Drive roots have no file name.
        (Icon, IsPicture) = available ? FileIcons.For(path) : (FileIcons.ForType(path), false);
    }

    public string FilePath { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    public bool IsPicture { get; }
    public bool IsAvailable { get; }

    /// <summary>
    /// Present, gone or unavailable. Gone needs the path's root (drive or \\server\share) to
    /// answer: a share behind a VPN that's off says nothing about the file. Can block for seconds
    /// on a server that doesn't answer, so it runs off the UI thread.
    /// </summary>
    internal static ShelfState Check(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                return ShelfState.Present;
            string? root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && Directory.Exists(root) ? ShelfState.Gone : ShelfState.Unavailable;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ShelfState.Unavailable;
        }
    }
}

/// <summary>
/// Files parked on the notch to drag somewhere else later. Only paths are kept; nothing is copied.
/// Entries whose file was deleted or moved disappear; ones on a drive or server that doesn't
/// answer stay, dimmed, until it's back or the user removes them.
/// </summary>
public sealed class Shelf
{
    private const int MaxItems = 30;

    private readonly AppStore _store;
    // Paths read from data.json but not checked yet: kept when saving meanwhile.
    private List<string> _pending;
    private bool _checking;

    public ObservableCollection<ShelfItem> Items { get; } = new();

    public Shelf(AppStore store)
    {
        _store = store;
        _pending = store.Data.Shelf.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Checks the saved paths off the UI thread, then shows them.</summary>
    public Task LoadAsync() => CheckAsync();

    public void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            // Just dragged here: the file is at hand.
            var item = new ShelfItem(path);
            if (File.Exists(path) || Directory.Exists(path))
                Items.Insert(0, item);
        }

        while (Items.Count > MaxItems)
            Items.RemoveAt(Items.Count - 1);
        Save();
    }

    public void Remove(ShelfItem item)
    {
        Items.Remove(item);
        Save();
    }

    /// <summary>Empties the shelf; returns what was on it, for <see cref="Restore"/>.</summary>
    public IReadOnlyList<ShelfItem> Clear()
    {
        var cleared = Items.ToList();
        Items.Clear();
        _pending.Clear();
        Save();
        return cleared;
    }

    /// <summary>Undoes <see cref="Clear"/>, after anything dropped on the shelf since.</summary>
    public void Restore(IEnumerable<ShelfItem> items)
    {
        foreach (var item in items)
        {
            if (Items.Count < MaxItems &&
                !Items.Any(i => string.Equals(i.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)))
                Items.Add(item);
        }
        Save();
        _ = Prune();
    }

    /// <summary>
    /// Drops entries whose file was moved or deleted (e.g. dragged out as a move), and dims or
    /// brings back the ones whose drive or server stopped or started answering.
    /// </summary>
    public Task Prune() => CheckAsync();

    private async Task CheckAsync()
    {
        if (_checking)
            return;
        _checking = true;
        try
        {
            var paths = Items.Select(i => i.FilePath).Concat(_pending).ToList();
            var states = await Task.Run(() => paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, ShelfItem.Check, StringComparer.OrdinalIgnoreCase));

            var pending = _pending;
            _pending = new List<string>();
            foreach (var path in pending)
            {
                if (states[path] != ShelfState.Gone &&
                    !Items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    Items.Add(new ShelfItem(path, states[path] == ShelfState.Present));
            }
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var item = Items[i];
                if (!states.TryGetValue(item.FilePath, out var state))
                    continue; // Dropped on the shelf while checking.
                if (state == ShelfState.Gone)
                    Items.RemoveAt(i);
                else if ((state == ShelfState.Present) != item.IsAvailable)
                    Items[i] = new ShelfItem(item.FilePath, state == ShelfState.Present);
            }
            while (Items.Count > MaxItems)
                Items.RemoveAt(Items.Count - 1);
            Save();
        }
        finally
        {
            _checking = false;
        }
    }

    public static void Open(ShelfItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No associated app, or the file vanished; nothing to do.
        }
    }

    private void Save()
    {
        _store.Data.Shelf = Items.Select(i => i.FilePath).Concat(_pending).ToList();
        _store.SaveSoon();
    }
}
