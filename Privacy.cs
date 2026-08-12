using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace Notch;

// checks whether the mic, camera, or screen is being used right now. windows keeps a list
// of which apps used each and when. if an app has a start time but no stop time, it's still
// using it. we check both the normal apps and the "NonPackaged" ones.
//
// two catches we handle:
//  - an app that crashes / gets force-killed while holding a device never writes its stop
//    time, so it reads as "in use" forever. we require the owning app to still be running.
//  - overlay / call / share apps (discord above all) hold a screen-capture session open
//    the whole time they're running, which would light the REC pill constantly. so for the
//    screen we also ignore those, and only flag an actual capture from something else.
static class Privacy
{
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static bool MicInUse => InUse("microphone");
    public static bool CamInUse => InUse("webcam");
    // screen sharing / recording, for apps that use the modern windows capture api
    public static bool ScreenInUse => Capturing("graphicsCaptureProgrammatic") || Capturing("graphicsCaptureWithoutBorder");

    // apps that keep a capture session open for overlays, calls, or screen share rather than
    // recording. none of these should light the REC pill. easy to add more if one slips through.
    static readonly HashSet<string> ScreenIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discordptb", "discordcanary", "discorddevelopment",
        "teams", "ms-teams", "msteams", "slack", "zoom",
        "steam", "steamwebhelper", "nvidia share", "nvcontainer", "nvsphelper64",
        "lghub", "logioverlay", "wallpaper32", "wallpaper64",
    };

    // apps that clamp the mic/cam open the whole time they run, so the dot would never turn
    // off. discord's voice-activity mode holds the mic stream open constantly, call or not.
    static readonly HashSet<string> DeviceIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discordptb", "discordcanary", "discorddevelopment",
        "nvidia broadcast", "voicemeeter", "voicemeeter8", "voicemeeter8x64",
    };

    static bool InUse(string capability)      // mic / cam: skip the always-on holders
        => AnyActive(Registry.CurrentUser, Root + capability, DeviceIgnore, trustUnknown: true)
        || AnyActive(Registry.LocalMachine, Root + capability, DeviceIgnore, trustUnknown: true);

    static bool Capturing(string capability)  // screen: skip the overlay / share apps
        => AnyActive(Registry.CurrentUser, Root + capability, ScreenIgnore, trustUnknown: false)
        || AnyActive(Registry.LocalMachine, Root + capability, ScreenIgnore, trustUnknown: false);

    static bool AnyActive(RegistryKey hive, string path, HashSet<string> ignore, bool trustUnknown)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            if (k == null) return false;
            var running = RunningExeNames();
            foreach (var name in k.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var np = k.OpenSubKey(name);
                    if (np != null)
                        foreach (var app in np.GetSubKeyNames())
                            if (Active(np, app) && Ok(AppName(app, true), running, ignore, trustUnknown)) return true;
                }
                else if (Active(k, name) && Ok(AppName(name, false), running, ignore, trustUnknown)) return true;
            }
        }
        catch { }
        return false;
    }

    static bool Active(RegistryKey parent, string name)
    {
        try
        {
            using var s = parent.OpenSubKey(name);
            if (s?.GetValue("LastUsedTimeStart") is long start &&
                s?.GetValue("LastUsedTimeStop") is long stop)
                return start > 0 && stop == 0;   // started, never stopped = in use now
        }
        catch { }
        return false;
    }

    static bool Ok(string exe, HashSet<string> running, HashSet<string> ignore, bool trustUnknown)
    {
        if (exe.Length == 0) return trustUnknown;    // unknown owner: trust for a dot, not for REC
        if (!running.Contains(exe)) return false;    // phantom left by a dead app
        if (ignore.Contains(exe)) return false;      // always-on holder / overlay, not real use
        return true;
    }

    // resolve a consent-store subkey to the owning exe's base name (lowercased).
    // nonpackaged keys are the full path with '#' for slashes; packaged keys are a
    // family name like "Microsoft.WindowsCamera_8wekyb3d8bbwe".
    static string AppName(string keyName, bool nonPackaged)
    {
        try
        {
            if (nonPackaged)
                return Path.GetFileNameWithoutExtension(keyName.Replace('#', '\\')).ToLowerInvariant();
            return keyName.Split('_')[0].Split('.').Last().ToLowerInvariant();
        }
        catch { return ""; }
    }

    // the set of running process names, cached briefly so the ~1/sec dot + recording checks
    // don't each walk the whole process list
    static HashSet<string>? _cache;
    static long _cacheAt;
    static HashSet<string> RunningExeNames()
    {
        long now = Environment.TickCount64;
        if (_cache != null && now - _cacheAt < 750) return _cache;
        var set = new HashSet<string>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try { set.Add(p.ProcessName.ToLowerInvariant()); } catch { } finally { p.Dispose(); }
            }
        }
        catch { }
        _cache = set; _cacheAt = now;
        return set;
    }
}
