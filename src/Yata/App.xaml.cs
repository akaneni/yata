using System.Linq;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Yata;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "Yata.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _window = new MainWindow();
        _window.Show();
        CreateTrayIcon();

        if (e.Args.Contains("--demo"))
            _window.Dispatcher.InvokeAsync(_window.RunCompanionDemo, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Demonstração do Yata", null, (_, _) => _window?.RunCompanionDemo());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Yata",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new System.Uri("pack://application:,,,/Assets/yata.ico"));
        if (resource == null)
            return System.Drawing.SystemIcons.Application;
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _window?.Close();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
