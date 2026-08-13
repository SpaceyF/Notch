using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
// winforms is used for the tray, so these names clash. lock them to wpf here.
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Cursors = System.Windows.Input.Cursors;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using Path = System.IO.Path;

namespace Notch;

enum HoverTab { Media, Timer, Stats, Shelf, Clipboard, Apps, Calc, Translate, Net }

public partial class Overlay : Window
{
    const int BarCount = 18;
    // the normal sizes. the width and height settings multiply these.
    const double BaseExpanded = 470, BaseCompactIsland = 150, BaseCompactNotch = 200, BaseHeight = 40, BaseBarMax = 22;
    const double BaseCompactIos = 125, BaseExpandedIos = 190;

    NotchSettings _settings = NotchSettings.Load();
    readonly NowPlaying _np = new();
    AudioLevels? _audio;
    readonly List<Rectangle> _bars = new();
    readonly List<Ellipse> _dots = new();          // 9x9 expanding-dot visualizer
    readonly List<ScaleTransform> _dotScales = new();
    readonly DispatcherTimer _ui = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer _top = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(40) };
    bool _expanded;
    double _compact;          // how wide when closed
    double _expandedW;        // how wide when open
    double _barMax = BaseBarMax;
    IntPtr _hwnd;
    bool _clickThrough = true;
    bool _dragging;
    bool _hovering;
    int _dotTick;
    Point _dragStart;
    SolidColorBrush? _artBrush;   // color taken from the current album art
    byte[]? _lastArt;
    double _ws = 1, _hs = 1;      // current width/height scale, kept for the hover width

    // hover menu + its tabs
    const double BaseHoverWidth = 470;
    HoverTab _tab = HoverTab.Media;
    bool _menuOpen;               // the drop menu is a right-click toggle now, not hover
    readonly DispatcherTimer _stats = new() { Interval = TimeSpan.FromSeconds(1) };
    long _pIdle, _pKernel, _pUser;
    readonly List<PerformanceCounter> _gpuCounters = new();
    bool _gpuBuilt;

    // countdown timer that lives in the timer tab
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    TimeSpan _timerLen = TimeSpan.FromMinutes(5);
    TimeSpan _timerLeft;
    bool _timerRunning;

    // notification / copied pops
    readonly Notifications _notes = new();
    readonly DispatcherTimer _noteTimer = new();
    bool _noteActive;

    // download ring: takes over the album art slot while a browser download runs
    readonly Downloads _downloads = new();
    readonly DispatcherTimer _dlEndTimer = new();
    bool _dlActive;      // a download is in progress and owning the art slot
    bool _dlDoneFlash;   // showing the finished checkmark for a beat

    // recording pill: red REC + running clock while something is capturing the screen
    readonly DispatcherTimer _recClock = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _recActive;
    bool _recForce;      // debug: force the pill on for a few seconds
    DateTime _recStart;

    // "hey siri" voice commands. the orb drops out of the notch as a mini live activity;
    // the glowing screen border is its own edge window
    readonly Voice _voice = new();
    readonly DispatcherTimer _siriHide = new();
    readonly System.Speech.Synthesis.SpeechSynthesizer _tts = new();
    bool _siriOpen;
    SiriBorderWindow? _border;

    // airdrop-style "file landed" card: a watcher fires when a finished file appears in a watched folder
    readonly Incoming _incoming = new();
    readonly DispatcherTimer _dropTimer = new();
    string _dropPath = "";
    bool _dropCardOpen;

    // apple-style media live activity (opens on a click while music plays)
    readonly DispatcherTimer _media = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _mediaOpen;
    bool _seeking;
    readonly List<Rectangle> _mediaBars = new();
    DateTime _downTime;   // to tell a tap from a drag on the pill

    // calculator tab
    string _calcExpr = "";
    TextBlock _calcDisplay = null!;
    bool _calcResult;   // the display is showing a result, next digit starts fresh

    // translator tab (translates the current clipboard text; no typing = no focus fights)
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    string _transTarget = "en";
    string _transSrc = "";

    // network speed tab
    readonly DispatcherTimer _net = new() { Interval = TimeSpan.FromSeconds(1) };
    long _netRx, _netTx, _netAt;
    readonly List<Rectangle> _netBars = new();
    readonly Queue<double> _netHist = new();

    // weather-reactive particles + confetti bursts (both off by default)
    readonly Weather _weather = new();
    readonly DispatcherTimer _wx = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly List<WxDrop> _wxDrops = new();
    Sky _sky = Sky.Unknown;
    readonly DispatcherTimer _confetti = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly List<Confetto> _confetti_ps = new();

    // file shelf + clipboard history (both in-memory) and pinned apps (saved)
    readonly List<string> _shelf = new();
    readonly List<string> _clips = new();
    bool _selfClip;   // ignore the clipboard change we cause when re-copying from history

    // "device connected" 3D card
    readonly DispatcherTimer _devTimer = new();
    bool _lastAc;
    bool _vrConnected, _vrInit;   // virtual desktop headset connected (streaming), not just open
    int _vrTick;
    // one physical plug fires several interface events; coalesce them into one card
    readonly DispatcherTimer _devBurst = new();
    DevSpec _burstSpec;
    bool _burstArrived, _burstPending;
    IntPtr _devNotify;

    // what to show for a plugged-in device: which model, its label, body color + length,
    // plus the raw vid/pid (shown on the card for now so we can pin devices to models)
    readonly record struct DevSpec(DeviceKind Kind, string Title, int Prio, Color Body, double Len, string Info);
    static readonly Color CBlack = Color.FromRgb(0x24, 0x24, 0x28);
    static readonly Color CWhite = Color.FromRgb(0xEC, 0xEC, 0xEE);
    static readonly Color CGray = Color.FromRgb(0x8A, 0x90, 0x98);

    public Overlay()
    {
        InitializeComponent();
        ApplyStyle();
        _ui.Tick += (s, e) => PaintBars();
        _top.Tick += (s, e) => { UpdateFullscreen(); if (IsVisible) Reassert(); UpdateDots(); UpdateVr(); UpdateRecording(); };
        _hover.Tick += (s, e) => HoverTick();

        // clip the pill to its real rounded shape (not just its bounding box) so blurred art,
        // weather particles, etc. don't poke out into the transparent corners
        Shape.SizeChanged += (s, e) => ClipShape();

        // grab and stretch: press the notch and drag to pull it, like on an iphone
        Shape.MouseLeftButtonDown += GrabDown;
        Shape.MouseMove += GrabMove;
        Shape.MouseLeftButtonUp += GrabUp;

        // right-click toggles the drop menu; it stays open until you right-click again
        Shape.MouseRightButtonUp += (s, e) => { ToggleMenu(); e.Handled = true; };

        // drop a file right onto the notch: preview it for a beat, then it's in the shelf
        Shape.DragOver += ShelfDragOver;
        Shape.Drop += PillDrop;

        // media tab talks to whatever app is playing
        PrevBtn.Click += (s, e) => _np.Prev();
        PlayBtn.Click += (s, e) => _np.TogglePlayPause();
        NextBtn.Click += (s, e) => _np.Next();

        // apple-style media live activity controls
        MShuffle.Click += (s, e) => _np.ToggleShuffle();
        MPrev.Click += (s, e) => _np.Prev();
        MPlay.Click += (s, e) => _np.TogglePlayPause();
        MNext.Click += (s, e) => _np.Next();
        MRepeat.Click += (s, e) => _np.CycleRepeat();
        MScrub.MouseLeftButtonDown += ScrubDown;
        MScrub.MouseMove += ScrubMove;
        MScrub.MouseLeftButtonUp += ScrubUp;
        _media.Tick += (s, e) => UpdateMediaCard();
        BuildMediaBars();

        // hover menu tabs
        TabMedia.Click += (s, e) => SwitchTab(HoverTab.Media);
        TabTimer.Click += (s, e) => SwitchTab(HoverTab.Timer);
        TabStats.Click += (s, e) => SwitchTab(HoverTab.Stats);
        TabShelf.Click += (s, e) => SwitchTab(HoverTab.Shelf);
        TabClip.Click += (s, e) => SwitchTab(HoverTab.Clipboard);
        TabApps.Click += (s, e) => SwitchTab(HoverTab.Apps);
        TabCalc.Click += (s, e) => SwitchTab(HoverTab.Calc);
        TabTrans.Click += (s, e) => SwitchTab(HoverTab.Translate);
        TabNet.Click += (s, e) => SwitchTab(HoverTab.Net);
        BuildCalc();
        BuildTransLangs();
        _net.Tick += (s, e) => UpdateNet();
        _wx.Tick += (s, e) => WeatherTick();
        _confetti.Tick += (s, e) => ConfettiTick();
        _weather.Changed += () => Dispatcher.BeginInvoke(() => { _sky = _weather.Condition; ApplyWeather(); });
        ShelfPanel.DragOver += ShelfDragOver;
        ShelfPanel.Drop += ShelfDrop;
        RebuildShelf();
        RebuildApps();

        // timer tab
        TMinus.Click += (s, e) => AdjustTimer(-1);
        TPlus.Click += (s, e) => AdjustTimer(+1);
        TStart.Click += (s, e) => ToggleTimer();
        TReset.Click += (s, e) => ResetTimer();
        _timer.Tick += (s, e) => TimerTick();
        _stats.Tick += (s, e) => UpdateStats();
        GetSystemTimes(out _pIdle, out _pKernel, out _pUser);
        _timerLeft = _timerLen;
        UpdateTimerText();
        SwitchTab(HoverTab.Media);

        _devTimer.Tick += (s, e) => HideDeviceCard();
        _devBurst.Tick += (s, e) => DeviceBurstTick();

        // a notification or a copied-text flash briefly takes over the pill
        _noteTimer.Tick += (s, e) => DismissNote();
        _notes.Popped += (app, title, body) =>
        {
            if (!_settings.ShowNotes) return;
            string t = string.IsNullOrWhiteSpace(title) ? app : title;
            string b = string.IsNullOrWhiteSpace(body) ? app : body;
            PopMessage("\uE7E7", t, b, 4.5);   // bell for notifications
        };

        _np.Changed += () => Dispatcher.BeginInvoke(UpdateTrack);

        // download ring: the watcher runs off-thread, so bounce every event to the ui
        _downloads.Active += (name, bytes) => Dispatcher.BeginInvoke(() => ShowDownload(name, bytes));
        _downloads.Done   += name => Dispatcher.BeginInvoke(() => DownloadDone(name));
        _downloads.AllDone += () => Dispatcher.BeginInvoke(EndDownload);
        _dlEndTimer.Tick += (s, e) => ClearDownload();

        _recClock.Tick += (s, e) => UpdateRecClock();

        // "hey siri": the recognizer fires on a background thread, bounce it to the ui
        _voice.Wakened += () => Dispatcher.BeginInvoke(OnWake);
        _voice.Command += key => Dispatcher.BeginInvoke(() => RunVoiceCommand(key));
        _voice.Dynamic += (kind, text) => Dispatcher.BeginInvoke(() => RunDynamic(kind, text));
        _voice.Dismissed += () => Dispatcher.BeginInvoke(() => { HideSiriAfter(1.0); _border?.HideSoon(1.0); });
        _voice.Level += lvl => Dispatcher.BeginInvoke(() => OrbCtl.SetLevel(lvl));
        _siriHide.Tick += (s, e) => HideSiri();

        // airdrop card: the watcher runs off-thread, so bounce the event to the ui
        _incoming.Landed += path => Dispatcher.BeginInvoke(() => ShowDrop(path));
        _dropTimer.Tick += (s, e) => HideDropCard();
        DropOpen.Click += (s, e) => { OpenPath(_dropPath); HideDropCard(); };
        DropShow.Click += (s, e) => { RevealInFolder(_dropPath); HideDropCard(); };

        Loaded += async (s, e) =>
        {
            Position();
            _top.Start();
            _hover.Start();
            _audio = new AudioLevels(BarCount);
            _audio.SetSensitivity(_settings.Sensitivity);
            if (_settings.ShowDownloadRing) _downloads.Start();
            if (_settings.ShowAirdrop) _incoming.Start(AirdropFolders());
            if (_settings.WeatherFx) _weather.Start();
            if (_settings.HeySiri) _voice.Start();
            try { await _np.Start(); } catch { }
            try { await _notes.Start(); } catch { }
        };
    }

    // ---------------------------------------------------------------- style
    public void ApplyStyle()
    {
        double ws = Clamp(_settings.WidthScale, 0.6, 2.0);
        double hs = Clamp(_settings.HeightScale, 0.6, 2.5);
        _ws = ws; _hs = hs;

        double h = BaseHeight * hs;
        bool ios = _settings.IosLayout;

        // the style is just the shape: island floats, notch hugs the top edge
        if (_settings.Style == NotchStyle.Island)
        {
            Shape.CornerRadius = new CornerRadius(20 * hs);
            Shape.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            Shape.CornerRadius = new CornerRadius(0, 0, 22, 22);
            Shape.Margin = new Thickness(0);
        }

        // ios layout is a separate toggle: narrower pill, blank middle, small square of bars
        bool dots = _settings.Visual == VisualStyle.Dots;
        if (ios)
        {
            int n = Math.Clamp(_settings.Bars, 3, 10);
            _compact = BaseCompactIos * ws;
            // grow the pill outward so the visualizer fits, instead of cramming it in
            _expandedW = (BaseCompactIos + (dots ? 60 : n * 6.5 + 40)) * ws;
            BuildBars(n, 4, 2.5, 2);   // bolder bars, honoring the count setting
        }
        else
        {
            _compact = (_settings.Style == NotchStyle.Island ? BaseCompactIsland : BaseCompactNotch) * ws;
            _expandedW = BaseExpanded * ws;
            BuildBars(Math.Clamp(_settings.Bars, 3, 10), 3, 2, 1.5);
        }
        BuildDots(hs);
        _barMax = BaseBarMax * hs;

        Shape.Height = h;

        // size the album art to the pill's height so it fills it and stays centered, however
        // short/tall the notch is (e.g. mac-notch dimensions ~110% wide, 65% tall)
        double art = Math.Max(16, Math.Round(h * 0.82));
        double ar = art * 0.26;
        ArtHost.Width = ArtHost.Height = art;
        ArtHost.VerticalAlignment = VerticalAlignment.Center;
        ArtHost.CornerRadius = new CornerRadius(ar);
        ArtHost.Clip = new RectangleGeometry(new Rect(0, 0, art, art), ar, ar);   // actually round the image
        NoteImageHost.Width = NoteImageHost.Height = art;
        NoteImageHost.VerticalAlignment = VerticalAlignment.Center;
        NoteImageHost.Clip = new RectangleGeometry(new Rect(0, 0, art, art), ar, ar);
        DlRing.LayoutTransform = new ScaleTransform(art / 30.0, art / 30.0);   // keep the ring in scale too
        BarHost.Height = 24 * hs;

        // stop any running width animation, then set the right width for now
        Shape.BeginAnimation(WidthProperty, null);
        Shape.Width = _expanded ? _expandedW : _compact;

        ApplyBarColor();
        RefreshContent();
        ClipShape();   // corner radius may have changed even if the size didn't
    }

    // clip the pill to its rounded outline so nothing (blurred art, particles) bleeds into
    // the transparent corners. tracks the live width and whatever corners are set right now.
    void ClipShape()
    {
        double w = Shape.ActualWidth, h = Shape.ActualHeight;
        if (w <= 0 || h <= 0) return;
        Shape.Clip = RoundedGeo(w, h, Shape.CornerRadius);
    }

    static Geometry RoundedGeo(double w, double h, CornerRadius c)
    {
        double lim = Math.Min(w, h) / 2;
        double tl = Math.Min(c.TopLeft, lim), tr = Math.Min(c.TopRight, lim);
        double br = Math.Min(c.BottomRight, lim), bl = Math.Min(c.BottomLeft, lim);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(tl, 0), true, true);
            ctx.LineTo(new Point(w - tr, 0), true, false);
            if (tr > 0) ctx.ArcTo(new Point(w, tr), new System.Windows.Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w, h - br), true, false);
            if (br > 0) ctx.ArcTo(new Point(w - br, h), new System.Windows.Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(bl, h), true, false);
            if (bl > 0) ctx.ArcTo(new Point(0, h - bl), new System.Windows.Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0, tl), true, false);
            if (tl > 0) ctx.ArcTo(new Point(tl, 0), new System.Windows.Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    // the bars use the album art color while music plays (if that's turned on),
    // otherwise they use the accent color chosen in settings
    void ApplyBarColor()
    {
        SolidColorBrush accent;
        if (_settings.AutoAccent && _artBrush != null && _expanded)
            accent = _artBrush;
        else { try { accent = Brush(_settings.Accent); } catch { accent = Brush("#3DD6C4"); } }
        foreach (var b in _bars) b.Fill = accent;
        foreach (var d in _dots) d.Fill = accent;
    }

    static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    // (re)build the visualizer bars. ios uses a few wide bars to make a small
    // square, the other styles use a longer row of thin ones.
    void BuildBars(int count, double width, double gap, double radius)
    {
        BarHost.Children.Clear();
        _bars.Clear();
        for (int i = 0; i < count; i++)
        {
            var r = new Rectangle
            {
                Width = width, Height = 2, RadiusX = radius, RadiusY = radius,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, gap, 0),
                Fill = Brush(_settings.Accent),
            };
            _bars.Add(r);
            BarHost.Children.Add(r);
        }
    }

    // build the 9x9 dot matrix. each dot has a scale transform so it can expand with the audio.
    void BuildDots(double hs)
    {
        DotHost.Children.Clear();
        _dots.Clear();
        _dotScales.Clear();
        double side = 30 * hs;
        DotHost.Width = DotHost.Height = side;
        double dot = Math.Max(2, side / 9 * 0.66);
        for (int i = 0; i < 81; i++)
        {
            var sc = new ScaleTransform(0.4, 0.4);
            var e = new Ellipse
            {
                Width = dot, Height = dot, Fill = Brush(_settings.Accent), Opacity = 0.3,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = sc,
            };
            _dots.Add(e); _dotScales.Add(sc); DotHost.Children.Add(e);
        }
    }

    // each column is a frequency band; dots expand up from the bottom as that band gets louder
    void PaintDots(float[] lv)
    {
        for (int c = 0; c < 9; c++)
        {
            int a = c * lv.Length / 9, b = Math.Max(a + 1, (c + 1) * lv.Length / 9);
            double m = 0;
            for (int k = a; k < b && k < lv.Length; k++) if (lv[k] > m) m = lv[k];
            for (int row = 0; row < 9; row++)
            {
                int idx = row * 9 + c;
                double inten = Math.Clamp(m * 9 - (8 - row), 0, 1);   // row 8 is the bottom
                _dotScales[idx].ScaleX = _dotScales[idx].ScaleY = 0.4 + inten * 0.6;
                _dots[idx].Opacity = 0.22 + inten * 0.78;
            }
        }
    }

    static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    // ---------------------------------------------------------------- art color
    // find one bright, colorful shade that represents the album art.
    // shrink it small, group the pixels by color, favor the bold colorful ones,
    // pick the biggest group, then brighten it so it always stands out.
    static Color? Dominant(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var src = new System.Drawing.Bitmap(ms);
            using var small = new System.Drawing.Bitmap(24, 24);
            using (var g = System.Drawing.Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, 24, 24);
            }
            double[] r = new double[12], gr = new double[12], b = new double[12], w = new double[12];
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                {
                    var p = small.GetPixel(x, y);
                    if (p.A < 128) continue;
                    RgbToHsv(p.R, p.G, p.B, out double h, out double s, out double v);
                    if (s < 0.18 || v < 0.12 || v > 0.97) continue;   // ignore greys and anything near black or white
                    int bin = ((int)(h / 30)) % 12;
                    double wt = s * Math.Min(v, 0.9);
                    r[bin] += p.R * wt; gr[bin] += p.G * wt; b[bin] += p.B * wt; w[bin] += wt;
                }
            int best = -1; double bw = 0;
            for (int i = 0; i < 12; i++) if (w[i] > bw) { bw = w[i]; best = i; }
            if (best < 0) return null;
            return Vivid(
                (int)(r[best] / w[best]), (int)(gr[best] / w[best]), (int)(b[best] / w[best]));
        }
        catch { return null; }
    }

    static Color Vivid(int r, int g, int b)
    {
        RgbToHsv(r, g, b, out double h, out double s, out double v);
        s = Clamp(s * 1.35, 0.55, 1.0);
        v = Clamp(Math.Max(v, 0.72), 0.0, 1.0);
        HsvToRgb(h, s, v, out int rr, out int gg, out int bb);
        return Color.FromRgb((byte)rr, (byte)gg, (byte)bb);
    }

    static void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
    {
        double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
        double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
        double d = max - min; v = max; s = max <= 0 ? 0 : d / max; h = 0;
        if (d <= 0) return;
        if (max == rr) h = 60 * (((gg - bb) / d) % 6);
        else if (max == gg) h = 60 * (((bb - rr) / d) + 2);
        else h = 60 * (((rr - gg) / d) + 4);
        if (h < 0) h += 360;
    }

    static void HsvToRgb(double h, double s, double v, out int r, out int g, out int b)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double rr = 0, gg = 0, bb = 0;
        if (h < 60) { rr = c; gg = x; }
        else if (h < 120) { rr = x; gg = c; }
        else if (h < 180) { gg = c; bb = x; }
        else if (h < 240) { gg = x; bb = c; }
        else if (h < 300) { rr = x; bb = c; }
        else { rr = c; bb = x; }
        r = (int)Math.Round((rr + m) * 255);
        g = (int)Math.Round((gg + m) * 255);
        b = (int)Math.Round((bb + m) * 255);
    }

    // ---------------------------------------------------------------- now playing
    void UpdateTrack()
    {
        if (_dlActive || _recActive) return;   // the download / recording activity owns the pill right now
        bool has = _np.Playing && !string.IsNullOrWhiteSpace(_np.Title);
        if (has)
        {
            TitleText.Text = _np.Title;
            ArtistText.Text = _np.Artist;
            if (_np.ArtBytes != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new MemoryStream(_np.ArtBytes);
                    bmp.EndInit();
                    bmp.Freeze();
                    Art.Source = bmp;
                    FrostBrush.ImageSource = bmp;   // same art, used blurred behind the pill
                }
                catch { }

                // only work out the color again when the art actually changes
                if (!ReferenceEquals(_np.ArtBytes, _lastArt))
                {
                    _lastArt = _np.ArtBytes;
                    var c = Dominant(_np.ArtBytes);
                    _artBrush = c is Color col ? Freeze(new SolidColorBrush(col)) : null;
                }
            }
            else { _artBrush = null; _lastArt = null; Art.Source = null; FrostBrush.ImageSource = null; }
            UpdatePlayGlyph();
            if (!_mediaOpen) Expand();   // don't fight the media card's width while it's open
            ApplyBarColor();
        }
        else if (!_mediaOpen) Collapse();
    }

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        RefreshContent();
        Animate(_expandedW);
        _ui.Start();
    }

    void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _ui.Stop();
        RefreshContent();
        if (_dlActive) return;   // stay wide while a download is showing its ring
        Animate(_compact);
    }

    // decide what's visible in the pill. the hover menu is a separate panel below,
    // so it doesn't touch this. the blurred art sits behind when frosted art is on.
    void RefreshContent()
    {
        RecBadge.Visibility = Visibility.Collapsed;   // on only in the recording branch below
        DotHost.Visibility = Visibility.Collapsed;    // on only in the normal branch, in dot mode

        // a notification / copied / file pop takes over the whole pill while it's up.
        // the pop method picks the glyph vs the file preview, so we leave those alone here.
        if (_noteActive)
        {
            NotePanel.Visibility = Visibility.Visible;
            ArtHost.Visibility = Visibility.Collapsed;
            Info.Visibility = Visibility.Collapsed;
            BarHost.Visibility = Visibility.Collapsed;
            DlRing.Visibility = Visibility.Collapsed;   // a pop takes over: don't leave the ring underneath it
            Frost.Visibility = Visibility.Collapsed;
            FrostShade.Visibility = Visibility.Collapsed;
            return;
        }
        NoteIcon.Visibility = Visibility.Collapsed;
        NoteImageHost.Visibility = Visibility.Collapsed;
        NotePanel.Visibility = Visibility.Collapsed;

        // a running download shows the ring in the album-art slot on the LEFT, like the art
        // sliding out. compact just hides the name/size text; full keeps it in the middle.
        if (_dlActive)
        {
            DlRing.Visibility = Visibility.Visible;
            ArtHost.Visibility = Visibility.Collapsed;
            Info.Visibility = _settings.DownloadRingCompact ? Visibility.Collapsed : Visibility.Visible;
            BarHost.Visibility = Visibility.Collapsed;
            Frost.Visibility = Visibility.Collapsed;
            FrostShade.Visibility = Visibility.Collapsed;
            return;
        }
        DlRing.Visibility = Visibility.Collapsed;

        // while the media activity is open, the pill is just the black top bar of one shape
        if (_mediaOpen)
        {
            ArtHost.Visibility = Visibility.Collapsed;
            Info.Visibility = Visibility.Collapsed;
            BarHost.Visibility = Visibility.Collapsed;
            Frost.Visibility = Visibility.Collapsed;
            FrostShade.Visibility = Visibility.Collapsed;
            return;
        }

        // the screen is being captured: red dot on the left, "Recording" + a running clock
        if (_recActive)
        {
            RecBadge.Visibility = Visibility.Visible;
            ArtHost.Visibility = Visibility.Collapsed;
            Info.Visibility = Visibility.Visible;
            BarHost.Visibility = Visibility.Collapsed;
            Frost.Visibility = Visibility.Collapsed;
            FrostShade.Visibility = Visibility.Collapsed;
            return;
        }

        bool ios = _settings.IosLayout;
        bool dots = _settings.Visual == VisualStyle.Dots;
        ArtHost.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        BarHost.Visibility = (_expanded && !dots) ? Visibility.Visible : Visibility.Collapsed;
        DotHost.Visibility = (_expanded && dots) ? Visibility.Visible : Visibility.Collapsed;
        Info.Visibility = (_expanded && !ios) ? Visibility.Visible : Visibility.Collapsed;

        bool frost = _settings.FrostedArt && _expanded && Art.Source != null;
        Frost.Visibility = frost ? Visibility.Visible : Visibility.Collapsed;
        FrostShade.Visibility = frost ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- pops (notifications + copied)
    // briefly show a message in the pill, then go back to whatever it was showing
    void PopMessage(string icon, string title, string body, double seconds)
    {
        NoteIcon.Text = string.IsNullOrEmpty(icon) ? "\uE7E7" : icon;   // default is a bell
        NoteIcon.Visibility = Visibility.Visible;
        NoteImageHost.Visibility = Visibility.Collapsed;
        NoteTitle.Text = title;
        NoteBody.Text = body;
        _noteActive = true;
        RefreshContent();
        Animate(BaseExpanded * _ws);
        _noteTimer.Stop();
        _noteTimer.Interval = TimeSpan.FromSeconds(seconds);
        _noteTimer.Start();
    }

    void DismissNote()
    {
        _noteTimer.Stop();
        _noteActive = false;
        RefreshContent();
        Animate(BaseWidth);   // back to the download ring, music, or the idle pill
    }

    // fired by windows whenever the clipboard changes
    void OnClipboard()
    {
        if (_selfClip) { _selfClip = false; return; }   // our own re-copy from history
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return;
            string t = System.Windows.Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(t)) return;
            AddClip(t);
            if (!_settings.ShowCopied) return;
            t = t.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > 44) t = t.Substring(0, 44) + "…";
            PopMessage("\uE8C8", "Copied", t, 1.6);   // copy glyph
        }
        catch { }
    }

    // ---------------------------------------------------------------- download ring
    // how wide the pill sits while a download shows: the normal idle pill plus room for
    // the ring on the left (like just the album art popping out), or full width with text
    double DlActiveWidth => _settings.DownloadRingCompact ? _compact + 48 * _ws : _expandedW;

    // how wide the pill sits while recording: idle pill plus room for "Recording  0:12"
    double RecWidth => _compact + 150 * _ws;

    // what the pill's width should be right now, ignoring a note pop (which sizes itself).
    // download owns the pill over recording, which owns it over the idle music/blank state
    double BaseWidth => _dlActive ? DlActiveWidth : _recActive ? RecWidth : (_expanded ? _expandedW : _compact);

    // called every poll while a download runs: keep the ring up and refresh the size
    void ShowDownload(string name, long bytes)
    {
        if (!_settings.ShowDownloadRing) return;
        bool first = !_dlActive;
        _dlActive = true;
        _dlDoneFlash = false;
        _dlEndTimer.Stop();

        DlDoneFill.Visibility = Visibility.Collapsed;
        DlCheck.Visibility = Visibility.Collapsed;
        DlArc.Visibility = Visibility.Visible;
        DlArc.Stroke = CurrentAccent();
        TitleText.Text = "Downloading";
        ArtistText.Text = $"{Shorten(name)}   {FormatBytes(bytes)}";

        if (first)
        {
            RefreshContent();
            if (!_noteActive) Animate(DlActiveWidth);   // a note pop, if up, restores width itself
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever };
            DlSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
    }

    // a partial finished and became a real file: flash a checkmark in the ring
    void DownloadDone(string name)
    {
        if (!_dlActive) return;
        _dlDoneFlash = true;
        DlSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        DlArc.Visibility = Visibility.Collapsed;
        DlDoneFill.Fill = CurrentAccent();
        DlDoneFill.Visibility = Visibility.Visible;
        DlCheck.Visibility = Visibility.Visible;
        TitleText.Text = "Downloaded";
        ArtistText.Text = Shorten(name);
        Burst();   // confetti on a finished download (if it's turned on)
    }

    // the last download left. if one just finished, linger the check; otherwise clear now
    void EndDownload()
    {
        if (!_dlActive) return;
        if (_dlDoneFlash)
        {
            _dlEndTimer.Stop();
            _dlEndTimer.Interval = TimeSpan.FromSeconds(1.8);
            _dlEndTimer.Start();
        }
        else ClearDownload();
    }

    void ClearDownload()
    {
        _dlEndTimer.Stop();
        _dlActive = false;
        _dlDoneFlash = false;
        DlSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        RefreshContent();
        UpdateTrack();   // decides _expanded from whether music is playing (no-ops while recording)
        // always snap the width back explicitly: music was already _expanded so UpdateTrack
        // wouldn't re-animate it, and the idle case needs to shrink off the download width
        if (!_noteActive) Animate(_recActive ? RecWidth : _expanded ? _expandedW : _compact);
    }

    static string Shorten(string s) => s.Length > 30 ? s.Substring(0, 29) + "\u2026" : s;

    static string FormatBytes(long b)
    {
        if (b < 1024) return $"{b} B";
        double kb = b / 1024.0;
        if (kb < 1024) return $"{kb:0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.0} MB";
        return $"{mb / 1024.0:0.00} GB";
    }

    void UpdatePlayGlyph() => PlayBtn.Content = _np.Playing ? "\uE769" : "\uE768";  // pause when playing, play when paused

    void Animate(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Shape.BeginAnimation(WidthProperty, a);
    }

    void PaintBars()
    {
        if (_audio == null) return;
        var lv = _audio.Snapshot();
        if (_settings.Visual == VisualStyle.Dots) PaintDots(lv);
        // when there are fewer bars than frequencies, each bar takes the loudest of its slice
        for (int i = 0; i < _bars.Count; i++)
        {
            int a = i * lv.Length / _bars.Count;
            int b = Math.Max(a + 1, (i + 1) * lv.Length / _bars.Count);
            double m = 0;
            for (int k = a; k < b && k < lv.Length; k++) if (lv[k] > m) m = lv[k];
            _bars[i].Height = 2 + m * _barMax;
        }

        // the little "now playing" equalizer in the media card
        if (_mediaOpen)
            for (int i = 0; i < _mediaBars.Count; i++)
            {
                int idx = (i + 1) * lv.Length / (_mediaBars.Count + 1);
                double v = idx < lv.Length ? lv[idx] : 0;
                _mediaBars[i].Height = 3 + v * 15;
            }
    }

    // show the mic and camera dots when something is using them
    void UpdateDots()
    {
        if ((++_dotTick % 3) != 0) return;   // only check about once a second
        if (!_settings.ShowDots) { if (Dots.Visibility != Visibility.Collapsed) Dots.Visibility = Visibility.Collapsed; return; }
        bool mic = Privacy.MicInUse, cam = Privacy.CamInUse;
        bool screen = Privacy.ScreenInUse && !_recActive;   // the REC pill already shows this
        MicDot.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        CamDot.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;
        ScreenDot.Visibility = screen ? Visibility.Visible : Visibility.Collapsed;
        Dots.Visibility = (mic || cam || screen) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- hover menu tabs
    void SwitchTab(HoverTab t)
    {
        _tab = t;
        MediaPanel.Visibility = t == HoverTab.Media ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = t == HoverTab.Timer ? Visibility.Visible : Visibility.Collapsed;
        StatsPanel.Visibility = t == HoverTab.Stats ? Visibility.Visible : Visibility.Collapsed;
        ShelfPanel.Visibility = t == HoverTab.Shelf ? Visibility.Visible : Visibility.Collapsed;
        ClipItems.Visibility = t == HoverTab.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        AppItems.Visibility = t == HoverTab.Apps ? Visibility.Visible : Visibility.Collapsed;
        CalcPanel.Visibility = t == HoverTab.Calc ? Visibility.Visible : Visibility.Collapsed;
        TransPanel.Visibility = t == HoverTab.Translate ? Visibility.Visible : Visibility.Collapsed;
        NetPanel.Visibility = t == HoverTab.Net ? Visibility.Visible : Visibility.Collapsed;

        var acc = CurrentAccent();
        TabMedia.Foreground = t == HoverTab.Media ? acc : Brushes.White;
        TabTimer.Foreground = t == HoverTab.Timer ? acc : Brushes.White;
        TabStats.Foreground = t == HoverTab.Stats ? acc : Brushes.White;
        TabShelf.Foreground = t == HoverTab.Shelf ? acc : Brushes.White;
        TabClip.Foreground = t == HoverTab.Clipboard ? acc : Brushes.White;
        TabApps.Foreground = t == HoverTab.Apps ? acc : Brushes.White;
        TabCalc.Foreground = t == HoverTab.Calc ? acc : Brushes.White;
        TabTrans.Foreground = t == HoverTab.Translate ? acc : Brushes.White;
        TabNet.Foreground = t == HoverTab.Net ? acc : Brushes.White;

        if (t == HoverTab.Clipboard) RebuildClips();
        if (t == HoverTab.Apps) RebuildApps();
        if (t == HoverTab.Shelf) RebuildShelf();
        if (t == HoverTab.Stats && _menuOpen) StartStats(); else StopStats();
        if (t == HoverTab.Translate) StartTranslate();
        if (t == HoverTab.Net && _menuOpen) StartNet(); else StopNet();
    }

    SolidColorBrush CurrentAccent() { try { return Brush(_settings.Accent); } catch { return Brush("#3DD6C4"); } }

    // the accent to actually paint with: the album-art color when auto-accent is on and we
    // have one, otherwise the chosen accent. the media card themes to the art with this.
    SolidColorBrush EffectiveAccent() => (_settings.AutoAccent && _artBrush != null) ? _artBrush : CurrentAccent();

    // ---------------------------------------------------------------- timer tab
    void AdjustTimer(int deltaMinutes)
    {
        if (_timerRunning) return;                 // only change the length while it's stopped
        int m = (int)Math.Round(_timerLen.TotalMinutes) + deltaMinutes;
        _timerLen = TimeSpan.FromMinutes(Math.Clamp(m, 1, 99));
        _timerLeft = _timerLen;
        TimerText.Foreground = Brushes.White;
        UpdateTimerText();
    }

    void ToggleTimer()
    {
        if (_timerRunning) { _timerRunning = false; _timer.Stop(); }
        else
        {
            if (_timerLeft <= TimeSpan.Zero) _timerLeft = _timerLen;
            _timerRunning = true; _timer.Start();
        }
        UpdateTimerButton();
    }

    void ResetTimer()
    {
        _timerRunning = false; _timer.Stop();
        _timerLeft = _timerLen;
        TimerText.Foreground = Brushes.White;
        UpdateTimerText(); UpdateTimerButton();
    }

    void TimerTick()
    {
        _timerLeft -= TimeSpan.FromSeconds(1);
        if (_timerLeft <= TimeSpan.Zero)
        {
            _timerLeft = TimeSpan.Zero;
            _timerRunning = false; _timer.Stop();
            TimerText.Foreground = Brush("#FF6B6B");
            UpdateTimerButton();
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            AttentionPulse();
            Burst();   // confetti when a timer finishes (if it's turned on)
        }
        UpdateTimerText();
    }

    void UpdateTimerText() => TimerText.Text = $"{(int)_timerLeft.TotalMinutes:00}:{_timerLeft.Seconds:00}";
    void UpdateTimerButton() => TStart.Content = _timerRunning ? "\uE769" : "\uE768";  // pause while running, play while stopped

    // a quick width wobble to get your attention when the timer finishes
    void AttentionPulse()
    {
        if (_hovering) return;
        double w = _expanded ? _expandedW : _compact;
        var a = new DoubleAnimation(w, w + 50, TimeSpan.FromMilliseconds(180))
        { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = new SineEase() };
        Shape.BeginAnimation(WidthProperty, a);
    }

    // ---------------------------------------------------------------- stats tab
    void StartStats() { GetSystemTimes(out _pIdle, out _pKernel, out _pUser); BuildGpu(); UpdateStats(); _stats.Start(); }
    void StopStats() { _stats.Stop(); DisposeGpu(); }

    void UpdateStats()
    {
        float gpu = GpuPercent();
        string g = gpu < 0 ? "--" : $"{gpu:0}%";
        StatsText.Text = $"CPU {CpuPercent():0}%   GPU {g}   RAM {RamPercent()}%";
    }

    // gpu usage comes from the "GPU Engine" performance counters. we sum the 3D
    // engine across every app. the counters need to live between reads, so build
    // them when the stats tab opens and throw them away when it closes.
    void BuildGpu()
    {
        DisposeGpu();
        try
        {
            var cat = new PerformanceCounterCategory("GPU Engine");
            foreach (var inst in cat.GetInstanceNames())
            {
                if (!inst.Contains("engtype_3D")) continue;
                foreach (var c in cat.GetCounters(inst))
                {
                    if (c.CounterName == "Utilization Percentage") { c.NextValue(); _gpuCounters.Add(c); }
                    else c.Dispose();
                }
            }
        }
        catch { }   // no gpu counters on this machine: stat just shows --
        _gpuBuilt = true;
    }

    void DisposeGpu()
    {
        foreach (var c in _gpuCounters) { try { c.Dispose(); } catch { } }
        _gpuCounters.Clear();
        _gpuBuilt = false;
    }

    float GpuPercent()
    {
        if (!_gpuBuilt || _gpuCounters.Count == 0) return -1;
        float total = 0;
        foreach (var c in _gpuCounters) { try { total += c.NextValue(); } catch { } }
        return (float)Clamp(total, 0, 100);
    }

    double CpuPercent()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
        long di = idle - _pIdle, dk = kernel - _pKernel, du = user - _pUser;
        _pIdle = idle; _pKernel = kernel; _pUser = user;
        long total = dk + du;   // kernel time already includes idle time
        return total <= 0 ? 0 : Clamp((total - di) * 100.0 / total, 0, 100);
    }

    int RamPercent()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (int)m.dwMemoryLoad : 0;
    }

    // ---------------------------------------------------------------- hey siri voice commands
    SiriBorderWindow Border() => _border ??= new SiriBorderWindow();

    // heard "hey siri ...": drop the orb card out of the notch (and light the border if on)
    void OnWake()
    {
        ShowSiriListening();
        if (_settings.SiriBorder && _settings.HeySiri)
        {
            Border().SetSize(_settings.SiriBorderSize);
            Border().ShowBorder(_settings.RgbSiriBorder);
            Border().Pulse();
        }
    }

    // speak a short reply if that's turned on
    void Say(string text)
    {
        if (!_settings.SiriVoice || string.IsNullOrWhiteSpace(text)) return;
        try { _tts.SpeakAsyncCancelAll(); _tts.SpeakAsync(text); } catch { }
    }

    void ShowSiriListening()
    {
        _siriHide.Stop();
        OrbCtl.Rgb = _settings.RgbSiri;
        OrbCtl.Start();
        SiriStatus.Text = "Listening…";
        if (_siriOpen) return;
        _siriOpen = true;
        SiriCard.Visibility = Visibility.Visible;
        SiriAnimate(1);
    }

    void ShowSiriResult(string text)
    {
        _siriHide.Stop();
        SiriStatus.Text = text;
        if (!_siriOpen) ShowSiriListening();
        SiriStatus.Text = text;
        HideSiriAfter(1.7);
    }

    void HideSiriAfter(double seconds)
    {
        if (!_siriOpen) return;
        _siriHide.Stop();
        _siriHide.Interval = TimeSpan.FromSeconds(seconds);
        _siriHide.Start();
    }

    void HideSiri()
    {
        _siriHide.Stop();
        _siriOpen = false;
        OrbCtl.Stop();
        SiriAnimate(0);
    }

    void SiriAnimate(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (to == 0) a.Completed += (s, e) => { if (SiriScale.ScaleY < 0.02) SiriCard.Visibility = Visibility.Collapsed; };
        SiriScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void RunVoiceCommand(string key)
    {
        string label = Voice.Commands.FirstOrDefault(c => c.Key == key).Label ?? key;
        try
        {
            switch (key)
            {
                case "next": _np.Next(); break;
                case "prev": _np.Prev(); break;
                case "playpause": _np.TogglePlayPause(); break;
                case "shuffle": _np.ToggleShuffle(); break;
                case "nowplaying":
                    label = _np.Playing && !string.IsNullOrWhiteSpace(_np.Title)
                        ? (string.IsNullOrWhiteSpace(_np.Artist) ? _np.Title : $"{_np.Title}   ·   {_np.Artist}")
                        : "Nothing playing";
                    break;
                case "volup": Tap(VK_VOLUME_UP); Tap(VK_VOLUME_UP); break;
                case "voldown": Tap(VK_VOLUME_DOWN); Tap(VK_VOLUME_DOWN); break;
                case "mute": Tap(VK_VOLUME_MUTE); break;
                case "screenshot": Combo(VK_LWIN, VK_SNAPSHOT); break;   // win+prtscn saves a file (and the airdrop card catches it)
                case "minimize": Combo(VK_LWIN, (byte)'D'); break;
                case "lock": LockWorkStation(); break;
                case "time": label = DateTime.Now.ToString("h:mm tt"); break;
                default:
                    if (key.StartsWith("close:")) KillApp(key.Substring(6));
                    else if (key.StartsWith("open:")) OpenApp(key.Substring(5));
                    break;
            }
        }
        catch { }
        OrbCtl.Flash();
        string spoken = key == "nowplaying"
            ? (_np.Playing && !string.IsNullOrWhiteSpace(_np.Title)
                ? (string.IsNullOrWhiteSpace(_np.Artist) ? _np.Title : $"This is {_np.Title} by {_np.Artist}")
                : "Nothing's playing")
            : SpokenFor(key, label);
        Say(spoken);
        ShowSiriResult(label);
        _border?.HideSoon(1.7);
    }

    // what siri says out loud for a command
    static string SpokenFor(string key, string label) => key switch
    {
        "next" => "Skipping",
        "prev" => "Going back",
        "playpause" => "Okay",
        "shuffle" => "Shuffle",
        "volup" => "Volume up",
        "voldown" => "Volume down",
        "mute" => "Muted",
        "screenshot" => "Screenshot taken",
        "minimize" => "Showing the desktop",
        "lock" => "Locking up",
        "time" => "It's " + label,
        _ when key.StartsWith("close:") => "Closing " + key.Substring(6),
        _ when key.StartsWith("open:") => "Opening " + key.Substring(5),
        _ => label,
    };

    // the open-ended ones: "hey siri search up ..." / "hey siri type ..."
    void RunDynamic(string kind, string text)
    {
        try
        {
            if (kind == "search")
                Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(text)) { UseShellExecute = true });
            else if (kind == "type")
                TypeText(text);
        }
        catch { }
        OrbCtl.Flash();
        Say(kind == "search" ? "Searching for " + text : "Typing");
        ShowSiriResult(kind == "search" ? "Searching: " + Shorten(text) : "Typed");
        _border?.HideSoon(1.7);
    }

    // send the text to whatever window has focus. the notch never takes focus, so it stays there.
    static void TypeText(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in text)
            sb.Append("+^%~(){}[]".IndexOf(c) >= 0 ? "{" + c + "}" : c.ToString());
        try { System.Windows.Forms.SendKeys.SendWait(sb.ToString()); } catch { }
    }

    static void KillApp(string name)
    {
        try { foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(); } catch { } finally { p.Dispose(); } } }
        catch { }
    }

    // best-effort launch by a known uri or path, since app locations vary
    static void OpenApp(string name)
    {
        string? target = name switch
        {
            "discord" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord", "Update.exe"),
            "spotify" => "spotify:",
            "steam" => "steam://open/main",
            _ => name,
        };
        try
        {
            if (name == "discord" && File.Exists(target!))
                Process.Start(new ProcessStartInfo(target!, "--processStart Discord.exe") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(target!) { UseShellExecute = true });
        }
        catch { }
    }

    static void Tap(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
    static void Combo(byte mod, byte vk)
    {
        keybd_event(mod, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); keybd_event(mod, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    const byte VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF, VK_SNAPSHOT = 0x2C, VK_LWIN = 0x5B;
    const uint KEYEVENTF_KEYUP = 0x2;
    [DllImport("user32")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32")] static extern bool LockWorkStation();

    // ---------------------------------------------------------------- calculator tab
    static readonly string[] CalcKeys =
    { "C", "⌫", "(", ")", "7", "8", "9", "/", "4", "5", "6", "×", "1", "2", "3", "-", "0", ".", "=", "+" };

    void BuildCalc()
    {
        _calcDisplay = new TextBlock
        {
            Text = "0", Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 20,
            TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 2, 8, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        CalcPanel.Children.Add(_calcDisplay);

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Width = 4 * 46 };
        foreach (var key in CalcKeys)
        {
            var b = new Button { Style = (Style)FindResource("Key"), Content = key };
            if (key is "=" ) b.Foreground = CurrentAccent();
            var k = key;
            b.Click += (s, e) => CalcKey(k);
            grid.Children.Add(b);
        }
        CalcPanel.Children.Add(grid);
    }

    void CalcKey(string k)
    {
        switch (k)
        {
            case "C": _calcExpr = ""; _calcResult = false; break;
            case "⌫":   // backspace
                if (_calcResult) { _calcExpr = ""; _calcResult = false; }
                else if (_calcExpr.Length > 0) _calcExpr = _calcExpr[..^1];
                break;
            case "=": CalcEval(); return;
            default:
                if (_calcResult && (char.IsDigit(k[0]) || k == "(")) { _calcExpr = ""; _calcResult = false; }
                else _calcResult = false;
                _calcExpr += k == "×" ? "*" : k;
                break;
        }
        UpdateCalcDisplay();
    }

    void CalcEval()
    {
        if (_calcExpr.Length == 0) return;
        try
        {
            var val = new DataTable().Compute(_calcExpr, null);
            _calcExpr = Convert.ToDouble(val).ToString("0.######");
            _calcResult = true;
        }
        catch { _calcExpr = "Error"; _calcResult = true; }
        UpdateCalcDisplay();
    }

    void UpdateCalcDisplay() =>
        _calcDisplay.Text = _calcExpr.Length == 0 ? "0" : _calcExpr.Replace("*", "×");

    // ---------------------------------------------------------------- translator tab
    static readonly (string code, string label)[] TransLangList =
    { ("en", "EN"), ("es", "ES"), ("fr", "FR"), ("de", "DE"), ("ja", "JA"), ("ko", "KO"), ("zh-CN", "ZH") };

    void BuildTransLangs()
    {
        foreach (var (code, label) in TransLangList)
        {
            var b = new Button { Style = (Style)FindResource("Key"), Content = label, Width = 36, FontSize = 11 };
            var c = code;
            b.Click += (s, e) => { _transTarget = c; StartTranslate(); };
            TransLangs.Children.Add(b);
        }
    }

    // grab the current clipboard text (or last copy) and translate it
    void StartTranslate()
    {
        foreach (var child in TransLangs.Children)
            if (child is Button b)
                b.Foreground = (string)b.Content == LabelFor(_transTarget) ? CurrentAccent() : Brushes.White;

        string text = "";
        try { if (System.Windows.Clipboard.ContainsText()) text = System.Windows.Clipboard.GetText(); } catch { }
        if (string.IsNullOrWhiteSpace(text)) text = _clips.FirstOrDefault() ?? "";
        text = text.Trim();
        if (text.Length > 500) text = text[..500];
        _transSrc = text;

        if (text.Length == 0) { TransSrc.Text = "copy some text, then open this"; TransResult.Text = ""; return; }
        TransSrc.Text = text;
        TransResult.Text = "…";
        _ = DoTranslate(text, _transTarget);
    }

    static string LabelFor(string code) => TransLangList.FirstOrDefault(l => l.code == code).label ?? "EN";

    async Task DoTranslate(string text, string target)
    {
        string result;
        try
        {
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={target}&dt=t&q={Uri.EscapeDataString(text)}";
            using var doc = System.Text.Json.JsonDocument.Parse(await _http.GetStringAsync(url));
            var sb = new System.Text.StringBuilder();
            foreach (var chunk in doc.RootElement[0].EnumerateArray())
                sb.Append(chunk[0].GetString());
            result = sb.ToString();
        }
        catch { result = "couldn't translate (offline?)"; }
        // ignore if the user moved on to another source/target while we were waiting
        if (_transSrc == text && _transTarget == target) TransResult.Text = result;
    }

    // ---------------------------------------------------------------- network speed tab
    void StartNet()
    {
        if (_netBars.Count == 0)
            for (int i = 0; i < 20; i++)
            {
                var r = new Rectangle
                {
                    Width = 4, Height = 2, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Bottom, Fill = CurrentAccent(),
                };
                _netBars.Add(r); NetBars.Children.Add(r);
            }
        ReadNet(out _netRx, out _netTx);
        _netAt = Environment.TickCount64;
        _net.Start();
    }

    void StopNet() => _net.Stop();

    void UpdateNet()
    {
        ReadNet(out long rx, out long tx);
        long now = Environment.TickCount64;
        double secs = Math.Max(0.001, (now - _netAt) / 1000.0);
        double down = (rx - _netRx) / secs, up = (tx - _netTx) / secs;
        _netRx = rx; _netTx = tx; _netAt = now;

        NetText.Text = $"↓ {NetRate(down)}    ↑ {NetRate(up)}";

        _netHist.Enqueue(down);
        while (_netHist.Count > _netBars.Count) _netHist.Dequeue();
        double max = Math.Max(1, _netHist.Max());
        var vals = _netHist.ToArray();
        for (int i = 0; i < _netBars.Count; i++)
        {
            int idx = i - (_netBars.Count - vals.Length);
            double v = idx >= 0 ? vals[idx] / max : 0;
            _netBars[i].Fill = CurrentAccent();
            _netBars[i].Height = 2 + v * 20;
        }
    }

    static void ReadNet(out long rx, out long tx)
    {
        rx = 0; tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived; tx += s.BytesSent;
            }
        }
        catch { }
    }

    static string NetRate(double bytesPerSec)
    {
        double b = Math.Max(0, bytesPerSec);
        if (b < 1024) return $"{b:0} B/s";
        double kb = b / 1024.0;
        if (kb < 1024) return $"{kb:0} KB/s";
        return $"{kb / 1024.0:0.0} MB/s";
    }

    // ---------------------------------------------------------------- weather-reactive particles
    readonly Random _rng = new();

    sealed class WxDrop { public FrameworkElement El = null!; public double X, Y, V, Drift, Phase; }

    void ApplyWeather()
    {
        bool on = _settings.WeatherFx && _sky is Sky.Rain or Sky.Snow or Sky.Storm;
        WeatherLayer.Children.Clear();
        _wxDrops.Clear();
        if (!on) { _wx.Stop(); return; }

        bool snow = _sky == Sky.Snow;
        double w = Math.Max(_compact, Shape.Width);
        int n = Math.Clamp((int)(w / (snow ? 26 : 18)), 6, 26);
        for (int i = 0; i < n; i++) _wxDrops.Add(NewDrop(snow, true));
        _wx.Start();
    }

    WxDrop NewDrop(bool snow, bool anywhere)
    {
        double w = Math.Max(_compact, WeatherLayer.ActualWidth > 1 ? WeatherLayer.ActualWidth : Shape.Width);
        double h = Math.Max(20, WeatherLayer.ActualHeight > 1 ? WeatherLayer.ActualHeight : Shape.Height);
        FrameworkElement el = snow
            ? new Ellipse { Width = 3, Height = 3, Fill = Brush("#CCFFFFFF") }
            : new Rectangle { Width = 1.6, Height = _sky == Sky.Storm ? 10 : 7, Fill = Brush("#99CFE4FF") };
        WeatherLayer.Children.Add(el);
        var d = new WxDrop
        {
            El = el,
            X = _rng.NextDouble() * w,
            Y = anywhere ? _rng.NextDouble() * h : -6,
            V = snow ? 0.5 + _rng.NextDouble() : (_sky == Sky.Storm ? 4.5 : 3) + _rng.NextDouble() * 2,
            Drift = snow ? (_rng.NextDouble() - 0.5) * 0.6 : 0.4,
            Phase = _rng.NextDouble() * 6.28,
        };
        Canvas.SetLeft(el, d.X); Canvas.SetTop(el, d.Y);
        return d;
    }

    void WeatherTick()
    {
        double h = WeatherLayer.ActualHeight > 1 ? WeatherLayer.ActualHeight : Shape.Height;
        double w = WeatherLayer.ActualWidth > 1 ? WeatherLayer.ActualWidth : Shape.Width;
        bool snow = _sky == Sky.Snow;
        foreach (var d in _wxDrops)
        {
            d.Y += d.V;
            d.Phase += 0.08;
            d.X += snow ? Math.Sin(d.Phase) * 0.6 : d.Drift;
            if (d.Y > h + 6 || d.X < -6 || d.X > w + 6)
            {
                d.Y = -6; d.X = _rng.NextDouble() * w;
            }
            Canvas.SetLeft(d.El, d.X); Canvas.SetTop(d.El, d.Y);
        }
    }

    // ---------------------------------------------------------------- confetti bursts
    static readonly string[] ConfettiColors = { "#FF5E5B", "#FFD93D", "#6BCB77", "#4D96FF", "#B983FF", "#FF9F45" };

    sealed class Confetto { public Rectangle El = null!; public RotateTransform Rot = null!; public double X, Y, Vx, Vy, Spin; }

    void Burst()
    {
        if (!_settings.Confetti) return;
        double cx = ActualWidth / 2, cy = 24;
        for (int i = 0; i < 42; i++)
        {
            var rot = new RotateTransform(_rng.Next(360));
            var r = new Rectangle
            {
                Width = 6, Height = 9, Fill = Brush(ConfettiColors[_rng.Next(ConfettiColors.Length)]),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rot,
            };
            ConfettiLayer.Children.Add(r);
            double ang = -Math.PI / 2 + (_rng.NextDouble() - 0.5) * 2.4;
            double sp = 6 + _rng.NextDouble() * 8;
            var c = new Confetto
            {
                El = r, Rot = rot, X = cx + (_rng.NextDouble() - 0.5) * 40, Y = cy,
                Vx = Math.Cos(ang) * sp, Vy = Math.Sin(ang) * sp, Spin = (_rng.NextDouble() - 0.5) * 24,
            };
            Canvas.SetLeft(r, c.X); Canvas.SetTop(r, c.Y);
            _confetti_ps.Add(c);
        }
        if (!_confetti.IsEnabled) _confetti.Start();
    }

    void ConfettiTick()
    {
        double floor = ActualHeight + 20;
        for (int i = _confetti_ps.Count - 1; i >= 0; i--)
        {
            var c = _confetti_ps[i];
            c.Vy += 0.35;          // gravity
            c.Vx *= 0.99;
            c.X += c.Vx; c.Y += c.Vy;
            c.Rot.Angle += c.Spin;
            Canvas.SetLeft(c.El, c.X); Canvas.SetTop(c.El, c.Y);
            if (c.Y > floor)
            {
                ConfettiLayer.Children.Remove(c.El);
                _confetti_ps.RemoveAt(i);
            }
        }
        if (_confetti_ps.Count == 0) _confetti.Stop();
    }

    // ---------------------------------------------------------------- placement + topmost
    void Position()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        // let clicks pass through, don't steal focus, hide from alt-tab
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        _clickThrough = true;

        // listen for clipboard changes so we can flash "copied", and for device /
        // power changes so we can pop the 3D device card
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        AddClipboardFormatListener(_hwnd);
        try { _lastAc = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online; } catch { }

        // ask windows to tell us about ALL device interfaces (dongles, audio, hid, usb),
        // not just storage drives, so wireless receivers etc. actually fire the card
        try
        {
            var dbi = new DEV_BROADCAST_DEVICEINTERFACE { dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE };
            dbi.dbcc_size = Marshal.SizeOf(dbi);
            var p = Marshal.AllocHGlobal(dbi.dbcc_size);
            Marshal.StructureToPtr(dbi, p, false);
            _devNotify = RegisterDeviceNotification(_hwnd, p, DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);
            Marshal.FreeHGlobal(p);
        }
        catch { }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) OnClipboard();
        else if (msg == WM_DEVICECHANGE) OnDeviceChange(w, l);
        else if (msg == WM_POWERBROADCAST && w.ToInt32() == PBT_APMPOWERSTATUSCHANGE) OnPowerChange();
        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- device connected card
    void OnDeviceChange(IntPtr w, IntPtr l)
    {
        if (!_settings.ShowDeviceCard || l == IntPtr.Zero) return;
        int ev = w.ToInt32();
        if (ev != DBT_DEVICEARRIVAL && ev != DBT_DEVICEREMOVECOMPLETE) return;
        bool arrived = ev == DBT_DEVICEARRIVAL;

        int devType = Marshal.ReadInt32(l, 4);   // DEV_BROADCAST_HDR.dbch_devicetype
        if (devType == DBT_DEVTYP_VOLUME)
        {
            string name = _settings.ShowDeviceName ? VolumeName(l) : "USB drive";
            QueueDeviceBurst(new DevSpec(DeviceKind.Usb, name, 2, CGray, 1.1, ""), arrived);
        }
        else if (devType == DBT_DEVTYP_DEVICEINTERFACE)
        {
            var path = ReadDevicePath(l);
            string vp = VidPid(path);
            if (vp.Length > 0 && !_seenDevices.Contains(vp)) _seenDevices.Add(vp);   // for the setup panel
            QueueDeviceBurst(ClassifyInterface(ReadGuid(l, 12), path), arrived);
        }
    }

    // devices plugged in this session + any the user already has a rule for
    readonly List<string> _seenDevices = new();
    internal List<string> KnownDevices() =>
        _settings.DeviceRules.Select(r => r.Vp).Concat(_seenDevices).Where(v => v.Length > 0).Distinct().ToList();
    internal DeviceRule? RuleFor(string vp) => _settings.DeviceRules.FirstOrDefault(r => r.Vp == vp);

    internal void SetDeviceRule(string vp, string name, string type)
    {
        _settings.DeviceRules.RemoveAll(r => r.Vp == vp);
        _settings.DeviceRules.Add(new DeviceRule { Vp = vp, Name = name, Type = type });
        _settings.Save();
    }

    internal void RemoveDeviceRule(string vp)
    {
        _settings.DeviceRules.RemoveAll(r => r.Vp == vp);
        _settings.Save();
    }

    // the model presets users can pick from in the "set up my devices" panel
    internal static readonly string[] DeviceTypes =
        { "Keyboard & mouse", "Headset / audio", "Controller", "USB drive", "Charger", "Phone", "Other" };

    static DevSpec SpecForType(string type, string name, string info, int prio)
    {
        string title = string.IsNullOrWhiteSpace(name) ? type : name;
        return type switch
        {
            "Keyboard & mouse" => new DevSpec(DeviceKind.Dongle, title, prio, CBlack, 0.7, info),
            "Headset / audio" => new DevSpec(DeviceKind.Dongle, title, prio, CWhite, 1.15, info),
            "Controller" => new DevSpec(DeviceKind.Generic, title, prio, CGray, 1.0, info),
            "USB drive" => new DevSpec(DeviceKind.Usb, title, prio, CGray, 1.1, info),
            "Charger" => new DevSpec(DeviceKind.Charger, title, prio, CWhite, 0, info),
            "Phone" => new DevSpec(DeviceKind.Phone, title, prio, CGray, 0, info),
            _ => new DevSpec(DeviceKind.Generic, title, prio, CGray, 1.0, info),
        };
    }

    // pick the model + look. a device announces several interfaces at once, so the
    // highest-priority one wins. the real key is the usb vendor id, on every event.
    DevSpec ClassifyInterface(Guid g, string? path)
    {
        string info = VidPid(path);

        // the user's own mappings win over everything
        var rule = _settings.DeviceRules.FirstOrDefault(r => r.Vp == info);
        if (rule != null) return SpecForType(rule.Type, rule.Name, info, 6);

        // built-in defaults (a little easter egg for anyone with the same gear)
        if (info == "0B05:1ACE") return new DevSpec(DeviceKind.Dongle, "Keyboard & mouse", 5, CBlack, 0.7, info);   // ROG Omni receiver
        if (info == "10F5:2242") return new DevSpec(DeviceKind.Dongle, "Audio device", 5, CWhite, 1.15, info);  // turtle beach receiver

        if (g == GAudio || g == GRender || g == GCapture)
            return new DevSpec(DeviceKind.Dongle, "Audio device", 3, CWhite, 1.15, info);
        if (g == GUsb) return new DevSpec(DeviceKind.Usb, "USB device", 2, CGray, 1.1, info);
        if (g == GHid) return new DevSpec(DeviceKind.Generic, "Input device", 1, CGray, 1.0, info);
        return new DevSpec(DeviceKind.Generic, "Device", 0, CGray, 1.0, info);
    }

    static string VidPid(string? path)
    {
        if (path == null) return "";
        string v = Grab(path, "VID_"), p = Grab(path, "PID_");
        return v == "" ? "" : (p == "" ? $"VID {v}" : $"{v}:{p}");
    }

    static string Grab(string s, string key)
    {
        int i = s.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        return i >= 0 && i + key.Length + 4 <= s.Length ? s.Substring(i + key.Length, 4).ToUpperInvariant() : "";
    }

    // gather the burst of interface events for one plug, then show a single card
    void QueueDeviceBurst(DevSpec spec, bool arrived)
    {
        if (!_burstPending || spec.Prio >= _burstSpec.Prio) _burstSpec = spec;
        _burstArrived = arrived;
        _burstPending = true;
        _devBurst.Stop();
        _devBurst.Interval = TimeSpan.FromMilliseconds(500);
        _devBurst.Start();
    }

    void DeviceBurstTick()
    {
        _devBurst.Stop();
        if (!_burstPending) return;
        _burstPending = false;
        // skip the random "Device Connected" cube for anything we couldn't identify
        if (_settings.HideUnknownDevices && _burstSpec.Kind == DeviceKind.Generic) return;
        ShowDeviceCard(_burstSpec.Kind, _burstSpec.Title, _burstArrived ? "Connected" : "Disconnected", _burstSpec.Body, _burstSpec.Len);
    }

    static string? ReadDevicePath(IntPtr l)
    {
        try { return Marshal.PtrToStringUni(new IntPtr(l.ToInt64() + 28)); }   // dbcc_name
        catch { return null; }
    }

    static Guid ReadGuid(IntPtr l, int off)
    {
        var b = new byte[16];
        Marshal.Copy(new IntPtr(l.ToInt64() + off), b, 0, 16);
        return new Guid(b);
    }

    static readonly Guid GAudio = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");
    static readonly Guid GRender = new("65E8773E-8F56-11D0-A3B9-00A0C9223196");
    static readonly Guid GCapture = new("65E8773D-8F56-11D0-A3B9-00A0C9223196");
    static readonly Guid GUsb = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    static readonly Guid GHid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    // virtual desktop spawns VirtualDesktop.Server.exe only while a headset is actually
    // connected/streaming (not just when the streamer is open), so use that as the signal
    void UpdateVr()
    {
        if (!_settings.ShowDeviceCard) return;
        if ((++_vrTick & 3) != 0) return;   // check about every 1.6s, not every tick
        bool conn = false;
        try
        {
            var procs = Process.GetProcessesByName("VirtualDesktop.Server");
            conn = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
        }
        catch { }

        bool first = !_vrInit; _vrInit = true;
        if (conn != _vrConnected)
        {
            _vrConnected = conn;
            if (!first)   // don't fire on launch if it was already connected
                ShowDeviceCard(DeviceKind.Quest3, "Quest 3", conn ? "Connected" : "Disconnected", CGray, 0);
        }
    }

    void OnPowerChange()
    {
        if (!_settings.ShowDeviceCard) return;
        var ps = System.Windows.Forms.SystemInformation.PowerStatus;
        bool ac = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        if (ac == _lastAc) return;   // only when it actually flips
        _lastAc = ac;
        int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
        string sub = pct >= 0 && pct <= 100 ? $"{pct}%" : (ac ? "Plugged in" : "Unplugged");
        ShowDeviceCard(DeviceKind.Charger, ac ? "Charging" : "On battery", sub, CWhite, 0);
    }

    // read the drive letters that just changed and turn them into a label
    static string VolumeName(IntPtr l)
    {
        try
        {
            int mask = Marshal.ReadInt32(l, 12);   // DEV_BROADCAST_VOLUME.dbcv_unitmask
            for (int i = 0; i < 26; i++)
                if ((mask & (1 << i)) != 0) return $"{(char)('A' + i)}: drive";
        }
        catch { }
        return "USB drive";
    }

    // debug: each press shows the next kind of popup so they can be checked without
    // plugging anything in or waiting for a notification
    int _dbg;
    internal void DebugNextPopup()
    {
        switch (_dbg++ % 11)
        {
            case 0: PopMessage("\uE7E7", "Discord", "new message from a friend", 4.0); break;   // notification
            case 1: PopMessage("\uE8C8", "Copied", "some text you just copied", 1.8); break;     // copied flash
            case 2: PopFile(@"C:\Windows\explorer.exe", 1); break;                               // file preview
            case 3: ShowDeviceCard(DeviceKind.Charger, "Charging", "82%", CWhite, 0); break;
            case 4: ShowDeviceCard(DeviceKind.Dongle, "Keyboard & mouse", "Connected", CBlack, 0.7); break;
            case 5: ShowDeviceCard(DeviceKind.Dongle, "Audio device", "Connected", CWhite, 1.15); break;
            case 6: ShowDeviceCard(DeviceKind.Usb, "USB device", "Connected", CGray, 1.1); break;
            case 7: ShowDeviceCard(DeviceKind.Quest3, "Quest 3", "Connected", CGray, 0); break;
            case 8: DebugDownload(); break;
            case 9: ShowDrop(@"C:\Windows\explorer.exe"); break;   // airdrop card preview
            case 10:   // recording pill preview: force it on for a few seconds
                _recForce = true;
                var rt = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
                rt.Tick += (s, e) => { rt.Stop(); _recForce = false; };
                rt.Start();
                UpdateRecording();
                break;
        }
    }

    // fake a download start -> grows -> finishes, so the ring can be seen without a real one
    void DebugDownload()
    {
        long[] steps = { 8_400_000, 31_000_000, 62_500_000, 90_200_000 };
        int i = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        t.Tick += (s, e) =>
        {
            if (i < steps.Length) ShowDownload("BONELAB_mod_pack.zip", steps[i++]);
            else { t.Stop(); DownloadDone("BONELAB_mod_pack.zip"); EndDownload(); }
        };
        t.Start();
        ShowDownload("BONELAB_mod_pack.zip", steps[i++]);
    }

    void ShowDeviceCard(DeviceKind kind, string title, string sub, Color body, double len)
    {
        var acc = CurrentAccent().Color;
        DevModel.Content = DeviceModels.Build(kind, acc, body, len);
        DevTitle.Text = title;
        DevSub.Text = sub;

        DeviceCard.Visibility = Visibility.Visible;
        DevAnimate(1);
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever };
        DevSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, spin);

        _devTimer.Stop();
        _devTimer.Interval = TimeSpan.FromSeconds(4.5);
        _devTimer.Start();
    }

    void HideDeviceCard()
    {
        _devTimer.Stop();
        DevSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        DevAnimate(0);
    }

    void DevAnimate(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (to == 0) a.Completed += (s, e) => { if (DevScale.ScaleY < 0.02) DeviceCard.Visibility = Visibility.Collapsed; };
        DevScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---------------------------------------------------------------- recording pill
    // checked a few times a second: is the screen being captured (recorded / shared)?
    void UpdateRecording()
    {
        if (!_settings.ShowRecording) { if (_recActive) StopRec(); return; }
        bool rec = _recForce || Privacy.ScreenInUse;
        if (rec && !_recActive) StartRec();
        else if (!rec && _recActive) StopRec();
    }

    void StartRec()
    {
        _recActive = true;
        _recStart = DateTime.Now;
        RecDot.Fill = Brush("#FF3B30");
        TitleText.Text = "Recording";
        ArtistText.Text = "0:00";
        RefreshContent();
        if (!_noteActive && !_dlActive) Animate(RecWidth);   // download, if running, keeps the width
        // pulse the red dot so it reads as a live "you're being recorded" light
        var pulse = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        RecDot.BeginAnimation(OpacityProperty, pulse);
        _recClock.Start();
    }

    void StopRec()
    {
        _recActive = false;
        _recClock.Stop();
        RecDot.BeginAnimation(OpacityProperty, null);
        RecDot.Opacity = 1;
        RefreshContent();
        UpdateTrack();   // hand the pill back to music / idle
        if (!_noteActive && !_dlActive) Animate(_expanded ? _expandedW : _compact);
    }

    void UpdateRecClock()
    {
        if (!_recActive) return;
        ArtistText.Text = Fmt(DateTime.Now - _recStart);
    }

    // ---------------------------------------------------------------- airdrop file card
    // the folders we watch: the user's list if they set one, otherwise the defaults
    IEnumerable<string> AirdropFolders() =>
        _settings.AirdropFolders.Count > 0 ? _settings.AirdropFolders : _incoming.DefaultFolders();

    // a finished file landed in a watched folder: drop a card with it whooshing in
    void ShowDrop(string path)
    {
        if (!_settings.ShowAirdrop) return;
        _dropPath = path;
        HideDeviceCard();   // don't stack two drop cards on top of each other

        DropThumb.Source = PreviewImage(path) ?? FileIcon(path);
        DropName.Text = Path.GetFileName(path);
        DropSub.Text = FileMeta(path);
        _dropCardOpen = true;

        DropCard.Visibility = Visibility.Visible;
        DropCardAnimate(1);
        // the thumbnail flies in: pops from small to full with a springy landing
        var pop = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(520))
        { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut } };
        DropThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        DropThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

        _dropTimer.Stop();
        _dropTimer.Interval = TimeSpan.FromSeconds(6);
        _dropTimer.Start();
    }

    void HideDropCard()
    {
        _dropTimer.Stop();
        _dropCardOpen = false;
        DropCardAnimate(0);
    }

    void DropCardAnimate(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (to == 0) a.Completed += (s, e) => { if (DropCardScale.ScaleY < 0.02) DropCard.Visibility = Visibility.Collapsed; };
        DropCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    static string FileMeta(string path)
    {
        try { return FormatBytes(new FileInfo(path).Length); } catch { return ""; }
    }

    void OpenPath(string path)
    {
        try { if (File.Exists(path) || Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    void RevealInFolder(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { }
    }

    // ---------------------------------------------------------------- media live activity
    void BuildMediaBars()
    {
        MEq.Children.Clear();
        _mediaBars.Clear();
        for (int i = 0; i < 4; i++)
        {
            var r = new Rectangle
            {
                Width = 3, Height = 3, RadiusX = 1.5, RadiusY = 1.5,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 0),
                Fill = CurrentAccent(),
            };
            _mediaBars.Add(r);
            MEq.Children.Add(r);
        }
    }

    void ToggleMediaCard() { if (_mediaOpen) HideMediaCard(); else ShowMediaCard(); }

    const double MediaCardWidth = 372;

    void ShowMediaCard()
    {
        if (_menuOpen) CloseMenu();       // don't stack it with the tabbed menu
        HideDeviceCard();
        _mediaOpen = true;
        UpdateMediaCard();                // fill it in before it slides open
        RefreshContent();                 // blank the pill so it reads as one expanding shape
        // widen the pill to the card and square its bottom so the two merge into one notch
        double tr = _settings.Style == NotchStyle.Island ? 20 * _hs : 0;
        Shape.CornerRadius = new CornerRadius(tr, tr, 0, 0);
        ClipShape();
        Animate(MediaCardWidth);
        MediaCard.Visibility = Visibility.Visible;
        MedAnimate(1);
        _media.Start();
    }

    void HideMediaCard()
    {
        if (!_mediaOpen) return;
        _mediaOpen = false;
        _media.Stop();
        MedAnimate(0);
        // restore the pill shape + width
        Shape.CornerRadius = _settings.Style == NotchStyle.Island
            ? new CornerRadius(20 * _hs) : new CornerRadius(0, 0, 22, 22);
        ClipShape();
        RefreshContent();
        Animate(_expanded ? _expandedW : _compact);
    }

    void MedAnimate(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (to == 0) a.Completed += (s, e) => { if (MedScale.ScaleY < 0.02) MediaCard.Visibility = Visibility.Collapsed; };
        MedScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void UpdateMediaCard()
    {
        if (!_mediaOpen) return;
        _np.RefreshTimeline();

        MArt.Source = Art.Source;
        MTitle.Text = _np.Title;
        MArtist.Text = _np.Artist;

        var dur = _np.Duration;
        var pos = _np.Position;
        MTimeCur.Text = Fmt(pos);
        MTimeTot.Text = Fmt(dur);

        if (!_seeking)
        {
            double frac = dur.TotalSeconds > 0 ? Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1) : 0;
            double w = MScrub.ActualWidth;
            MFill.Width = w * frac;
            MThumb.Margin = new Thickness(Math.Max(0, w * frac - MThumb.Width / 2), 0, 0, 0);
        }

        var acc = EffectiveAccent();
        MFill.Background = acc;
        foreach (var bar in _mediaBars) bar.Fill = acc;   // theme the little equalizer to the art too
        MPlay.Content = _np.Playing ? "\uE769" : "\uE768";
        MShuffle.Foreground = _np.Shuffle ? acc : Brushes.White;
        MRepeat.Foreground = _np.Repeat > 0 ? acc : Brushes.White;
        MRepeat.Content = _np.Repeat == 2 ? "\uE8ED" : "\uE8EE";   // repeat-one vs repeat-all glyph
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    // click or drag anywhere on the scrubber to seek
    void ScrubDown(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        _seeking = true;
        MScrub.CaptureMouse();
        ScrubTo(e.GetPosition(MScrub).X);
    }

    void ScrubMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (_seeking) ScrubTo(e.GetPosition(MScrub).X);
    }

    void ScrubUp(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        double frac = Clamp(e.GetPosition(MScrub).X / Math.Max(1, MScrub.ActualWidth), 0, 1);
        _seeking = false;
        MScrub.ReleaseMouseCapture();
        _np.Seek(frac);
    }

    void ScrubTo(double x)
    {
        double w = Math.Max(1, MScrub.ActualWidth);
        double frac = Clamp(x / w, 0, 1);
        MFill.Width = w * frac;
        MThumb.Margin = new Thickness(Math.Max(0, w * frac - MThumb.Width / 2), 0, 0, 0);
        MTimeCur.Text = Fmt(TimeSpan.FromSeconds(_np.Duration.TotalSeconds * frac));
    }

    void Reassert()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // optionally get out of the way of fullscreen apps (games, videos)
    void UpdateFullscreen()
    {
        if (!_settings.HideOnFullscreen) { if (!IsVisible) Show(); return; }
        bool fs = IsForegroundFullscreen();
        if (fs && IsVisible) Hide();
        else if (!fs && !IsVisible) Show();
    }

    bool IsForegroundFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return false;

        // ignore the desktop and the taskbar/shell
        var sb = new System.Text.StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;

        if (!GetWindowRect(fg, out var r)) return false;
        var mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;

        // the window covers the whole monitor it's on
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    // ---------------------------------------------------------------- grab-to-stretch
    // normally clicks pass right through the window. when the mouse is over the notch
    // we briefly make it solid so you can grab it, then let it go back to pass-through.
    // clicks anywhere else still reach whatever is behind it.
    void HoverTick()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_dragging) return;                 // keep it grabbable while you're dragging
        GetCursorPos(out var p);
        bool over = OverInteractive(p.X, p.Y);
        if (_menuOpen && _tab == HoverTab.Shelf) over = true;   // stay droppable while the shelf is open
        SetClickThrough(!over);                 // solid while the cursor is on the notch or the open menu
        _hovering = over;
    }

    // right-click toggles the menu open/closed; it doesn't auto-hide
    void ToggleMenu()
    {
        if (_menuOpen) CloseMenu();
        else OpenMenu();
    }

    void OpenMenu()
    {
        HideMediaCard();          // the two drop panels are mutually exclusive
        _menuOpen = true;
        UpdatePlayGlyph();
        DropPanel.Visibility = Visibility.Visible;
        DropAnimate(1);                         // grow the panel downward
        if (_tab == HoverTab.Stats) StartStats();
        if (_tab == HoverTab.Net) StartNet();
        if (_tab == HoverTab.Translate) StartTranslate();
    }

    void CloseMenu()
    {
        _menuOpen = false;
        DropAnimate(0);                         // shrink it back up
        StopStats();
        StopNet();
    }

    void DropAnimate(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(160))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (to == 0) a.Completed += (s, e) => { if (!_menuOpen) DropPanel.Visibility = Visibility.Collapsed; };
        Drop.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // the notch area = the pill, plus the drop menu (and the little gap) while it's open
    bool OverInteractive(int px, int py)
    {
        try
        {
            var pa = Shape.PointToScreen(new Point(0, 0));
            var pb = Shape.PointToScreen(new Point(Shape.ActualWidth, Shape.ActualHeight));
            bool inPill = px >= Math.Min(pa.X, pb.X) && px <= Math.Max(pa.X, pb.X)
                       && py >= Math.Min(pa.Y, pb.Y) && py <= Math.Max(pa.Y, pb.Y);
            if (inPill) return true;

            if (_menuOpen && DropPanel.ActualHeight > 1 && InUnionWithPill(DropPanel, pa, pb, px, py)) return true;
            if (_mediaOpen && MediaCard.ActualHeight > 1 && InUnionWithPill(MediaCard, pa, pb, px, py)) return true;
            if (_dropCardOpen && DropCard.ActualHeight > 1 && InUnionWithPill(DropCard, pa, pb, px, py)) return true;
        }
        catch { }
        return false;
    }

    // is the cursor inside the box that covers both the pill and a dropped panel (plus the gap)?
    bool InUnionWithPill(FrameworkElement panel, Point pa, Point pb, int px, int py)
    {
        var da = panel.PointToScreen(new Point(0, 0));
        var db = panel.PointToScreen(new Point(panel.ActualWidth, panel.ActualHeight));
        double left = Math.Min(Math.Min(pa.X, pb.X), Math.Min(da.X, db.X));
        double right = Math.Max(Math.Max(pa.X, pb.X), Math.Max(da.X, db.X));
        double top = Math.Min(pa.Y, pb.Y);
        double bottom = Math.Max(da.Y, db.Y);
        return px >= left && px <= right && py >= top && py <= bottom;
    }

    void SetClickThrough(bool on)
    {
        if (_clickThrough == on || _hwnd == IntPtr.Zero) return;
        _clickThrough = on;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        ex = on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
    }

    void GrabDown(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _downTime = DateTime.Now;
        Shape.CaptureMouse();
        // stop any leftover bounce so the pull starts fresh
        Stretch.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Stretch.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Stretch.ScaleX = 1; Stretch.ScaleY = 1;
    }

    void GrabMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        // normally the top bar stays put while the music player is pulled down; the joke
        // setting lets you grab and wiggle it loose from the card underneath
        if (_mediaOpen && !_settings.WiggleWhenOpen) return;
        var p = e.GetPosition(this);
        double dx = p.X - _dragStart.X;
        double dy = p.Y - _dragStart.Y;
        // the harder you pull, the less it moves, like a rubber band. the drag strength
        // (1x-10x) scales how far it can go before it maxes out
        double m = _settings.DragStrength;
        Stretch.ScaleX = 1 + Rubber(dx, 0.55 * m, 220);
        Stretch.ScaleY = 1 + Rubber(Math.Max(0, dy), 1.10 * m, 150);   // only stretches down
    }

    void GrabUp(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Shape.ReleaseMouseCapture();
        var spring = new ElasticEase { Oscillations = 2, Springiness = 4, EasingMode = EasingMode.EaseOut };
        var t = TimeSpan.FromMilliseconds(650);
        Stretch.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, t) { EasingFunction = spring });
        Stretch.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, t) { EasingFunction = spring });

        // a quick tap (not a drag) while music is playing opens the media live activity
        var p = e.GetPosition(this);
        double moved = Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y);
        bool tap = moved < 6 && (DateTime.Now - _downTime).TotalMilliseconds < 400;
        if (tap && _np.Playing && !string.IsNullOrWhiteSpace(_np.Title))
            ToggleMediaCard();
    }

    static double Rubber(double dist, double limit, double k) =>
        limit * (1 - Math.Exp(-Math.Abs(dist) / k));

    // ---------------------------------------------------------------- file shelf
    void ShelfDragOver(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void ShelfDrop(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
            if (!_shelf.Contains(f)) _shelf.Add(f);
        RebuildShelf();
    }

    // drop straight onto the notch: add to the shelf and flash a quick preview
    void PillDrop(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var f in files) if (!_shelf.Contains(f)) _shelf.Add(f);
        RebuildShelf();
        if (files.Length > 0) PopFile(files[0], files.Length);
    }

    void PopFile(string path, int count)
    {
        NoteImage.Source = PreviewImage(path) ?? FileIcon(path);   // real image if we can, else the file's icon
        NoteImageHost.Visibility = Visibility.Visible;
        NoteIcon.Visibility = Visibility.Collapsed;
        NoteTitle.Text = Path.GetFileName(path);
        NoteBody.Text = count > 1 ? $"+{count} to shelf" : "Added to shelf";
        _noteActive = true;
        RefreshContent();
        Animate(BaseExpanded * _ws);
        _noteTimer.Stop();
        _noteTimer.Interval = TimeSpan.FromSeconds(1.8);
        _noteTimer.Start();
    }

    // load an image/gif as a small preview, or null if it isn't a picture
    static System.Windows.Media.ImageSource? PreviewImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".ico" or ".jfif")) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelHeight = 64;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    void RebuildShelf()
    {
        ShelfItems.Children.Clear();
        if (_shelf.Count == 0) { ShelfItems.Children.Add(Hint("drop files here")); return; }
        foreach (var path in _shelf.ToList())
        {
            var item = new StackPanel { Width = 60, Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand };
            item.Children.Add(new Image { Width = 28, Height = 28, Source = FileIcon(path), HorizontalAlignment = HorizontalAlignment.Center });
            item.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path), Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 8.5,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 58,
            });
            var p = path;
            item.MouseMove += (o, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    try { DragDrop.DoDragDrop(item, new DataObject(DataFormats.FileDrop, new[] { p }), DragDropEffects.Copy); } catch { }
            };
            item.MouseRightButtonUp += (o, e) => { _shelf.Remove(p); RebuildShelf(); e.Handled = true; };
            ShelfItems.Children.Add(item);
        }
    }

    // ---------------------------------------------------------------- clipboard history
    void AddClip(string full)
    {
        full = full.Trim();
        if (full.Length == 0) return;
        _clips.Remove(full);
        _clips.Insert(0, full);
        while (_clips.Count > 12) _clips.RemoveAt(_clips.Count - 1);
        if (_tab == HoverTab.Clipboard && _menuOpen) RebuildClips();
    }

    void RebuildClips()
    {
        ClipItems.Children.Clear();
        if (_clips.Count == 0) { ClipItems.Children.Add(Hint("nothing copied yet")); return; }
        foreach (var text in _clips.Take(8))
        {
            string disp = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (disp.Length > 16) disp = disp.Substring(0, 15) + "…";
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = disp, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 11 },
            };
            var full = text;
            chip.MouseLeftButtonUp += (o, e) => { try { _selfClip = true; System.Windows.Clipboard.SetText(full); } catch { _selfClip = false; } };
            ClipItems.Children.Add(chip);
        }
    }

    // ---------------------------------------------------------------- pinned apps
    void RebuildApps()
    {
        AppItems.Children.Clear();
        foreach (var path in _settings.PinnedApps.ToList())
        {
            var img = new Image
            {
                Width = 30, Height = 30, Source = FileIcon(path), Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 4, 0), ToolTip = Path.GetFileNameWithoutExtension(path),
            };
            var p = path;
            img.MouseLeftButtonUp += (o, e) => LaunchApp(p);
            img.MouseRightButtonUp += (o, e) => { _settings.PinnedApps.Remove(p); _settings.Save(); RebuildApps(); e.Handled = true; };
            AppItems.Children.Add(img);
        }
        var add = new Button { Style = (Style)FindResource("Ctl"), Content = "\uE710", Margin = new Thickness(4, 0, 0, 0), ToolTip = "Add an app" };
        add.Click += (o, e) => AddApp();
        AppItems.Children.Add(add);
    }

    void AddApp()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe;*.lnk;*.bat;*.url|All files|*.*" };
        if (dlg.ShowDialog() == true && !_settings.PinnedApps.Contains(dlg.FileName))
        {
            _settings.PinnedApps.Add(dlg.FileName);
            _settings.Save();
            RebuildApps();
        }
    }

    void LaunchApp(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    static TextBlock Hint(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
        FontFamily = new FontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
    };

    static System.Windows.Media.ImageSource? FileIcon(string path)
    {
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico == null) return null;
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                ico.Handle, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    internal NotchSettings Settings => _settings;
    internal void SetStyle(NotchStyle s) { _settings.Style = s; _settings.Save(); ApplyStyle(); }
    internal void SetAccent(string hex) { _settings.Accent = hex; _settings.Save(); ApplyStyle(); }
    internal void SetWidthScale(double s) { _settings.WidthScale = Clamp(s, 0.6, 2.0); _settings.Save(); ApplyStyle(); }
    internal void SetHeightScale(double s) { _settings.HeightScale = Clamp(s, 0.6, 2.5); _settings.Save(); ApplyStyle(); }
    internal void ResetSize() { _settings.WidthScale = 1.0; _settings.HeightScale = 1.0; _settings.Save(); ApplyStyle(); }
    internal void SetAutoAccent(bool v) { _settings.AutoAccent = v; _settings.Save(); ApplyBarColor(); }
    internal void SetShowDots(bool v) { _settings.ShowDots = v; _settings.Save(); _dotTick = -1; UpdateDots(); }
    internal void SetFrostedArt(bool v) { _settings.FrostedArt = v; _settings.Save(); RefreshContent(); }
    internal void SetIosLayout(bool v) { _settings.IosLayout = v; _settings.Save(); ApplyStyle(); }
    internal void SetSensitivity(double s) { _settings.Sensitivity = Clamp(s, 0.25, 7.5); _settings.Save(); _audio?.SetSensitivity(_settings.Sensitivity); }
    internal void SetBars(int n) { _settings.Bars = Math.Clamp(n, 3, 10); _settings.Save(); ApplyStyle(); }
    internal void SetVisual(VisualStyle v) { _settings.Visual = v; _settings.Save(); ApplyStyle(); }
    internal void SetDragStrength(int v) { _settings.DragStrength = Math.Clamp(v, 1, 10); _settings.Save(); }
    internal void SetShowNotes(bool v) { _settings.ShowNotes = v; _settings.Save(); }
    internal void SetShowCopied(bool v) { _settings.ShowCopied = v; _settings.Save(); }
    internal void SetHideOnFullscreen(bool v) { _settings.HideOnFullscreen = v; _settings.Save(); UpdateFullscreen(); }
    internal void SetShowDownloadRing(bool v)
    {
        _settings.ShowDownloadRing = v; _settings.Save();
        if (v) _downloads.Start();
        else { _downloads.Stop(); if (_dlActive) ClearDownload(); }
    }
    internal void SetDownloadRingCompact(bool v)
    {
        _settings.DownloadRingCompact = v; _settings.Save();
        if (_dlActive) { RefreshContent(); if (!_noteActive) Animate(DlActiveWidth); }   // re-lay-out a live one
    }
    internal void SetShowDeviceCard(bool v) { _settings.ShowDeviceCard = v; _settings.Save(); }
    internal void SetShowDeviceName(bool v) { _settings.ShowDeviceName = v; _settings.Save(); }
    internal void SetHideUnknownDevices(bool v) { _settings.HideUnknownDevices = v; _settings.Save(); }
    internal void SetShowAirdrop(bool v)
    {
        _settings.ShowAirdrop = v; _settings.Save();
        if (v) _incoming.Start(AirdropFolders());
        else { _incoming.Stop(); if (_dropCardOpen) HideDropCard(); }
    }
    internal void AddAirdropFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        // once the user picks their own folder, keep the defaults too so nothing silently stops working
        if (_settings.AirdropFolders.Count == 0) _settings.AirdropFolders.AddRange(_incoming.DefaultFolders());
        if (!_settings.AirdropFolders.Contains(dir)) _settings.AirdropFolders.Add(dir);
        _settings.Save();
        if (_settings.ShowAirdrop) _incoming.Start(AirdropFolders());
    }
    internal string DropFolderPath => _incoming.DropFolder;
    internal void SetShowRecording(bool v)
    {
        _settings.ShowRecording = v; _settings.Save();
        if (!v && _recActive) StopRec();
    }
    internal void SetWeatherFx(bool v)
    {
        _settings.WeatherFx = v; _settings.Save();
        if (v) { _weather.Start(); _sky = _weather.Condition; ApplyWeather(); }
        else { _weather.Stop(); _sky = Sky.Unknown; ApplyWeather(); }
    }
    internal void SetConfetti(bool v) { _settings.Confetti = v; _settings.Save(); if (v) Burst(); }   // little preview when you turn it on
    internal void SetWiggleWhenOpen(bool v) { _settings.WiggleWhenOpen = v; _settings.Save(); }
    internal void SetHeySiri(bool v)
    {
        _settings.HeySiri = v; _settings.Save();
        if (v) _voice.Start();
        else { _voice.Stop(); HideSiri(); _border?.HideSoon(0.2); }
    }
    internal void SetSiriBorder(bool v) { _settings.SiriBorder = v; _settings.Save(); if (!v) _border?.HideSoon(0.2); }
    internal void SetSiriBorderSize(double v) { _settings.SiriBorderSize = Clamp(v, 0.5, 2.5); _settings.Save(); _border?.SetSize(_settings.SiriBorderSize); }
    internal void SetSiriVoice(bool v) { _settings.SiriVoice = v; _settings.Save(); }
    internal void SetRgbSiri(bool v) { _settings.RgbSiri = v; _settings.Save(); OrbCtl.Rgb = v; }
    internal void SetRgbSiriBorder(bool v) { _settings.RgbSiriBorder = v; _settings.Save(); _border?.SetRgb(v); }

    internal void Shutdown() { _audio?.Dispose(); _top.Stop(); _ui.Stop(); _hover.Stop(); _timer.Stop(); _stats.Stop(); _noteTimer.Stop(); _notes.Stop(); _downloads.Stop(); _dlEndTimer.Stop(); _recClock.Stop(); _incoming.Stop(); _dropTimer.Stop(); _media.Stop(); _net.Stop(); _weather.Stop(); _wx.Stop(); _confetti.Stop(); _voice.Stop(); _siriHide.Stop(); OrbCtl.Stop(); _border?.Close(); try { _tts.Dispose(); } catch { } _devTimer.Stop(); _devBurst.Stop(); if (_devNotify != IntPtr.Zero) UnregisterDeviceNotification(_devNotify); DisposeGpu(); }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [DllImport("user32")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32")] static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32")] static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr filter, int flags);
    [DllImport("user32")] static extern bool UnregisterDeviceNotification(IntPtr handle);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32")] static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFO mi);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int max);
    [DllImport("kernel32")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    const int WM_CLIPBOARDUPDATE = 0x031D;
    const int WM_DEVICECHANGE = 0x0219, WM_POWERBROADCAST = 0x0218;
    const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
    const int DBT_DEVTYP_VOLUME = 2, DBT_DEVTYP_DEVICEINTERFACE = 5;
    const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x4;
    const int PBT_APMPOWERSTATUSCHANGE = 0xA;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size, dbcc_devicetype, dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
}
