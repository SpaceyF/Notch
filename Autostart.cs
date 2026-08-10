using Microsoft.Win32;

namespace Notch;

// turns "start with windows" on or off by adding or removing an entry in the
// windows startup list (the registry run key for the current user).
static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Notch";

    public static bool Enabled
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) != null; }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true) ?? Registry.CurrentUser.CreateSubKey(Key);
            if (k == null) return;
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
        }
        catch { }
    }
}
