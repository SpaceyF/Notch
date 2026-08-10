using NAudio.Dsp;
using NAudio.Wave;

namespace Notch;

// listens to the sound coming out of the speakers and splits it into frequencies
// so the bars can dance to whatever is actually playing.
sealed class AudioLevels : IDisposable
{
    const int FftLen = 1024;
    const int FftBits = 10;                 // 1024 is 2 to the 10th

    readonly WasapiLoopbackCapture _cap;
    readonly Complex[] _fft = new Complex[FftLen];
    readonly int _bars;
    readonly float[] _levels;               // the smoothed bar heights the ui reads
    readonly object _lock = new();
    int _pos;
    volatile float _sens = 1f;   // how strongly the bars react

    public void SetSensitivity(double s) => _sens = (float)s;

    public AudioLevels(int bars = 18)
    {
        _bars = bars;
        _levels = new float[bars];
        _cap = new WasapiLoopbackCapture();
        _cap.DataAvailable += OnData;
        try { _cap.StartRecording(); } catch { }
    }

    public float[] Snapshot() { lock (_lock) return (float[])_levels.Clone(); }

    void OnData(object? s, WaveInEventArgs e)
    {
        var fmt = _cap.WaveFormat;
        int step = fmt.BitsPerSample / 8 * fmt.Channels;
        for (int i = 0; i + step <= e.BytesRecorded; i += step)
        {
            float sample = fmt.BitsPerSample == 32
                ? BitConverter.ToSingle(e.Buffer, i)
                : BitConverter.ToInt16(e.Buffer, i) / 32768f;
            _fft[_pos].X = (float)(sample * FastFourierTransform.HannWindow(_pos, FftLen));
            _fft[_pos].Y = 0;
            if (++_pos >= FftLen) { Compute(); _pos = 0; }
        }
    }

    void Compute()
    {
        FastFourierTransform.FFT(true, FftBits, _fft);
        int half = FftLen / 2;
        var frame = new float[_bars];
        for (int b = 0; b < _bars; b++)
        {
            // spread the bars so low and high sounds each get a fair share
            int lo = Math.Clamp((int)Math.Pow(half, (double)b / _bars), 1, half - 1);
            int hi = Math.Clamp((int)Math.Pow(half, (double)(b + 1) / _bars), lo + 1, half);
            float peak = 0;
            for (int k = lo; k < hi; k++)
            {
                float m = (float)Math.Sqrt(_fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y);
                if (m > peak) peak = m;
            }
            frame[b] = (float)Math.Clamp(Math.Log10(1 + peak * 45 * _sens) * 0.85, 0, 1);
        }
        lock (_lock)
            for (int b = 0; b < _bars; b++)
                _levels[b] = frame[b] > _levels[b] ? frame[b] : _levels[b] * 0.80f + frame[b] * 0.20f;  // jump up quick, fall down slow
    }

    public void Dispose() { try { _cap.StopRecording(); _cap.Dispose(); } catch { } }
}
