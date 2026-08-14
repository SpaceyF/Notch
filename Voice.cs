using System.Globalization;
using System.IO;
using System.Speech.Recognition;

namespace Notch;

// offline "hey siri" voice commands. runs an in-process windows recognizer with a fixed
// grammar of "hey siri <command>" phrases, so no internet and no cloud. it only ever reacts
// to the phrases below, nothing else you say gets matched or sent anywhere.
sealed class Voice
{
    // key -> (label shown in settings, the phrases that trigger it). the overlay maps the key
    // to what actually happens. add rows here to add commands.
    public static readonly (string Key, string Label, string[] Phrases)[] Commands =
    {
        ("next",         "Skip song",        new[] { "skip song", "next song", "next track", "skip this song", "skip" }),
        ("prev",         "Previous song",    new[] { "previous song", "last song", "go back a song", "previous track" }),
        ("playpause",    "Play / pause",     new[] { "play", "pause", "play music", "pause music", "pause the music", "resume", "resume music" }),
        ("shuffle",      "Shuffle",          new[] { "shuffle", "shuffle music", "toggle shuffle" }),
        ("nowplaying",   "What's playing",   new[] { "what song is this", "what's this song", "what is this song", "name this song", "what's playing", "what is playing", "song", "current song" }),
        ("close:discord","Close Discord",    new[] { "close discord", "quit discord", "kill discord" }),
        ("close:spotify","Close Spotify",    new[] { "close spotify", "quit spotify" }),
        ("close:chrome", "Close Chrome",     new[] { "close chrome", "quit chrome" }),
        ("close:steam",  "Close Steam",      new[] { "close steam", "quit steam" }),
        ("open:discord", "Open Discord",     new[] { "open discord", "launch discord", "start discord" }),
        ("open:spotify", "Open Spotify",     new[] { "open spotify", "launch spotify" }),
        ("open:steam",   "Open Steam",       new[] { "open steam", "launch steam" }),
        ("volup",        "Volume up",        new[] { "volume up", "turn it up", "louder" }),
        ("voldown",      "Volume down",      new[] { "volume down", "turn it down", "quieter" }),
        ("mute",         "Mute / unmute",    new[] { "mute", "mute volume", "unmute" }),
        ("screenshot",   "Screenshot",       new[] { "take a screenshot", "screenshot", "take a screen shot" }),
        ("time",         "What time is it",  new[] { "what time is it", "what's the time", "what is the time", "tell me the time" }),
        ("minimize",     "Show desktop",     new[] { "minimize everything", "show desktop", "minimize all" }),
        ("lock",         "Lock the PC",      new[] { "lock the pc", "lock my pc", "lock the computer" }),
    };

    const string Wake = "hey siri";

    SpeechRecognitionEngine? _engine;
    readonly Dictionary<string, string> _phraseToKey = new(StringComparer.OrdinalIgnoreCase);
    Grammar? _dyn;   // the free-text "search .../type ..." grammar
    bool _running;

    // the open-ended verbs, longest first so "search up" beats "search"
    static readonly (string Prefix, string Kind)[] DynVerbs =
    {
        ("search up ", "search"), ("look up ", "search"), ("search for ", "search"),
        ("search ", "search"), ("google ", "search"), ("type ", "type"),
        ("open ", "open"), ("launch ", "open"), ("start ", "open"),
        // smart questions, answered locally (math / convert / spell / define)
        ("how much is ", "math"), ("calculate ", "math"), ("what is ", "math"), ("what's ", "math"),
        ("how many ", "convert"), ("convert ", "convert"),
        ("what does ", "define"), ("define ", "define"),
        ("spell ", "spell"),
    };

    // fired (on a background thread) when a "hey siri ..." is starting to be heard
    public event Action? Wakened;
    // fired with a command key once a full phrase is recognized confidently
    public event Action<string>? Command;
    // fired for the open-ended ones: (kind, System.Speech's guess, the recorded audio so a
    // better engine can re-transcribe the messy free-text part). e.g. ("search", "...", wav)
    public event Action<string, string, byte[]?>? Dynamic;
    // fired when it clearly wasn't a "hey siri" at all: hide quietly
    public event Action? Dismissed;
    // fired when it heard "hey siri" but couldn't make out the rest: show "didn't catch that"
    public event Action? Misheard;
    // mic loudness 0-100, so the orb can react to your voice
    public event Action<int>? Level;

