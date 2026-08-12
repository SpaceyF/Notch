using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace Notch;

// one dark settings window, split into a left nav rail and a content panel so it's
// not one giant scroll of checkboxes. opens from the tray. changes apply right away.
sealed class SettingsForm : WinForms.Form
{
    readonly Overlay _overlay;
    WinForms.Label _wVal = null!, _hVal = null!, _sVal = null!, _dVal = null!, _bVal = null!;
    WinForms.Button _island = null!, _notch = null!;
    readonly List<(WinForms.Button btn, string hex)> _swatches = new();

    readonly WinForms.Panel _content = new();
    readonly List<(WinForms.Button nav, WinForms.Panel page)> _pages = new();

    static readonly Color Bg = Color.FromArgb(18, 18, 18);
    static readonly Color Rail = Color.FromArgb(24, 24, 26);
    static readonly Color Face = Color.FromArgb(34, 34, 36);
    static readonly Color Line = Color.FromArgb(60, 60, 64);
    static readonly Color Fg = Color.FromArgb(235, 235, 235);
    static readonly Color Sub = Color.FromArgb(150, 150, 155);

    static readonly (string name, string hex)[] Presets =
    {
        ("Teal", "#3DD6C4"), ("Purple", "#A66BFF"), ("Pink", "#FF6B9D"),
        ("Green", "#4ADE80"), ("Orange", "#FF9E1B"), ("White", "#FFFFFF"),
    };

    public SettingsForm(Overlay overlay)
    {
        _overlay = overlay;
        Text = "Notch";
        FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(452, 452);

        // the content area on the right, one page shown at a time
        _content.Location = new Point(128, 14);
        _content.Size = new Size(312, 424);
        _content.BackColor = Bg;
        Controls.Add(_content);

        BuildLook();
        BuildMedia();
        BuildAlerts();
        BuildDevices();
        BuildSystem();
        BuildJokes();

        // the left nav rail
        var rail = new WinForms.Panel { Location = new Point(0, 0), Size = new Size(116, 452), BackColor = Rail };
        Controls.Add(rail);
        string[] names = { "Look", "Media", "Alerts", "Devices", "System", "Jokes" };
        for (int i = 0; i < names.Length; i++)
        {
            var b = NavBtn(names[i], 8, 14 + i * 44);
            rail.Controls.Add(b);
            var page = _pages[i].page;
            b.Click += (s, e) => Select(page);
            _pages[i] = (b, page);
        }

        Select(_pages[0].page);
        Sync();
    }

    // ---------------------------------------------------------------- pages
    void BuildLook()
    {
        var p = Page();

        Section(p, "Style", 0);
        _island = Btn("Island", 0, 24, 148, 34);
        _notch = Btn("Notch", 156, 24, 148, 34);
        _island.Click += (s, e) => { _overlay.SetStyle(NotchStyle.Island); Sync(); };
        _notch.Click += (s, e) => { _overlay.SetStyle(NotchStyle.Notch); Sync(); };
        p.Controls.Add(_island); p.Controls.Add(_notch);

        Section(p, "Accent", 74);
        int sx = 0;
        foreach (var (_, hex) in Presets)
        {
            var h = hex;
            var b = Swatch(hex, sx, 98);
            b.Click += (s, e) => { _overlay.SetAccent(h); Sync(); };
            _swatches.Add((b, hex));
            p.Controls.Add(b);
            sx += 44;
        }
        var custom = Btn("Custom color…", 0, 138, 304, 30);
        custom.Click += (s, e) => PickCustom();
        p.Controls.Add(custom);

        Section(p, "Layout", 182);
        Check(p, "iOS layout (blank middle, art + square bars)", 206, _overlay.Settings.IosLayout, v => _overlay.SetIosLayout(v));
    }

    void BuildMedia()
    {
        var p = Page();
        Section(p, "Album art", 0);
        Check(p, "Match accent to what's playing", 24, _overlay.Settings.AutoAccent, v => _overlay.SetAutoAccent(v));
        Check(p, "Frosted album art", 50, _overlay.Settings.FrostedArt, v => _overlay.SetFrostedArt(v));

        Section(p, "Visualizer", 92);
        Row(p, "React", 116, () => StepSens(-0.25), () => StepSens(+0.25), out _sVal);
        Row(p, "Bars", 160, () => StepBars(-1), () => StepBars(+1), out _bVal);
        Check(p, "Dot matrix (9x9) instead of bars", 200, _overlay.Settings.Visual == VisualStyle.Dots,
            v => _overlay.SetVisual(v ? VisualStyle.Dots : VisualStyle.Bars));

        Section(p, "Effects", 240);
        Check(p, "Weather on the notch (rain / snow)", 264, _overlay.Settings.WeatherFx, v => _overlay.SetWeatherFx(v));
        Check(p, "Confetti on big moments", 290, _overlay.Settings.Confetti, v => _overlay.SetConfetti(v));
    }

