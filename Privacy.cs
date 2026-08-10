using Microsoft.Win32;

namespace Notch;

// checks whether the mic or camera is being used right now. windows keeps a list
// of which apps used them and when. if an app has a start time but no stop time,
// it's still using it. we check both the normal apps and the "NonPackaged" ones.
static class Privacy
{
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static bool MicInUse => InUse("microphone");
    public static bool CamInUse => InUse("webcam");
    // screen sharing / recording, for apps that use the modern windows capture api
    public static bool ScreenInUse => InUse("graphicsCaptureProgrammatic") || InUse("graphicsCaptureWithoutBorder");

    static bool InUse(string capability)
        => AnyActive(Registry.CurrentUser, Root + capability)
        || AnyActive(Registry.LocalMachine, Root + capability);

    static bool AnyActive(RegistryKey hive, string path)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            if (k == null) return false;
            foreach (var name in k.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var np = k.OpenSubKey(name);
                    if (np != null)
                        foreach (var app in np.GetSubKeyNames())
                            if (Active(np, app)) return true;
                }
                else if (Active(k, name)) return true;
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
}
