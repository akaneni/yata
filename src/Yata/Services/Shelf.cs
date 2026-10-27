using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Yata.Helpers;

namespace Yata.Services;

public sealed class ShelfItem
{
    public ShelfItem(string path)
    {
        FilePath = path;
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        Name = name.Length > 0 ? name : path; // Drive roots have no file name.
        (Icon, IsPicture) = FileIcons.For(path);
    }

    public string FilePath { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    public bool IsPicture { get; }

    public bool Exists => File.Exists(FilePath) || Directory.Exists(FilePath);
}

/// <summary>
/// Files parked on the notch to drag somewhere else later. Only paths are kept;
/// nothing is copied, and entries whose file is gone disappear.
/// </summary>
public sealed class Shelf
{
    private const int MaxItems = 30;

    private readonly AppStore _store;

    public ObservableCollection<ShelfItem> Items { get; } = new();

    public Shelf(AppStore store)
    {
        _store = store;
        foreach (var path in store.Data.Shelf.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = new ShelfItem(path);
            if (item.Exists)
                Items.Add(item);
        }
        if (Items.Count != store.Data.Shelf.Count)
            Save();
    }

    public void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var item = new ShelfItem(path);
            if (item.Exists)
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
        Save();
        return cleared;
    }

    /// <summary>Undoes <see cref="Clear"/>, after anything dropped on the shelf since.</summary>
    public void Restore(IEnumerable<ShelfItem> items)
    {
        foreach (var item in items)
        {
            if (Items.Count < MaxItems && item.Exists &&
                !Items.Any(i => string.Equals(i.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)))
                Items.Add(item);
        }
        Save();
    }

    /// <summary>Drops entries whose file was moved or deleted (e.g. dragged out as a move).</summary>
    public void Prune()
    {
        var gone = Items.Where(i => !i.Exists).ToList();
        foreach (var item in gone)
            Items.Remove(item);
        if (gone.Count > 0)
            Save();
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
        _store.Data.Shelf = Items.Select(i => i.FilePath).ToList();
        _store.SaveSoon();
    }
}
