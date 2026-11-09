using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AuroraPAR
{
    /// <summary>
    /// The VID of a callsign connected to IVAO (the user, 7 Nov: for the log of the flights): IVAO's public list of who is
    /// connected (Whazzup API v2, refreshed by IVAO every 15 s). Asked rarely (at the connection, then at most every few
    /// minutes while not found), never on its own otherwise.
    /// </summary>
    internal static class IvaoDirectory
    {
        private const string Address = "https://api.ivao.aero/v2/tracker/whazzup";

        /// <summary>The VID of the callsign (ATC, observer or pilot), or null (no internet, not in the list, an odd answer).</summary>
        public static async Task<string?> FindVidAsync(string callsign, string version)
        {
            string wanted = (callsign ?? "").Trim();
            if (wanted.Length == 0) return null;
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AuroraPARTrainer/" + (version.Length > 0 ? version.Replace(' ', '-') : "0"));
                // (The whole answer within 15 s, not only its start: a stuck download would stop the IVAO traffic.)
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
                await using Stream body = await http.GetStreamAsync(Address, cts.Token);
                using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: cts.Token);
                if (!json.RootElement.TryGetProperty("clients", out JsonElement clients) || clients.ValueKind != JsonValueKind.Object) return null;
                foreach (JsonProperty group in clients.EnumerateObject())
                {
                    if (group.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (JsonElement client in group.Value.EnumerateArray())
                    {
                        if (client.ValueKind != JsonValueKind.Object || !client.TryGetProperty("callsign", out JsonElement name)
                            || name.ValueKind != JsonValueKind.String || !string.Equals(name.GetString(), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!client.TryGetProperty("userId", out JsonElement id)) return null;
                        return id.ValueKind switch
                        {
                            JsonValueKind.Number => id.GetRawText(),
                            JsonValueKind.String => id.GetString() is string text && text.Trim().Length > 0 ? text.Trim() : null,
                            _ => null
                        };
                    }
                }
                return null;
            }
            catch (Exception)
            {
                // No connection, a timeout, an odd answer: not found (never a crash).
                return null;
            }
        }
    }
}
