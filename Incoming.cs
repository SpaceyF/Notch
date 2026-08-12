using System.IO;
using Timer = System.Timers.Timer;

namespace Notch;

// watches a few folders and fires when a finished file "lands" in one, airdrop style.
// browser downloads are the ring's job, so this is for stuff that just appears: screenshots,
// nearby-share / phone transfers, anything dropped into the notch drop folder. we wait for the
// file to stop growing and unlock before firing so you never get a half-written flash.
sealed class Incoming
{
    // half-written temp files to ignore until they turn into the real thing
    static readonly string[] Ignore = { ".crdownload", ".part", ".download", ".opdownload", ".partial", ".tmp", ".aria2", ".!ut" };

    readonly List<FileSystemWatcher> _watchers = new();
    readonly Timer _settle = new(450) { AutoReset = true };
    readonly Dictionary<string, (long size, int stable)> _pending = new();   // path -> last size + how many settled reads
    readonly object _lock = new();
    bool _running;

    public string DropFolder { get; }

    // fires with the finished file's full path once it's done landing
    public event Action<string>? Landed;

    public Incoming()
    {
        DropFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Notch Drop");
        _settle.Elapsed += (s, e) => Settle();
    }

    // where we watch out of the box: the notch drop folder + the screenshots folder
    public IEnumerable<string> DefaultFolders()
    {
        yield return DropFolder;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }

    public void Start(IEnumerable<string> folders)
    {
        Stop();
        try { Directory.CreateDirectory(DropFolder); } catch { }
        foreach (var dir in folders.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct())
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                var w = new FileSystemWatcher(dir) { IncludeSubdirectories = false };
                w.Created += OnTouched;
                w.Renamed += OnTouched;
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch { }
        }
        _running = true;
        _settle.Start();
    }

    public void Stop()
    {
        _running = false;
        _settle.Stop();
        foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
        _watchers.Clear();
        lock (_lock) _pending.Clear();
    }

    void OnTouched(object s, FileSystemEventArgs e)
    {
        if (!_running) return;
        var path = e.FullPath;
        try
        {
            if (Directory.Exists(path)) return;                          // ignore folders
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(Ignore, ext) >= 0) return;                 // still-writing temp file
            var name = Path.GetFileName(path);
            if (name.StartsWith("~") || name.StartsWith(".")) return;    // temp / hidden
        }
        catch { return; }
        lock (_lock) _pending[path] = (-1, 0);
    }

    // once a pending file stops changing and can be opened, it's landed
    void Settle()
    {
        if (!_running) return;
        var done = new List<string>();
        lock (_lock)
        {
            foreach (var path in _pending.Keys.ToList())
            {
                long size;
                try
                {
                    if (!File.Exists(path)) { _pending.Remove(path); continue; }
                    size = new FileInfo(path).Length;
                }
                catch { _pending.Remove(path); continue; }

                var (last, stable) = _pending[path];
                if (size == last && size > 0 && IsUnlocked(path))
                {
                    done.Add(path);            // held steady one full tick and openable: it's done
                    _pending.Remove(path);
                }
                else _pending[path] = (size, 0);
            }
        }
        foreach (var p in done) Landed?.Invoke(p);
    }

    static bool IsUnlocked(string path)
    {
        try { using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch { return false; }
    }
}
