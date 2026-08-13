using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Notch;

// the full-screen "siri border": a neon frame of colored light that wraps the screen edge,
// flows around, pulses, and bleeds softly inward while the center stays clear. shows while
// siri is listening. two palettes: the normal siri blue/purple/pink, or a rainbow rgb one.
public partial class SiriBorderWindow : Window
{
    readonly RotateTransform _sweep = new() { CenterX = 0.5, CenterY = 0.5 };
    readonly DispatcherTimer _hide = new();
    bool _shown, _rgb;

    static readonly string[] SiriCols = { "#3B82F6", "#8B5CF6", "#EC4899", "#FFFFFF", "#22D3EE", "#3B82F6" };
    static readonly string[] RgbCols = { "#FF0000", "#FF8A00", "#FFE100", "#33FF00", "#00A2FF", "#8B00FF", "#FF0000" };

    public SiriBorderWindow()
    {
        InitializeComponent();
        _hide.Tick += (s, e) => { _hide.Stop(); FadeOut(); };
        Loaded += (s, e) => Place();
        Paint();
    }

    // build the rotating gradient the frame is stroked with
    void Paint()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var cols = _rgb ? RgbCols : SiriCols;
        for (int i = 0; i < cols.Length; i++)
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(cols[i]), (double)i / (cols.Length - 1)));
        b.RelativeTransform = _sweep;
        FrameGlow.Stroke = b;
        FrameCore.Stroke = b;
    }

    public void SetRgb(bool rgb)
    {
        if (_rgb == rgb) return;
        _rgb = rgb;
        Paint();
    }

    // scale how thick and how far the glow reaches
    public void SetSize(double m)
    {
        m = Math.Clamp(m, 0.4, 3.0);
        FrameGlow.StrokeThickness = 70 * m;
        ((System.Windows.Media.Effects.BlurEffect)FrameGlow.Effect).Radius = 55 * m;
        FrameCore.StrokeThickness = 16 * m;
        ((System.Windows.Media.Effects.BlurEffect)FrameCore.Effect).Radius = 12 * Math.Max(0.7, m);
    }

    public void ShowBorder(bool rgb)
    {
        _hide.Stop();
        if (_rgb != rgb) { _rgb = rgb; Paint(); }
        if (_shown) return;
        _shown = true;
        Show();
        Place();
        // colors sweep around the frame
        _sweep.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(7)) { RepeatBehavior = RepeatBehavior.Forever });
        // gentle breathing pulse
        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        FrameGlow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.4, 0.62, TimeSpan.FromMilliseconds(1100)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    // called when it hears you talking: a quick brighten so it reacts to your voice
    public void Pulse()
    {
        if (!_shown) return;
        FrameCore.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, 0.9, TimeSpan.FromMilliseconds(260)) { AutoReverse = false });
    }

    public void HideSoon(double seconds = 1.0)
    {
        if (!_shown) return;
        _hide.Stop();
        _hide.Interval = TimeSpan.FromSeconds(seconds);
        _hide.Start();
    }

    void FadeOut()
    {
        var a = new DoubleAnimation(Root.Opacity, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (s, e) =>
        {
            _shown = false;
            _sweep.BeginAnimation(RotateTransform.AngleProperty, null);
            FrameGlow.BeginAnimation(OpacityProperty, null);
            Hide();
        };
        Root.BeginAnimation(OpacityProperty, a);
    }

    // cover the whole primary screen, sit on top, never take clicks or focus
    void Place()
    {
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32")] static extern int SetWindowLong(IntPtr h, int i, int v);
}