    void BuildAlerts()
    {
        var p = Page();
        Section(p, "Pops", 0);
        Check(p, "Notification pop", 24, _overlay.Settings.ShowNotes, v => _overlay.SetShowNotes(v));
        Check(p, "Flash when you copy text", 50, _overlay.Settings.ShowCopied, v => _overlay.SetShowCopied(v));
        Check(p, "Download ring while a download runs", 76, _overlay.Settings.ShowDownloadRing, v => _overlay.SetShowDownloadRing(v));
        Check(p, "Just the ring, no name or size", 100, _overlay.Settings.DownloadRingCompact, v => _overlay.SetDownloadRingCompact(v));

        Section(p, "Airdrop", 142);
        Check(p, "Card when a file lands (screenshots, drops)", 166, _overlay.Settings.ShowAirdrop, v => _overlay.SetShowAirdrop(v));
        var drop = Btn("Open drop folder", 0, 196, 148, 30);
        drop.Click += (s, e) => { try { System.IO.Directory.CreateDirectory(_overlay.DropFolderPath); System.Diagnostics.Process.Start("explorer.exe", $"\"{_overlay.DropFolderPath}\""); } catch { } };
        p.Controls.Add(drop);
        var watch = Btn("Watch a folder…", 156, 196, 148, 30);
        watch.Click += (s, e) => { using var d = new WinForms.FolderBrowserDialog(); if (d.ShowDialog() == WinForms.DialogResult.OK) _overlay.AddAirdropFolder(d.SelectedPath); };
        p.Controls.Add(watch);

        Section(p, "Indicators", 246);
        Check(p, "Mic and camera dots", 270, _overlay.Settings.ShowDots, v => _overlay.SetShowDots(v));
        Check(p, "Recording pill while the screen is captured", 296, _overlay.Settings.ShowRecording, v => _overlay.SetShowRecording(v));

        Section(p, "Behavior", 338);
        Check(p, "Hide when an app is fullscreen", 362, _overlay.Settings.HideOnFullscreen, v => _overlay.SetHideOnFullscreen(v));
    }

    void BuildDevices()
    {
        var p = Page();
        Section(p, "Plugged-in devices", 0);
        Check(p, "3D card when you plug something in", 24, _overlay.Settings.ShowDeviceCard, v => _overlay.SetShowDeviceCard(v));
        Check(p, "Show the device name on it", 50, _overlay.Settings.ShowDeviceName, v => _overlay.SetShowDeviceName(v));
        Check(p, "Hide unknown devices", 76, _overlay.Settings.HideUnknownDevices, v => _overlay.SetHideUnknownDevices(v));
        var devs = Btn("Set up my devices…", 0, 114, 304, 30);
        devs.Click += (s, e) => ShowDevices();
        p.Controls.Add(devs);
    }

    void BuildSystem()
    {
        var p = Page();
        Section(p, "Size & feel", 0);
        Row(p, "Width", 24, () => Step(true, -0.05), () => Step(true, +0.05), out _wVal);
        Row(p, "Height", 68, () => Step(false, -0.05), () => Step(false, +0.05), out _hVal);
        Row(p, "Drag", 112, () => StepDrag(-1), () => StepDrag(+1), out _dVal);
        var reset = Btn("Reset size", 0, 156, 304, 30);
        reset.Click += (s, e) => { _overlay.ResetSize(); Sync(); };
        p.Controls.Add(reset);

        Section(p, "Startup", 200);
        Check(p, "Start with Windows", 224, Autostart.Enabled, Autostart.Set);

        Section(p, "Debug", 266);
        var dbg = Btn("Cycle popups", 0, 290, 304, 30);
        dbg.Click += (s, e) => _overlay.DebugNextPopup();
        p.Controls.Add(dbg);
    }

    void BuildJokes()
    {
        var p = Page();
        Section(p, "Just for fun", 0);
        Check(p, "Wiggle the top bar while the player's open", 24, _overlay.Settings.WiggleWhenOpen, v => _overlay.SetWiggleWhenOpen(v));
        var note = new WinForms.Label
        {
            Text = "grab the black bar while the music player is\npulled down and it'll wobble loose from it.",
            ForeColor = Sub, AutoSize = true, Location = new Point(2, 56), BackColor = Bg,
            Font = new Font("Segoe UI", 8.5f),
        };
        p.Controls.Add(note);
    }

    // ---------------------------------------------------------------- nav
    WinForms.Panel Page()
    {
        var p = new WinForms.Panel { Location = new Point(0, 0), Size = _content.Size, BackColor = Bg, Visible = false };
        _content.Controls.Add(p);
        _pages.Add((null!, p));
        return p;
    }

    void Select(WinForms.Panel page)
    {
        foreach (var (nav, pg) in _pages)
        {
            pg.Visible = pg == page;
            if (nav != null) StyleNav(nav, pg == page);
        }
    }

    WinForms.Button NavBtn(string text, int x, int y)
    {
        var b = new WinForms.Button
        {
            Text = text, Location = new Point(x, y), Size = new Size(100, 34),
            FlatStyle = WinForms.FlatStyle.Flat, BackColor = Rail, ForeColor = Sub,
            Font = new Font("Segoe UI", 10f), TabStop = false,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new WinForms.Padding(8, 0, 0, 0),
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 38, 42);
        return b;
    }

