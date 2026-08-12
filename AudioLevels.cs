using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Dsp;
using NAudio.Wave;

namespace Notch;

// listens to the sound coming out of the speakers and splits it into frequencies
// so the bars can dance to whatever is actually playing.
//
// the loopback capture binds to the default playback device when it starts, so if you
// unplug/replug headphones (or switch outputs) the old device goes dead and the bars
// freeze. we watch for the default device changing (and for the capture stopping) and
// rebuild onto the new device, debounced so a burst of events only rebuilds once.
sealed class AudioLevels : IDisposable, IMMNotificationClient
{
    const int FftLen = 1024;
    const int FftBits = 10;                 // 1024 is 2 to the 10th

    WasapiLoopbackCapture? _cap;
    readonly MMDeviceEnumerator _enum = new();
    readonly object _capLock = new();
    System.Threading.Timer? _rebuild;
    bool _disposed;

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
        try { _enum.RegisterEndpointNotificationCallback(this); } catch { }
        Build();
    }

    public float[] Snapshot() { lock (_lock) return (float[])_levels.Clone(); }

    // (re)create the capture on the current default output device
    void Build()
    {
        lock (_capLock)
        {
            if (_disposed) return;
            var old = _cap;
            if (old != null)
            {
                old.DataAvailable -= OnData;
                old.RecordingStopped -= OnStopped;
                try { old.StopRecording(); } catch { }
                try { old.Dispose(); } catch { }
            }
            _pos = 0;
            try
            {
                _cap = new WasapiLoopbackCapture();
                _cap.DataAvailable += OnData;
                _cap.RecordingStopped += OnStopped;
                _cap.StartRecording();
            }
            catch { _cap = null; }
        }
    }

    // the capture died (device removed): rebuild onto whatever's default now
    void OnStopped(object? s, StoppedEventArgs e) => ScheduleRebuild();

    // device changes fire a burst (one per role), so coalesce them into a single rebuild
    void ScheduleRebuild()
    {
        lock (_capLock)
        {
            if (_disposed) return;
            _rebuild?.Dispose();
            _rebuild = new System.Threading.Timer(_ => Build(), null, 300, System.Threading.Timeout.Infinite);
        }
    }

    void OnData(object? s, WaveInEventArgs e)
    {
        var cap = s as WasapiLoopbackCapture ?? _cap;
        if (cap == null) return;
        var fmt = cap.WaveFormat;
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

    // IMMNotificationClient: the only one we care about is the default output switching
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    { if (flow == DataFlow.Render) ScheduleRebuild(); }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _disposed = true;
        try { _enum.UnregisterEndpointNotificationCallback(this); } catch { }
        lock (_capLock)
        {
            _rebuild?.Dispose();
            if (_cap != null)
            {
                _cap.DataAvailable -= OnData;
                _cap.RecordingStopped -= OnStopped;
                try { _cap.StopRecording(); } catch { }
                try { _cap.Dispose(); } catch { }
                _cap = null;
            }
        }
        try { _enum.Dispose(); } catch { }
    }
}
