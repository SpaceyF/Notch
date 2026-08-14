using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace Notch;

// one dark settings window, split into a left nav rail and a content panel so it's
// not one giant scroll of checkboxes. opens from the tray. changes apply right away.
sealed class SettingsForm : WinForms.Form
{
    readonly Overlay _overlay;
    WinForms.Label _wVal = null!, _hVal = null!, _sVal = null!, _dVal = null!, _bVal = null!, _gVal = null!, _rVal = null!, _aVal = null!, _tVal = null!, _txVal = null!;
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
        BuildVoice();
        BuildJokes();

        // the left nav rail
        var rail = new WinForms.Panel { Location = new Point(0, 0), Size = new Size(116, 452), BackColor = Rail };
        Controls.Add(rail);
        string[] names = { "Look", "Media", "Alerts", "Devices", "System", "Voice", "Jokes" };
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
        Row(p, "Size", 76, () => StepArtSize(-0.05), () => StepArtSize(+0.05), out _aVal);
        Row(p, "Tune ↕", 116, () => StepArtNudge(-1), () => StepArtNudge(+1), out _tVal);
        Row(p, "Tune ↔", 156, () => StepArtNudgeX(-1), () => StepArtNudgeX(+1), out _txVal);

        Section(p, "Visualizer", 196);
        Row(p, "React", 220, () => StepSens(-0.25), () => StepSens(+0.25), out _sVal);
        Row(p, "Bars", 260, () => StepBars(-1), () => StepBars(+1), out _bVal);
        p.Controls.Add(new WinForms.Label { Text = "Style", ForeColor = Sub, AutoSize = true, Location = new Point(2, 304), BackColor = Bg });
        var vis = new WinForms.ComboBox
        {
            DropDownStyle = WinForms.ComboBoxStyle.DropDownList, Location = new Point(56, 300), Size = new Size(248, 24),
            BackColor = Face, ForeColor = Fg, FlatStyle = WinForms.FlatStyle.Flat,
        };
        vis.Items.AddRange(new object[] { "Bars (from center)", "Bars (classic)", "Dots (9x9)" });
        vis.SelectedIndex = _overlay.Settings.Visual switch { VisualStyle.Centered => 0, VisualStyle.Bars => 1, VisualStyle.Dots => 2, _ => 0 };
        vis.SelectedIndexChanged += (s, e) => _overlay.SetVisual(vis.SelectedIndex switch { 0 => VisualStyle.Centered, 1 => VisualStyle.Bars, 2 => VisualStyle.Dots, _ => VisualStyle.Centered });
        p.Controls.Add(vis);

        Section(p, "Effects", 338);
        Check(p, "Weather on the notch (rain / snow)", 362, _overlay.Settings.WeatherFx, v => _overlay.SetWeatherFx(v));
        Check(p, "Confetti on big moments", 388, _overlay.Settings.Confetti, v => _overlay.SetConfetti(v));
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

    void BuildVoice()
    {
        var p = Page();
        Section(p, "Hey Siri", 0);
        WinForms.CheckBox border = null!;
        var siri = new WinForms.CheckBox
        {
            Text = "Listen for “Hey Siri”", ForeColor = Fg, BackColor = Bg, AutoSize = true,
            Location = new Point(2, 24), Checked = _overlay.Settings.HeySiri, FlatStyle = WinForms.FlatStyle.Flat,
        };
        siri.CheckedChanged += (s, e) => { _overlay.SetHeySiri(siri.Checked); border.Enabled = siri.Checked; };
        p.Controls.Add(siri);

        border = new WinForms.CheckBox
        {
            Text = "Glowing screen border while listening", ForeColor = Fg, BackColor = Bg, AutoSize = true,
            Location = new Point(2, 50), Checked = _overlay.Settings.SiriBorder, FlatStyle = WinForms.FlatStyle.Flat,
            Enabled = _overlay.Settings.HeySiri,
        };
        border.CheckedChanged += (s, e) => _overlay.SetSiriBorder(border.Checked);
        p.Controls.Add(border);

        Row(p, "Glow", 78, () => StepGlow(-0.25), () => StepGlow(+0.25), out _gVal);

        Section(p, "Talk back", 128);
        Check(p, "Talk back out loud", 152, _overlay.Settings.SiriVoice, v => _overlay.SetSiriVoice(v));
        p.Controls.Add(new WinForms.Label { Text = "Voice", ForeColor = Sub, AutoSize = true, Location = new Point(2, 184), BackColor = Bg });
        var combo = new WinForms.ComboBox
        {
            DropDownStyle = WinForms.ComboBoxStyle.DropDownList, Location = new Point(56, 180), Size = new Size(248, 24),
            BackColor = Face, ForeColor = Fg, FlatStyle = WinForms.FlatStyle.Flat,
        };
        foreach (var name in _overlay.VoiceNames()) combo.Items.Add(name);
        combo.SelectedItem = string.IsNullOrEmpty(_overlay.Settings.SiriVoiceName) ? _overlay.CurrentVoiceName() : _overlay.Settings.SiriVoiceName;
        combo.SelectedIndexChanged += (s, e) => { if (combo.SelectedItem is string n) _overlay.SetSiriVoiceName(n); };   // attached after selecting, so no preview on open
        p.Controls.Add(combo);
        Row(p, "Speed", 214, () => StepRate(-1), () => StepRate(+1), out _rVal);

        Section(p, "Commands", 258);
        Check(p, "Sharper free-text (Whisper)", 282, _overlay.Settings.UseWhisper, v => _overlay.SetUseWhisper(v));
        var see = Btn("See all commands…", 0, 312, 304, 30);
        see.Click += (s, e) => ShowCommands();
        p.Controls.Add(see);
    }

    CommandsForm? _cmds;
    void ShowCommands()
    {
        if (_cmds == null || _cmds.IsDisposed)
        {
            _cmds = new CommandsForm();
            _cmds.FormClosed += (s, e) => _cmds = null;
            _cmds.Show();
        }
        _cmds.Activate();
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

        Section(p, "RGB mode", 100);
        Check(p, "RGB Siri (rainbow orb)", 124, _overlay.Settings.RgbSiri, v => _overlay.SetRgbSiri(v));
        Check(p, "RGB Siri borders (rainbow frame)", 150, _overlay.Settings.RgbSiriBorder, v => _overlay.SetRgbSiriBorder(v));
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

    void StepGlow(double delta) { _overlay.SetSiriBorderSize(_overlay.Settings.SiriBorderSize + delta); Sync(); }

    void StepRate(int delta) { _overlay.SetSiriRate(_overlay.Settings.SiriRate + delta); Sync(); }

    void StepArtSize(double delta) { _overlay.SetArtScale(_overlay.Settings.ArtScale + delta); Sync(); }

    void StepArtNudge(double delta) { _overlay.SetArtNudge(_overlay.Settings.ArtNudge + delta); Sync(); }

    void StepArtNudgeX(double delta) { _overlay.SetArtNudgeX(_overlay.Settings.ArtNudgeX + delta); Sync(); }

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
        _gVal.Text = $"{_overlay.Settings.SiriBorderSize * 100:0}%";
        _rVal.Text = $"{_overlay.Settings.SiriRate:+0;-0;0}";
        _aVal.Text = $"{_overlay.Settings.ArtScale * 100:0}%";
        _tVal.Text = $"{_overlay.Settings.ArtNudge:+0;-0;0}px";
        _txVal.Text = $"{_overlay.Settings.ArtNudgeX:+0;-0;0}px";
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