    void StyleNav(WinForms.Button b, bool active)
    {
        b.BackColor = active ? Face : Rail;
        b.ForeColor = active ? Fg : Sub;
    }

    // ---------------------------------------------------------------- controls
    void Section(WinForms.Control host, string name, int y)
    {
        host.Controls.Add(new WinForms.Label
        {
            Text = name.ToUpper(), ForeColor = Sub, AutoSize = true,
            Location = new Point(0, y), BackColor = Bg,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        });
    }

    void Check(WinForms.Control host, string text, int y, bool value, Action<bool> onChange)
    {
        var cb = new WinForms.CheckBox
        {
            Text = text, ForeColor = Fg, BackColor = Bg, AutoSize = true,
            Location = new Point(2, y), Checked = value, FlatStyle = WinForms.FlatStyle.Flat,
        };
        cb.CheckedChanged += (s, e) => onChange(cb.Checked);
        host.Controls.Add(cb);
    }

    void Row(WinForms.Control host, string name, int y, Action minus, Action plus, out WinForms.Label val)
    {
        var lbl = new WinForms.Label { Text = name, ForeColor = Sub, AutoSize = true, Location = new Point(2, y + 8), BackColor = Bg };
        var dec = Btn("−", 164, y, 34, 34);
        val = new WinForms.Label { Text = "100%", ForeColor = Fg, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(202, y), Size = new Size(64, 34), BackColor = Bg };
        var inc = Btn("+", 270, y, 34, 34);
        dec.Click += (s, e) => minus();
        inc.Click += (s, e) => plus();
        host.Controls.Add(lbl); host.Controls.Add(dec); host.Controls.Add(val); host.Controls.Add(inc);
    }

    void Step(bool width, double delta)
    {
        if (width) _overlay.SetWidthScale(_overlay.Settings.WidthScale + delta);
        else _overlay.SetHeightScale(_overlay.Settings.HeightScale + delta);
        Sync();
    }

    void StepSens(double delta) { _overlay.SetSensitivity(_overlay.Settings.Sensitivity + delta); Sync(); }

    void StepBars(int delta) { _overlay.SetBars(_overlay.Settings.Bars + delta); Sync(); }

    void StepDrag(int delta) { _overlay.SetDragStrength(_overlay.Settings.DragStrength + delta); Sync(); }

    DevicesForm? _devices;
    void ShowDevices()
    {
        if (_devices == null || _devices.IsDisposed)
        {
            _devices = new DevicesForm(_overlay);
            _devices.FormClosed += (s, e) => _devices = null;
            _devices.Show();
        }
        _devices.Activate();
    }

    void PickCustom()
    {
        using var dlg = new WinForms.ColorDialog { FullOpen = true };
        if (dlg.ShowDialog(this) == WinForms.DialogResult.OK)
        {
            var c = dlg.Color;
            _overlay.SetAccent($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            Sync();
        }
    }

    // update the buttons to show what's currently selected
    void Sync()
    {
        var acc = Parse(_overlay.Settings.Accent);
        var style = _overlay.Settings.Style;
        StyleBtn(_island, style == NotchStyle.Island, acc);
        StyleBtn(_notch, style == NotchStyle.Notch, acc);
        foreach (var (btn, hex) in _swatches)
        {
            bool on = string.Equals(hex, _overlay.Settings.Accent, StringComparison.OrdinalIgnoreCase);
            btn.FlatAppearance.BorderColor = on ? Color.White : Line;
            btn.FlatAppearance.BorderSize = on ? 2 : 1;
        }
        _wVal.Text = $"{_overlay.Settings.WidthScale * 100:0}%";
        _hVal.Text = $"{_overlay.Settings.HeightScale * 100:0}%";
        _sVal.Text = $"{_overlay.Settings.Sensitivity / 2.5 * 100:0}%";   // 100% = the normal (punchy) 2.5x
        _bVal.Text = $"{_overlay.Settings.Bars}";
        _dVal.Text = $"{_overlay.Settings.DragStrength}x";
    }

    void StyleBtn(WinForms.Button b, bool active, Color acc)
    {
        b.BackColor = active ? Color.FromArgb(40, 40, 44) : Face;
        b.ForeColor = active ? Fg : Sub;
        b.FlatAppearance.BorderColor = active ? acc : Line;
        b.FlatAppearance.BorderSize = active ? 2 : 1;
    }

    static Color Parse(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); } catch { return Color.White; }
    }

    WinForms.Button Btn(string text, int x, int y, int w, int h)
    {
        var b = new WinForms.Button
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
            FlatStyle = WinForms.FlatStyle.Flat, BackColor = Face, ForeColor = Fg,
            Font = new Font("Segoe UI", 10f), TabStop = false,
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 54);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(64, 64, 70);
        return b;
    }

    WinForms.Button Swatch(string hex, int x, int y)
    {
        var b = new WinForms.Button
        {
            Location = new Point(x, y), Size = new Size(34, 34),
            FlatStyle = WinForms.FlatStyle.Flat, BackColor = Parse(hex), TabStop = false,
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Line;
        return b;
    }

    protected override void OnPaintBackground(WinForms.PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }
}
