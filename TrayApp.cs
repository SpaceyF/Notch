using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace Notch;

// the tray icon runs the app: it shows the notch and opens the settings window.
// since the notch itself lets clicks pass through, all the controls live here.
sealed class TrayApp : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly Overlay _overlay;
    SettingsForm? _settings;

    public TrayApp()
    {
        _overlay = new Overlay();
        _overlay.Show();

        _icon = new WinForms.NotifyIcon { Icon = MakeIcon(), Visible = true, Text = "Notch" };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (s, e) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (s, e) => Quit());
        _icon.ContextMenuStrip = menu;

        // left-click or double-click the tray icon to open settings
        _icon.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) ShowSettings(); };
        _icon.DoubleClick += (s, e) => ShowSettings();
    }

    void ShowSettings()
    {
        if (_settings == null || _settings.IsDisposed)
        {
            _settings = new SettingsForm(_overlay);
            _settings.FormClosed += (s, e) => _settings = null;
            _settings.Show();
        }
        _settings.Activate();
        _settings.BringToFront();
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var rect = new Rectangle(4, 10, 24, 12);
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, 12, 12, 90, 180);
            path.AddArc(rect.Right - 12, rect.Y, 12, 12, 270, 180);
            path.CloseFigure();
            using var b = new SolidBrush(Color.Black);
            g.FillPath(b, path);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    void Quit()
    {
        _overlay.Shutdown();
        _icon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
