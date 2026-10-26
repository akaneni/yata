using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

        // Measured: for a small transparent overlay, GPU rendering costs more than it saves —
        // each frame is read back from the GPU — and the Direct3D device alone took ~75 MB.
        // Software rendering cut Task Manager memory from ~119 MB to ~43 MB and halved CPU.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _singleInstance = new Mutex(true, "Yata.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _window = new MainWindow();
        _window.Show();
        CreateTrayIcon();

        int show = System.Array.IndexOf(e.Args, "--show");
        if (show >= 0)
        {
            string page = show + 1 < e.Args.Length ? e.Args[show + 1] : "media";
            int seconds = show + 2 < e.Args.Length && int.TryParse(e.Args[show + 2], out int s) ? s : 20;
            _window.Dispatcher.InvokeAsync(() => _window.ShowPage(page, seconds), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        if (e.Args.Contains("--settings"))
            _window.OpenSettings();
        if (e.Args.Contains("--demo"))
            _window.Dispatcher.InvokeAsync(_window.RunCompanionDemo, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Configurações…", null, (_, _) => _window?.OpenSettings());
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
        _tray.DoubleClick += (_, _) => _window?.OpenSettings();
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
