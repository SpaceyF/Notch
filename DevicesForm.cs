using System.Drawing;
using WinForms = System.Windows.Forms;

namespace Notch;

// "set up my devices": lists the devices you've plugged in (by usb id) and lets you
// pick what each one is and what to call it. changes apply and save right away.
sealed class DevicesForm : WinForms.Form
{
    readonly Overlay _overlay;

    static readonly Color Bg = Color.FromArgb(18, 18, 18);
    static readonly Color Face = Color.FromArgb(34, 34, 36);
    static readonly Color Line = Color.FromArgb(60, 60, 64);
    static readonly Color Fg = Color.FromArgb(235, 235, 235);
    static readonly Color Sub = Color.FromArgb(150, 150, 155);
    const string Auto = "(auto-detect)";

    public DevicesForm(Overlay overlay)
    {
        _overlay = overlay;
        Text = "Set up my devices";
        FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Bg; ForeColor = Fg;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(380, 400);

        Controls.Add(new WinForms.Label
        {
            Text = "Plug a device in and it shows up here. Pick what it is and\nname it, and Notch will show the right model next time.",
            ForeColor = Sub, AutoSize = true, Location = new Point(16, 12), BackColor = Bg,
        });

        var list = new WinForms.FlowLayoutPanel
        {
            Location = new Point(12, 56), Size = new Size(356, 332),
            FlowDirection = WinForms.FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Bg,
        };
        Controls.Add(list);

        var devices = _overlay.KnownDevices();
        if (devices.Count == 0)
        {
            list.Controls.Add(new WinForms.Label
            {
                Text = "No devices seen yet. Plug something in,\nthen reopen this window.",
                ForeColor = Sub, AutoSize = true, BackColor = Bg, Margin = new WinForms.Padding(4, 8, 0, 0),
            });
            return;
        }
        foreach (var vp in devices) list.Controls.Add(Row(vp));
    }

    WinForms.Panel Row(string vp)
    {
        var rule = _overlay.RuleFor(vp);
        var row = new WinForms.Panel { Size = new Size(332, 60), BackColor = Bg, Margin = new WinForms.Padding(0, 0, 0, 6) };

        row.Controls.Add(new WinForms.Label { Text = vp, ForeColor = Sub, AutoSize = true, Location = new Point(2, 2), BackColor = Bg });

        var combo = new WinForms.ComboBox
        {
            DropDownStyle = WinForms.ComboBoxStyle.DropDownList, Location = new Point(2, 24), Width = 150,
            FlatStyle = WinForms.FlatStyle.Flat, BackColor = Face, ForeColor = Fg,
        };
        combo.Items.Add(Auto);
        combo.Items.AddRange(Overlay.DeviceTypes);
        combo.SelectedItem = rule != null && Array.IndexOf(Overlay.DeviceTypes, rule.Type) >= 0 ? rule.Type : Auto;

        var name = new WinForms.TextBox
        {
            Location = new Point(160, 24), Width = 168, BorderStyle = WinForms.BorderStyle.FixedSingle,
            BackColor = Face, ForeColor = Fg, Text = rule?.Name ?? "",
        };
        SetPlaceholder(name);

        void Apply()
        {
            var t = combo.SelectedItem as string ?? Auto;
            if (t == Auto) _overlay.RemoveDeviceRule(vp);
            else _overlay.SetDeviceRule(vp, name.Text.Trim(), t);
        }
        combo.SelectedIndexChanged += (s, e) => Apply();
        name.Leave += (s, e) => Apply();

        row.Controls.Add(combo);
        row.Controls.Add(name);
        return row;
    }

    static void SetPlaceholder(WinForms.TextBox t)
    {
        t.PlaceholderText = "name (optional)";
    }

    protected override void OnPaintBackground(WinForms.PaintEventArgs e) => base.OnPaintBackground(e);
}
