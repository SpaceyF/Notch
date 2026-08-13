using System.Drawing;
using WinForms = System.Windows.Forms;

namespace Notch;

// a scrollable popout that lists every "hey siri" command and the phrases that trigger it.
// opens from the Voice tab in settings.
sealed class CommandsForm : WinForms.Form
{
    static readonly Color Bg = Color.FromArgb(18, 18, 18);
    static readonly Color Fg = Color.FromArgb(235, 235, 235);
    static readonly Color Sub = Color.FromArgb(150, 150, 155);
    static readonly Color Accent = Color.FromArgb(61, 214, 196);

    public CommandsForm()
    {
        Text = "Hey Siri commands";
        FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(430, 540);

        var scroll = new WinForms.Panel { Dock = WinForms.DockStyle.Fill, AutoScroll = true, BackColor = Bg, Padding = new WinForms.Padding(18, 14, 18, 14) };
        Controls.Add(scroll);

        int y = 6;
        scroll.Controls.Add(new WinForms.Label
        {
            Text = "say “Hey Siri,” then any of these", ForeColor = Sub, AutoSize = true,
            Location = new Point(0, y), BackColor = Bg, Font = new Font("Segoe UI", 9.5f),
        });
        y += 34;

        foreach (var (_, label, phrases) in Voice.Commands)
            y = Row(scroll, label, phrases, y);

        // the open-ended ones (free speech after the verb)
        y = Row(scroll, "Search the web", new[] { "search up ___", "search ___", "google ___", "look up ___" }, y);
        y = Row(scroll, "Type text", new[] { "type ___" }, y);
    }

    // one command: the name in white, its trigger phrases underneath in grey
    static int Row(WinForms.Control host, string name, string[] phrases, int y)
    {
        host.Controls.Add(new WinForms.Label
        {
            Text = name, ForeColor = Fg, AutoSize = true, Location = new Point(0, y), BackColor = Bg,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
        });
        var sub = new WinForms.Label
        {
            Text = string.Join("   ·   ", phrases), ForeColor = Sub, AutoSize = true, MaximumSize = new Size(384, 0),
            Location = new Point(0, y + 22), BackColor = Bg, Font = new Font("Segoe UI", 8.75f),
        };
        host.Controls.Add(sub);
        return y + 22 + Math.Max(18, sub.PreferredHeight) + 12;
    }
}
