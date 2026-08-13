using System.IO;
using System.Text.Json;

namespace Notch;

enum NotchStyle { Notch, Island }
enum VisualStyle { Bars, Dots }

// a user-set mapping from a plugged-in device (its usb vid:pid) to a model + name
sealed class DeviceRule
{
    public string Vp { get; set; } = "";     // e.g. "0B05:1ACE"
    public string Name { get; set; } = "";   // what to call it on the card
    public string Type { get; set; } = "";   // which model preset to show
}

// everything the user can change, saved to a file so it sticks between launches
sealed class NotchSettings
{
    public NotchStyle Style { get; set; } = NotchStyle.Island;
    public string Accent { get; set; } = "#3DD6C4";   // teal by default
    public bool ArtLeft { get; set; } = true;          // which side the album art is on
    public double WidthScale { get; set; } = 1.0;      // how wide, 0.6 to 2.0
    public double HeightScale { get; set; } = 1.0;     // how tall, 0.6 to 2.5
    public bool AutoAccent { get; set; } = true;       // color the bars to match the album art
    public bool ShowDots { get; set; } = true;         // show the mic and camera in-use dots
    public bool FrostedArt { get; set; } = false;      // blur the album art behind the pill
    public bool IosLayout { get; set; } = false;       // blank middle, art left, small square visualizer right
    public double Sensitivity { get; set; } = 2.5;     // how strongly the bars react. shown as a %, where 100% = 2.5x
    public int Bars { get; set; } = 10;                // how many visualizer bars (3 - 10)
    public VisualStyle Visual { get; set; } = VisualStyle.Bars;   // bar row or a 9x9 expanding dot matrix
    public int Ver { get; set; } = 0;                  // settings schema version, for one-time migrations
    public int DragStrength { get; set; } = 1;         // how far the grab-stretch pulls, 1x (normal) to 10x
    public bool ShowNotes { get; set; } = true;        // pop windows notifications into the notch
    public bool ShowCopied { get; set; } = true;       // flash "copied" when you copy text
    public bool HideOnFullscreen { get; set; } = false; // hide while a fullscreen app is focused
    public bool ShowDownloadRing { get; set; } = true;  // spinning ring in the art spot while a download runs
    public bool DownloadRingCompact { get; set; } = true; // just the ring (like album art), no name/size text
    public bool ShowAirdrop { get; set; } = true;       // airdrop-style card when a file lands in a watched folder
    public List<string> AirdropFolders { get; set; } = new();  // folders to watch; empty = screenshots + the notch drop folder
    public bool ShowRecording { get; set; } = true;     // red REC pill with a running clock while the screen is being captured
    public bool WeatherFx { get; set; } = false;        // rain/snow particles on the notch matching real weather (off by default)
    public bool Confetti { get; set; } = false;         // confetti burst on big moments like a finished download/timer (off by default)
    public bool WiggleWhenOpen { get; set; } = false;   // joke: let you grab-wiggle the top bar while the music player is pulled down
    public bool HeySiri { get; set; } = false;          // always-listening "hey siri" voice commands (off by default, uses the mic)
    public bool SiriBorder { get; set; } = false;       // glowing screen-edge frame while siri listens (needs HeySiri on)
    public double SiriBorderSize { get; set; } = 1.0;   // how thick / far the border glow reaches (0.5 - 2.5)
    public bool SiriVoice { get; set; } = true;         // siri speaks its reply out loud
    public bool RgbSiri { get; set; } = false;          // joke: rainbow orb instead of the siri colors
    public bool RgbSiriBorder { get; set; } = false;    // joke: rainbow screen border instead of the siri colors
    public List<string> PinnedApps { get; set; } = new();  // apps you can launch from the notch
    public bool ShowDeviceCard { get; set; } = true;   // pop a 3D card when you plug something in
    public bool ShowDeviceName { get; set; } = false;  // include the specific device name on that card
    public bool HideUnknownDevices { get; set; } = true; // don't pop a card for unclassified (generic cube) devices
    public List<DeviceRule> DeviceRules { get; set; } = new();  // user's own device -> model/name mappings

    static string File_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch", "settings.json");

    public static NotchSettings Load()
    {
        // note: the react % is just a display rebase (100% shown = 2.5x internal), so there's
        // no value migration. everyone keeps the exact bar strength they had; only the label
        // and the fresh-install default (2.5 = "100%") changed.
        try
        {
            if (File.Exists(File_))
                return JsonSerializer.Deserialize<NotchSettings>(File.ReadAllText(File_)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