    public void Start()
    {
        if (_running) return;
        try
        {
            var eng = new SpeechRecognitionEngine(new CultureInfo("en-US"));

            var choices = new Choices();
            foreach (var (key, _, phrases) in Commands)
                foreach (var p in phrases)
                {
                    _phraseToKey[p] = key;
                    choices.Add(p);
                }

            var gb = new GrammarBuilder(Wake) { Culture = new CultureInfo("en-US") };
            gb.Append(choices);
            eng.LoadGrammar(new Grammar(gb));

            // a second grammar for the open-ended ones: "hey siri <verb> <free speech>"
            try
            {
                var verbs = new Choices(DynVerbs.Select(v => v.Prefix.Trim()).Distinct().ToArray());
                var gd = new GrammarBuilder(Wake) { Culture = new CultureInfo("en-US") };
                gd.Append(verbs);
                gd.AppendDictation();
                _dyn = new Grammar(gd) { Name = "dyn" };
                eng.LoadGrammar(_dyn);
            }
            catch { _dyn = null; }   // no dictation support: the fixed commands still work

            eng.SpeechHypothesized += OnHypothesized;
            eng.SpeechRecognized += OnRecognized;
            eng.SpeechRecognitionRejected += (s, e) => Dismissed?.Invoke();
            eng.AudioLevelUpdated += (s, e) => Level?.Invoke(e.AudioLevel);

            eng.SetInputToDefaultAudioDevice();
            eng.RecognizeAsync(RecognizeMode.Multiple);
            _engine = eng;
            _running = true;
        }
        catch { Stop(); }   // no mic, or no en-US recognizer installed
    }

    public void Stop()
    {
        _running = false;
        var eng = _engine; _engine = null;
        if (eng == null) return;
        try { eng.RecognizeAsyncCancel(); } catch { }
        try { eng.Dispose(); } catch { }
    }

    // the exact audio the recognizer heard, as a wav, so whisper can re-transcribe it
    static byte[]? CaptureAudio(SpeechRecognizedEventArgs e)
    {
        try { using var ms = new MemoryStream(); e.Result.Audio.WriteToWaveStream(ms); return ms.ToArray(); }
        catch { return null; }
    }

    // light up the orb once it's actually heard "siri", not just "hey" on its own
    void OnHypothesized(object? s, SpeechHypothesizedEventArgs e)
    {
        if (e.Result.Text.Contains("siri", StringComparison.OrdinalIgnoreCase))
            Wakened?.Invoke();
    }

    void OnRecognized(object? s, SpeechRecognizedEventArgs e)
    {
        var text = e.Result.Text ?? "";
        // not a "hey siri" at all: hide quietly, don't nag
        if (!text.StartsWith(Wake, StringComparison.OrdinalIgnoreCase)) { Dismissed?.Invoke(); return; }
        var rest = text.Substring(Wake.Length).Trim();

        // the open-ended "search .../type ..." grammar uses dictation, which mishears a lot.
        // hold it to a real bar so it stops confidently typing similar-sounding gibberish.
        if (_dyn != null && ReferenceEquals(e.Result.Grammar, _dyn))
        {
            // lower bar than before: whisper re-checks the payload, so we just need to be sure
            // it was a "hey siri <verb> ..." at all
            if (e.Result.Confidence < 0.45f) { Misheard?.Invoke(); return; }
            foreach (var (prefix, kind) in DynVerbs)
                if (rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var payload = rest.Substring(prefix.Length).Trim();
                    if (payload.Length > 0) { Dynamic?.Invoke(kind, payload, CaptureAudio(e)); return; }
                    break;
                }
            Misheard?.Invoke();
            return;
        }

        if (e.Result.Confidence < 0.5f) { Misheard?.Invoke(); return; }
        if (_phraseToKey.TryGetValue(rest, out var key)) Command?.Invoke(key);
        else Misheard?.Invoke();
    }
}
