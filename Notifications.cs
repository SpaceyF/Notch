using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using System.Windows.Threading;

namespace Notch;

// watches windows notifications so we can pop them into the notch. windows keeps a
// running list of active toasts; we ask for permission once, then poll the list and
// fire for anything new. needs the app to have notification access granted.
sealed class Notifications
{
    public event Action<string, string, string>? Popped;   // app, title, body

    UserNotificationListener? _listener;
    DispatcherTimer? _poll;
    readonly HashSet<uint> _seen = new();
    bool _first = true;

    public async Task Start()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed) return;

            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _poll.Tick += async (s, e) => await Poll();
            _poll.Start();
        }
        catch { }   // no access / unsupported: notifications just stay off
    }

    async Task Poll()
    {
        if (_listener == null) return;
        try
        {
            var notes = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var current = new HashSet<uint>();
            foreach (var n in notes)
            {
                current.Add(n.Id);
                if (!_seen.Contains(n.Id) && !_first) Emit(n);
            }
            _seen.Clear();
            foreach (var id in current) _seen.Add(id);
            _first = false;   // don't pop everything that was already on screen at launch
        }
        catch { }
    }

    void Emit(UserNotification n)
    {
        string app = "", title = "", body = "";
        try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch { }
        try
        {
            var toast = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (toast != null)
            {
                var text = toast.GetTextElements();
                if (text.Count > 0) title = text[0].Text;
                for (int i = 1; i < text.Count; i++) body += (i > 1 ? "  " : "") + text[i].Text;
            }
        }
        catch { }
        Popped?.Invoke(app, title, body);
    }

    public void Stop() { _poll?.Stop(); }
}
