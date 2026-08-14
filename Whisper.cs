using System.IO;
using System.Net.Http;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace Notch;

// runs whisper (tiny.en) on a short audio clip to transcribe the free-text part of a command
// ("type ...", "search ...", "open ..."). the fast System.Speech grammar still handles the
// wake word + quick commands; this only re-listens to the messy free-text bit for accuracy.
// the ~75mb model downloads once in the background and caches; the factory loads lazily and
// unloads after a while idle so it isn't sitting in ram.
sealed class Whisper
{
    const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin";
    static string ModelPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch", "ggml-tiny.en.bin");

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    readonly SemaphoreSlim _gate = new(1, 1);
    WhisperFactory? _factory;
    WhisperProcessor? _proc;
    DateTime _lastUse;

    public bool ModelReady => File.Exists(ModelPath);

    // download the model once; safe to call repeatedly (no-op once it's there)
    public async Task<bool> EnsureModel()
    {
        if (ModelReady) return true;
        await _gate.WaitAsync();
        try
        {
            if (ModelReady) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            var tmp = ModelPath + ".part";
            using (var resp = await Http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs);
            }
            File.Move(tmp, ModelPath, true);
            return true;
        }
        catch { return false; }
        finally { _gate.Release(); }
    }

    public async Task<string> Transcribe(byte[] wav)
    {
        if (!ModelReady) return "";
        await _gate.WaitAsync();
        try
        {
            _factory ??= WhisperFactory.FromPath(ModelPath);
            _proc ??= _factory.CreateBuilder().WithLanguage("en").Build();
            _lastUse = DateTime.Now;
            using var wav16 = To16kMonoWav(wav);
            var sb = new System.Text.StringBuilder();
            await foreach (var seg in _proc.ProcessAsync(wav16))
                sb.Append(seg.Text);
            return sb.ToString().Trim();
        }
        catch { return ""; }
        finally { _gate.Release(); }
    }

    // whisper wants 16khz mono; System.Speech hands us its own format, so convert
    static MemoryStream To16kMonoWav(byte[] src)
    {
        using var reader = new WaveFileReader(new MemoryStream(src));
        ISampleProvider sp = reader.ToSampleProvider();
        if (reader.WaveFormat.Channels > 1)
            sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };
        var resampler = new WdlResamplingSampleProvider(sp, 16000);
        var outMs = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(outMs, resampler.ToWaveProvider16());
        outMs.Position = 0;
        return outMs;
    }

    // free the model from ram if it hasn't been used in a while
    public void UnloadIfIdle(TimeSpan after)
    {
        if (_factory == null || DateTime.Now - _lastUse < after) return;
        if (!_gate.Wait(0)) return;
        try { _proc?.Dispose(); _factory?.Dispose(); _proc = null; _factory = null; }
        catch { }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        try { _proc?.Dispose(); _factory?.Dispose(); } catch { }
        _proc = null; _factory = null;
    }
}
