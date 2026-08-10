using System.IO;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Notch;

// watches the downloads folder for a browser's partial-download file and reports
// how it's going. we can't know the total size ahead of time (the browsers don't
// store it in the temp file), so the ring is a spinner with a live byte counter,
// then a done flash when the temp file turns into the finished file.
sealed class Downloads
{
    // the half-finished files browsers leave while a download runs
    static readonly string[] PartExts = { ".crdownload", ".part", ".download", ".opdownload", ".partial" };

    readonly string _dir;
    readonly Timer _poll = new(700) { AutoReset = true };
    readonly Dictionary<string, long> _active = new();   // partial path -> last seen size
    string _newest = "";                                 // the partial we show the ring for
    bool _running;

    // name (no partial extension) + bytes so far, fired every poll while downloading
    public event Action<string, long>? Active;
    // fired once with the finished file name when a partial becomes a real file
    public event Action<string>? Done;
    // fired when the last download leaves, whether it finished or got cancelled
    public event Action? AllDone;

    public Downloads()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        _poll.Elapsed += Tick;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _active.Clear();
        _newest = "";
        _poll.Start();
    }

    public void Stop()
    {
        _running = false;
        _poll.Stop();
        _active.Clear();
    }

    void Tick(object? s, ElapsedEventArgs e)
    {
        if (!_running) return;
        try
        {
            var seen = new HashSet<string>();
            string newestPath = _newest;
            DateTime newestTime = DateTime.MinValue;

            if (Directory.Exists(_dir))
            {
                foreach (var ext in PartExts)
                {
                    foreach (var path in Directory.EnumerateFiles(_dir, "*" + ext))
                    {
                        seen.Add(path);
                        long size = 0;
                        DateTime when = DateTime.MinValue;
                        try { var fi = new FileInfo(path); size = fi.Length; when = fi.CreationTimeUtc; } catch { }
                        _active[path] = size;
                        if (when > newestTime) { newestTime = when; newestPath = path; }
                    }
                }
            }

            // any partial we were tracking that's gone now either finished or was cancelled
            foreach (var path in _active.Keys.ToList())
            {
                if (seen.Contains(path)) continue;
                _active.Remove(path);
                string final = StripPart(path);
                if (File.Exists(final)) Done?.Invoke(Path.GetFileName(final));
            }

            if (_active.Count == 0)
            {
                if (_newest != "") { _newest = ""; AllDone?.Invoke(); }
                return;
            }

            _newest = seen.Contains(newestPath) ? newestPath : _active.Keys.First();
            Active?.Invoke(Path.GetFileName(StripPart(_newest)), _active[_newest]);
        }
        catch { }
    }

    static string StripPart(string path)
    {
        foreach (var ext in PartExts)
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return path.Substring(0, path.Length - ext.Length);
        return path;
    }
}
