using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

namespace Notch;

// a small siri orb that lives inside the notch's drop card. same flowing, twisting ribbon
// as the big one, just sized down. the twist comes from letting each band's amplitude swing
// through zero (edge-on) and flip, so it reads as a 3d band spinning on a horizontal axis.
public partial class SiriOrb : UserControl
{
    const double S = 44, Mid = 22, K = 0.19;

    sealed class Ribbon { public Path Path = null!; public double Amp, Phase, TwistOff, FlowOff; }

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly List<Ribbon> _ribbons = new();
    double _t;
    double _react = 0.35, _reactTarget = 0.35;   // eased mic loudness so the ribbon swells when you talk

    // mic loudness 0-100 while listening; the ribbon grows with your voice
    public void SetLevel(int level) => _reactTarget = Math.Clamp(level / 65.0, 0, 1);

    // a quick pop when a command lands
    public void Flash()
    {
        var a = new System.Windows.Media.Animation.DoubleAnimation(1.18, 1, TimeSpan.FromMilliseconds(340))
        { EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        OrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        OrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    LinearGradientBrush _multi = null!;
    SolidColorBrush _side1 = null!, _side2 = null!;
    bool _rgb;
    public bool Rgb { get => _rgb; set { _rgb = value; if (!value) SiriPalette(); } }

    public SiriOrb()
    {
        InitializeComponent();
        BuildRibbons();
        _timer.Tick += (s, e) => Frame();
    }

    public void Start() { if (!_timer.IsEnabled) _timer.Start(); }
    public void Stop() => _timer.Stop();

    void BuildRibbons()
    {
        _multi = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i < 6; i++) _multi.GradientStops.Add(new GradientStop(Colors.White, i / 5.0));
        _side1 = new SolidColorBrush();
        _side2 = new SolidColorBrush();
        SiriPalette();

        Add(_multi, 4.0, 7.0, 0.0, 0.0, 0.0, 3, 0.9);
        Add(new SolidColorBrush(Colors.White), 1.8, 6.0, 0.6, 0.5, 0.4, 1.5, 0.95);
        Add(_side1, 3.0, 7.6, 2.0, 1.1, 0.9, 4.5, 0.55);
        Add(_side2, 3.0, 7.2, 4.1, 2.2, 1.5, 4.5, 0.55);
    }

    void Add(Brush brush, double thick, double amp, double phase, double twistOff, double flowOff, double blur, double op)
    {
        var p = new Path
        {
            Stroke = brush, StrokeThickness = thick, Opacity = op,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = blur },
        };
        Wave.Children.Add(p);
        _ribbons.Add(new Ribbon { Path = p, Amp = amp, Phase = phase, TwistOff = twistOff, FlowOff = flowOff });
    }

    void SiriPalette()
    {
        string[] cols = { "#3B82F6", "#8B5CF6", "#FDBA74", "#FFFFFF", "#67E8F9", "#F472B6" };
        for (int i = 0; i < cols.Length; i++)
            _multi.GradientStops[i].Color = (Color)ColorConverter.ConvertFromString(cols[i]);
        _side1.Color = (Color)ColorConverter.ConvertFromString("#67E8F9");
        _side2.Color = (Color)ColorConverter.ConvertFromString("#F472B6");
    }

    void RainbowPalette()
    {
        double baseH = _t * 60;
        for (int i = 0; i < _multi.GradientStops.Count; i++)
            _multi.GradientStops[i].Color = Hsv(baseH + i * 40, 0.85, 1);
        _side1.Color = Hsv(baseH + 120, 0.9, 1);
        _side2.Color = Hsv(baseH + 240, 0.9, 1);
    }

    void Frame()
    {
        _t += 0.016;
        if (_rgb) RainbowPalette();
        _react += (_reactTarget - _react) * 0.22;   // ease toward the current mic level
        OrbReact.ScaleX = OrbReact.ScaleY = 1 + _react * 0.22;   // the whole orb grows while you talk
        double flow = _t * 2.1, twist = _t * 1.35;
        double react = 0.45 + 0.85 * _react;         // calm when quiet, swells when you talk
        foreach (var r in _ribbons)
        {
            double amp = r.Amp * react * Math.Cos(twist + r.TwistOff);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                bool first = true;
                for (double x = 0; x <= S; x += 2)
                {
                    double env = Math.Sin(Math.PI * x / S);
                    double y = Mid + env * amp * Math.Sin(K * x + flow + r.FlowOff + r.Phase);
                    var pt = new Point(x, y);
                    if (first) { ctx.BeginFigure(pt, false, false); first = false; }
                    else ctx.LineTo(pt, true, true);
                }
            }
            g.Freeze();
            r.Path.Data = g;
        }
    }

    static Color Hsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
