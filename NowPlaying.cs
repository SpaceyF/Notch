using System.IO;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Notch;

// reads whatever is playing using windows' built-in media info, so it works with
// any app (spotify, cider, a youtube tab) with no login or setup.
sealed class NowPlaying
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;

    public event Action? Changed;   // can fire on a background thread, so jump to the ui thread before using it

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public bool Playing { get; private set; }
    public byte[]? ArtBytes { get; private set; }

    // for the apple-style live activity: where we are in the track, and the shuffle/repeat state
    public TimeSpan Duration { get; private set; }
    public bool Shuffle { get; private set; }
    public int Repeat { get; private set; }        // 0 off, 1 whole list, 2 this track
    TimeSpan _pos;
    DateTimeOffset _posAt;

    // the position keeps moving between reads, so add the time elapsed since we last read it
    public TimeSpan Position
    {
        get
        {
            if (!Playing) return _pos;
            var t = _pos + (DateTimeOffset.Now - _posAt);
            return t > Duration ? Duration : t;
        }
    }

    public async Task Start()
    {
        _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _mgr.CurrentSessionChanged += (s, e) => Hook();
        _mgr.SessionsChanged += (s, e) => Hook();   // catch new tabs/apps (e.g. a youtube tab)
        Hook();
    }

    // prefer whatever is actually playing, then fall back to the system's current session
    GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_mgr == null) return null;
        try
        {
            foreach (var s in _mgr.GetSessions())
                try { if (s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return s; } catch { }
        }
        catch { }
        return _mgr.GetCurrentSession();
    }

    void Hook()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnAny;
            _session.PlaybackInfoChanged -= OnAny;
        }
        _session = PickSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnAny;
            _session.PlaybackInfoChanged += OnAny;
        }
        _ = Refresh();
    }

    void OnAny(GlobalSystemMediaTransportControlsSession sender, object args) => _ = Refresh();

    // send commands to whatever app is playing, for the hover controls
    public async void TogglePlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Prev() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

    // read the current position/duration and shuffle/repeat from whatever's playing
    public void RefreshTimeline()
    {
        var s = _session;
        if (s == null) { Duration = TimeSpan.Zero; _pos = TimeSpan.Zero; return; }
        try
        {
            var tl = s.GetTimelineProperties();
            Duration = tl.EndTime - tl.StartTime;
            _pos = tl.Position;
            _posAt = DateTimeOffset.Now;
            var pi = s.GetPlaybackInfo();
            Shuffle = pi.IsShuffleActive ?? false;
            Repeat = pi.AutoRepeatMode switch
            {
                MediaPlaybackAutoRepeatMode.Track => 2,
                MediaPlaybackAutoRepeatMode.List => 1,
                _ => 0,
            };
        }
        catch { }
    }

    public async void Seek(double frac)
    {
        try
        {
            var s = _session;
            if (s == null || Duration <= TimeSpan.Zero) return;
            frac = Math.Clamp(frac, 0, 1);
            long ticks = (long)(Duration.Ticks * frac);
            await s.TryChangePlaybackPositionAsync(ticks);
            _pos = TimeSpan.FromTicks(ticks);
            _posAt = DateTimeOffset.Now;
        }
        catch { }
    }

    public async void ToggleShuffle() { try { if (_session != null) await _session.TryChangeShuffleActiveAsync(!Shuffle); } catch { } }

    public async void CycleRepeat()
    {
        try
        {
            if (_session == null) return;
            var next = Repeat == 0 ? MediaPlaybackAutoRepeatMode.List
                     : Repeat == 1 ? MediaPlaybackAutoRepeatMode.Track
                     : MediaPlaybackAutoRepeatMode.None;
            await _session.TryChangeAutoRepeatModeAsync(next);
        }
        catch { }
    }

    async Task Refresh()
    {
        var s = _session;
        if (s == null)
        {
            Title = ""; Artist = ""; Playing = false; ArtBytes = null;
            Changed?.Invoke();
            return;
        }
        try
        {
            Playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var p = await s.TryGetMediaPropertiesAsync();
            Title = p.Title ?? "";
            Artist = p.Artist ?? "";
            ArtBytes = p.Thumbnail != null ? await ReadThumb(p.Thumbnail) : null;
        }
        catch { }
        Changed?.Invoke();
    }

    static async Task<byte[]?> ReadThumb(IRandomAccessStreamReference r)
    {
        try
        {
            using var stream = await r.OpenReadAsync();
            using var net = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
