using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: the METAR of an airport from the internet, for lessons without Aurora: the public data
    /// service of the Aviation Weather Center (NOAA, https://aviationweather.gov/data/api/), free and without a key,
    /// worldwide by ICAO code. Asked only when the instructor presses From METAR (never by itself).
    /// </summary>
    internal static class OnlineMetar
    {
        private const string Address = "https://aviationweather.gov/api/data/metar?format=raw&ids=";

        /// <summary>The newest METAR of the airport (its text), or null (no internet, no answer, no METAR for it).</summary>
        public static async Task<string?> FetchAsync(string icao)
        {
            string code = (icao ?? "").Trim().ToUpperInvariant();
            if (code.Length != 4 || !code.All(char.IsAsciiLetterOrDigit)) return null;
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AuroraPARTrainer/" + AppInfo.Version.Replace(' ', '-'));
                string text = await http.GetStringAsync(Address + code);
                // One METAR a line (the newest first); "METAR " / "SPECI " in front on some.
                string? line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                if (line == null) return null;
                if (line.StartsWith("METAR ", StringComparison.Ordinal) || line.StartsWith("SPECI ", StringComparison.Ordinal)) line = line[6..];
                return line.StartsWith(code, StringComparison.Ordinal) ? line : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                return null;
            }
        }

        private static readonly Regex Qnh = new(@"\b(?:Q(\d{4})|A(\d{4}))\b", RegexOptions.CultureInvariant);
        private static readonly Regex Time = new(@"\b(\d{6}Z)\b", RegexOptions.CultureInvariant);

        /// <summary>The QNH of a METAR in hPa (Q1018, or A2992 in inHg), or null.</summary>
        public static int? QnhOf(string? metar)
        {
            if (string.IsNullOrEmpty(metar)) return null;
            Match match = Qnh.Match(metar);
            if (!match.Success) return null;
            int hpa = match.Groups[1].Success
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : (int)Math.Round(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0 * 33.8639);
            return hpa is >= 850 and <= 1090 ? hpa : null;
        }

        /// <summary>The time of a METAR (e.g. 241650Z), or "".</summary>
        public static string TimeOf(string? metar) => metar != null && Time.Match(metar) is { Success: true } m ? m.Groups[1].Value : "";
    }
}
