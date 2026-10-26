using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

// Dropping files on the notch fills the shelf.
public partial class MainWindow
{
    private void OnNotchDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ShelfPage.IsDraggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // The shelf only keeps a reference, so "link" is the honest cursor when the source allows it.
        e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
        _hoverTimer.Stop();
        if (_dropHinting)
            return;

        _dropHinting = true;
        TabShelf.IsChecked = true;
        ShelfPage.ShowDropHint(true);
        SetExpanded(true);
    }

    private void OnNotchDragLeave(object sender, DragEventArgs e)
    {
        // Also fires when moving between child elements; the next DragOver cancels this.
        if (_dropHinting)
            ScheduleExpanded(false, 400);
    }

    private void OnNotchDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!ShelfPage.IsDraggingOut && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _shelf.Add(paths);
            // Tell the source nothing was moved: the shelf only references the files.
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        EndDropHint();
    }

    private void EndDropHint()
    {
        _dropHinting = false;
        ShelfPage.ShowDropHint(false);
    }
}
