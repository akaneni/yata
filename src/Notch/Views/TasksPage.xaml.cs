using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notch.Services;

namespace Notch.Views;

public partial class TasksPage : UserControl
{
    private TaskList? _tasks;

    /// <summary>The notch normally never takes focus; typing needs it, so the host is asked first.</summary>
    public event Action? KeyboardRequested;

    /// <summary>Typing is over (task added, Escape, or focus left the box).</summary>
    public event Action? KeyboardReleased;

    public TasksPage() => InitializeComponent();

    public bool IsTyping => Input.IsKeyboardFocused;

    /// <summary>Half-typed text that would be lost if the notch closed now.</summary>
    public bool HasDraft => IsTyping && Input.Text.Length > 0;

    public void Attach(TaskList tasks)
    {
        _tasks = tasks;
        List.ItemsSource = tasks.Items;
        tasks.Items.CollectionChanged += OnItemsChanged;
        OnItemsChanged(null, null);
    }

    public void EndTyping()
    {
        if (IsTyping)
            Keyboard.ClearFocus();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs? e) =>
        EmptyText.Visibility = _tasks?.Items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    private void OnInputMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTyping)
            return;
        KeyboardRequested?.Invoke();
        Input.Focus();
        e.Handled = true;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Input.Clear();
            EndTyping();
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => KeyboardReleased?.Invoke();

    private void OnAdd(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        _tasks?.Add(Input.Text);
        Input.Clear();
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItem item })
            _tasks?.Toggle(item);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItem item })
            _tasks?.Remove(item);
    }
}
