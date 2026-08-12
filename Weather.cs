using System.Net.Http;
using System.Text.Json;
using Timer = System.Timers.Timer;

namespace Notch;

enum Sky { Unknown, Clear, Clouds, Fog, Rain, Snow, Storm }

// figures out the current weather so the notch can rain/snow to match. everything here is
// keyless: ip-api gives a rough location from your ip, open-meteo gives the conditions.
// needs internet; if it can't reach either it just stays Unknown and no effect shows.
sealed class Weather
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly Timer _poll = new(TimeSpan.FromMinutes(20).TotalMilliseconds) { AutoReset = true };
    double _lat = double.NaN, _lon = double.NaN;
    bool _running;

    public Sky Condition { get; private set; } = Sky.Unknown;
    public double TempC { get; private set; } = double.NaN;

    // fired after each successful refresh (on a background thread)
    public event Action? Changed;

    public Weather() => _poll.Elapsed += async (s, e) => await Refresh();

    public async void Start()
    {
        if (_running) return;
        _running = true;
        _poll.Start();
        await Refresh();
    }

    public void Stop() { _running = false; _poll.Stop(); }

    async Task Refresh()
    {
        try
        {
            if (double.IsNaN(_lat)) await Locate();
            if (double.IsNaN(_lat)) return;

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={_lat:0.####}&longitude={_lon:0.####}&current_weather=true";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current_weather");
            int code = cur.GetProperty("weathercode").GetInt32();
            TempC = cur.GetProperty("temperature").GetDouble();
            Condition = Map(code);
            Changed?.Invoke();
        }
        catch { }   // offline or blocked: leave the last known condition, no effect if never set
    }

    async Task Locate()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("http://ip-api.com/json/?fields=lat,lon"));
            _lat = doc.RootElement.GetProperty("lat").GetDouble();
            _lon = doc.RootElement.GetProperty("lon").GetDouble();
        }
        catch { }
    }

    // WMO weather codes -> the handful of looks we actually draw
    static Sky Map(int c) => c switch
    {
        0 => Sky.Clear,
        1 or 2 or 3 => Sky.Clouds,
        45 or 48 => Sky.Fog,
        >= 51 and <= 67 => Sky.Rain,
        >= 71 and <= 77 => Sky.Snow,
        >= 80 and <= 82 => Sky.Rain,
        85 or 86 => Sky.Snow,
        >= 95 => Sky.Storm,
        _ => Sky.Clouds,
    };
}
