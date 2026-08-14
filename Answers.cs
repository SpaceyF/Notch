using System.Data;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Notch;

// answers the "smart" hey-siri questions locally, no model and no api tokens: math, unit
// conversion, spelling, date/time, and word definitions via a free keyless dictionary. keeps
// siri feeling smart on any hardware without a bill or a laggy local model.
static class Answers
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public static async Task<string?> Ask(string kind, string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        return kind switch
        {
            "math" => DoMath(text),
            "convert" => DoConvert(text),
            "spell" => Spell(text),
            "define" => await Define(text),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- math + date/time
    static string? DoMath(string s)
    {
        s = s.ToLowerInvariant().Trim();
        if (s.Contains("date") || s.StartsWith("today") || s.Contains("day is") || s.Contains("what day"))
            return DateTime.Now.ToString("dddd, MMMM d");
        if (s.Contains("time"))
            return DateTime.Now.ToString("h:mm tt");

        s = s.Replace("multiplied by", "*").Replace("divided by", "/")
             .Replace(" times ", "*").Replace(" plus ", "+").Replace(" minus ", "-").Replace(" over ", "/");
        s = s.Replace("percent of", "*0.01*").Replace("percent", "*0.01").Replace("%", "*0.01");
        s = s.Replace(" of ", "*");
        s = Regex.Replace(s, @"[^0-9+\-*/().]", "");
        if (!Regex.IsMatch(s, @"\d")) return null;
        try
        {
            var r = new DataTable().Compute(s, null);
            return System.Convert.ToDouble(r).ToString("0.######");
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- unit conversion
    static readonly Dictionary<string, (string cat, double baseVal)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = ("len", 0.001), ["cm"] = ("len", 0.01), ["m"] = ("len", 1), ["meter"] = ("len", 1), ["meters"] = ("len", 1), ["metre"] = ("len", 1), ["metres"] = ("len", 1),
        ["km"] = ("len", 1000), ["kilometer"] = ("len", 1000), ["kilometers"] = ("len", 1000), ["kilometre"] = ("len", 1000), ["kilometres"] = ("len", 1000),
        ["inch"] = ("len", 0.0254), ["inches"] = ("len", 0.0254), ["foot"] = ("len", 0.3048), ["feet"] = ("len", 0.3048), ["ft"] = ("len", 0.3048),
        ["yard"] = ("len", 0.9144), ["yards"] = ("len", 0.9144), ["mile"] = ("len", 1609.34), ["miles"] = ("len", 1609.34),
        ["mg"] = ("wt", 0.001), ["g"] = ("wt", 1), ["gram"] = ("wt", 1), ["grams"] = ("wt", 1), ["kg"] = ("wt", 1000), ["kilo"] = ("wt", 1000), ["kilogram"] = ("wt", 1000), ["kilograms"] = ("wt", 1000),
        ["oz"] = ("wt", 28.3495), ["ounce"] = ("wt", 28.3495), ["ounces"] = ("wt", 28.3495), ["lb"] = ("wt", 453.592), ["lbs"] = ("wt", 453.592), ["pound"] = ("wt", 453.592), ["pounds"] = ("wt", 453.592),
        ["ml"] = ("vol", 0.001), ["milliliter"] = ("vol", 0.001), ["milliliters"] = ("vol", 0.001), ["l"] = ("vol", 1), ["liter"] = ("vol", 1), ["liters"] = ("vol", 1), ["litre"] = ("vol", 1), ["litres"] = ("vol", 1),
        ["cup"] = ("vol", 0.236588), ["cups"] = ("vol", 0.236588), ["pint"] = ("vol", 0.473176), ["pints"] = ("vol", 0.473176), ["quart"] = ("vol", 0.946353), ["quarts"] = ("vol", 0.946353), ["gallon"] = ("vol", 3.78541), ["gallons"] = ("vol", 3.78541),
    };

    static string? DoConvert(string s)
    {
        s = s.ToLowerInvariant();
        var temp = Temp(s); if (temp != null) return temp;

        var num = Regex.Match(s, @"-?\d+(\.\d+)?");
        if (!num.Success) return null;
        double val = double.Parse(num.Value);

        var hits = new List<(int pos, string u)>();
        foreach (Match w in Regex.Matches(s, @"[a-z]+"))
            if (Units.ContainsKey(w.Value)) hits.Add((w.Index, w.Value));
        if (hits.Count < 2) return null;

        // "from" is the unit nearest the number; "to" is a different unit of the same category
        int np = num.Index;
        hits.Sort((a, b) => Math.Abs(a.pos - np).CompareTo(Math.Abs(b.pos - np)));
        var from = hits[0];
        foreach (var h in hits)
            if (!h.u.Equals(from.u, StringComparison.OrdinalIgnoreCase) && Units[h.u].cat == Units[from.u].cat)
                return $"{val * Units[from.u].baseVal / Units[h.u].baseVal:0.###} {h.u}";
        return null;
    }

    static string? Temp(string s)
    {
        if (!Regex.IsMatch(s, @"celsius|fahrenheit|centigrade|degree")) return null;
        var m = Regex.Match(s, @"-?\d+(\.\d+)?");
        if (!m.Success) return null;
        double v = double.Parse(m.Value);
        int cPos = s.IndexOf("celsius", StringComparison.Ordinal);
        if (cPos < 0) cPos = s.IndexOf("centigrade", StringComparison.Ordinal);
        int fPos = s.IndexOf("fahrenheit", StringComparison.Ordinal);
        bool fromC = (cPos >= 0 && fPos >= 0) ? cPos < fPos : cPos >= 0;
        return fromC ? $"{v * 9 / 5 + 32:0.#} degrees Fahrenheit" : $"{(v - 32) * 5 / 9:0.#} degrees Celsius";
    }

    // ---------------------------------------------------------------- spell
    static string Spell(string s)
    {
        var word = Regex.Replace(s, @"[^a-zA-Z]", " ").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return word.Length == 0 ? "" : string.Join("-", word.ToUpperInvariant().ToCharArray());
    }

    // ---------------------------------------------------------------- define (free, keyless)
    static async Task<string?> Define(string s)
    {
        var cleaned = Regex.Replace(s, @"\b(mean|means|meaning|definition|of|the|word)\b", " ", RegexOptions.IgnoreCase);
        var word = Regex.Replace(cleaned, @"[^a-zA-Z]", " ").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (word.Length == 0) return null;
        try
        {
            var json = await Http.GetStringAsync($"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word)}");
            using var doc = JsonDocument.Parse(json);
            var def = doc.RootElement[0].GetProperty("meanings")[0].GetProperty("definitions")[0].GetProperty("definition").GetString();
            return string.IsNullOrWhiteSpace(def) ? null : $"{word}: {def}";
        }
        catch { return null; }
    }
}
