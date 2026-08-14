using System.IO;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace Notch;

// text to speech using the modern windows voices (Windows.Media.SpeechSynthesis), which
// exposes the good voices (Mark, the newer David/Zira), not just the two old "Desktop" ones
// the classic api sees. we synth to a wav stream and play it through NAudio, which is far
// more reliable in a plain desktop app than the WinRT MediaPlayer.
sealed class Tts
{
    readonly SpeechSynthesizer _synth = new();
    WaveOutEvent? _out;
    WaveFileReader? _reader;
    MemoryStream? _ms;

    public List<string> Voices()
    {
        try { return SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).ToList(); }
        catch { return new(); }
    }

    public string CurrentVoice() { try { return _synth.Voice?.DisplayName ?? ""; } catch { return ""; } }

    public void SetVoice(string name)
    {
        try
        {
            var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.DisplayName == name);
            if (v != null) _synth.Voice = v;
        }
        catch { }
    }

    // -5 (slow) .. 5 (fast) onto the engine's speaking rate (1.0 is normal)
    public void SetRate(int r)
    {
        try { _synth.Options.SpeakingRate = Math.Clamp(1.0 + r * 0.16, 0.5, 3.0); } catch { }
    }

    public async void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            var ms = new MemoryStream();
            using (var s = stream.AsStreamForRead()) await s.CopyToAsync(ms);
            ms.Position = 0;

            Stop();
            _ms = ms;
            _reader = new WaveFileReader(ms);
            _out = new WaveOutEvent();
            _out.Init(_reader);
            _out.Play();
        }
        catch { }
    }

    public void Stop()
    {
        try { _out?.Stop(); } catch { }
        try { _out?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _ms?.Dispose(); } catch { }
        _out = null; _reader = null; _ms = null;
    }

    public void Dispose() { Stop(); try { _synth.Dispose(); } catch { } }
}
