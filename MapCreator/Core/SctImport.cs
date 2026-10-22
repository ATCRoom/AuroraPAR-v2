using System.Globalization;

namespace ATCRoom.Maps
{
    public static partial class MapImport
    {
        /// <summary>
        /// EuroScope sector file .sct (VATSIM). Sections in brackets, comments after ";", fields separated by spaces, coordinates
        /// "N041.48.00.000" (or without decimals). Read: [VOR] and [NDB] ("ID frequency lat lon"), [FIXES] ("NAME lat lon"),
        /// [AIRPORT] ("ICAO frequency lat lon class") and [GEO] ("[name] lat lon lat lon COLOR": segments that follow each
        /// other are joined into one line; one part for each colour, the coast suggested as boundaries). The rest (runways, SID,
        /// STAR, ARTCC, airways, regions) has no category in the maps and is not read.
        /// </summary>
        public static FileImport ImportSct(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "EuroScope sector file .sct (VATSIM)" };
            MapFile vor = new(), ndb = new(), fixes = new(), airports = new();
            Dictionary<string, MapFile> geo = new(StringComparer.OrdinalIgnoreCase);
            List<string> geoOrder = [];
            HashSet<string> seen = [];
            int duplicates = 0, bad = 0, segments = 0;
            string section = "";
            string group = "";
            MapLine? current = null;
            string currentColour = "";
            double lastLatitude = double.NaN, lastLongitude = double.NaN;

            foreach (string raw in lines)
            {
                string row = raw;
                int comment = row.IndexOf(';');
                if (comment >= 0) row = row[..comment];
                row = row.Trim();
                if (row.Length == 0)
                {
                    current = null;
                    continue;
                }
                if (row[0] == '[')
                {
                    section = row.ToUpperInvariant();
                    current = null;
                    group = "";
                    continue;
                }
                string[] t = row.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                switch (section)
                {
                    case "[VOR]":
                    case "[NDB]":
                    {
                        if (t.Length < 4 || !SctCoordinate(t[2], t[3], out double lat, out double lon)) { bad++; break; }
                        if (!seen.Add($"{section}|{t[0]}|{lat:F4}|{lon:F4}")) { duplicates++; break; }
                        (section == "[VOR]" ? vor : ndb).Points.Add(new MapPoint
                        {
                            Name = t[0], Description = t[1], Latitude = lat, Longitude = lon,
                            Symbol = section == "[VOR]" ? MapSymbol.Vor : MapSymbol.Ndb
                        });
                        break;
                    }
                    case "[FIXES]":
                    {
                        if (t.Length < 3 || !SctCoordinate(t[1], t[2], out double lat, out double lon)) { bad++; break; }
                        if (!seen.Add($"F|{t[0]}|{lat:F4}|{lon:F4}")) { duplicates++; break; }
                        fixes.Points.Add(new MapPoint { Name = t[0], Latitude = lat, Longitude = lon, Symbol = MapSymbol.Point });
                        break;
                    }
                    case "[AIRPORT]":
                    {
                        string code = t[0].ToUpperInvariant();
                        if (t.Length < 4 || !Icao.IsMatch(code) || !SctCoordinate(t[2], t[3], out double lat, out double lon)) { bad++; break; }
                        if (NotAnAirport.IsMatch(code)) break;
                        airports.Points.Add(new MapPoint { Name = code, Latitude = lat, Longitude = lon, Symbol = MapSymbol.Airport });
                        break;
                    }
                    case "[GEO]":
                    {
                        // The name (if any) comes first, then two pairs of coordinates and the colour.
                        int i = 0;
                        while (i + 3 < t.Length && !(IsSctLatitude(t[i]) && IsSctLongitude(t[i + 1]))) i++;
                        if (i + 3 >= t.Length || !SctCoordinate(t[i], t[i + 1], out double lat1, out double lon1) || !SctCoordinate(t[i + 2], t[i + 3], out double lat2, out double lon2))
                        {
                            bad++;
                            current = null;
                            break;
                        }
                        if (i > 0)
                        {
                            group = string.Join(" ", t.Take(i));
                            current = null;
                        }
                        string colour = i + 4 < t.Length ? t[i + 4] : "GEO";
                        if (colour.StartsWith("COLOR_", StringComparison.OrdinalIgnoreCase)) colour = colour[6..];
                        if (!geo.TryGetValue(colour, out MapFile? file))
                        {
                            file = new MapFile();
                            geo[colour] = file;
                            geoOrder.Add(colour);
                        }
                        bool continues = current != null && currentColour == colour && Math.Abs(lastLatitude - lat1) < 1e-7 && Math.Abs(lastLongitude - lon1) < 1e-7;
                        if (!continues)
                        {
                            current = new MapLine { Name = group };
                            current.Points.Add([lat1, lon1]);
                            file.Lines.Add(current);
                            currentColour = colour;
                        }
                        current!.Points.Add([lat2, lon2]);
                        lastLatitude = lat2;
                        lastLongitude = lon2;
                        segments++;
                        break;
                    }
                }
            }

            const string source = "VATSIM sector file";
            result.Entries.Add(NewEntry(fileName, "VOR (sector file)", MapCategory.Navaids, vor, source));
            result.Entries.Add(NewEntry(fileName, "NDB (sector file)", MapCategory.Navaids, ndb, source));
            result.Entries.Add(NewEntry(fileName, "Fixes (sector file)", MapCategory.Fix, fixes, source));
            result.Entries.Add(NewEntry(fileName, "Airports (sector file)", MapCategory.Airports, airports, source));
            foreach (string colour in geoOrder)
            {
                bool coast = colour.Equals("Coastline", StringComparison.OrdinalIgnoreCase) || colour.Equals("COAST", StringComparison.OrdinalIgnoreCase);
                result.Entries.Add(NewEntry(fileName, $"Lines [GEO]: {colour}", coast ? MapCategory.Boundaries : null, geo[colour], source));
            }
            result.Notes.Add($"{Plural(segments, "segment", "segments")} of [GEO] joined into lines, one part for each colour.");
            if (duplicates > 0) result.Notes.Add($"{Plural(duplicates, "repeated point", "repeated points")} (same name and place) read once.");
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            result.Notes.Add("Not read: runways, SID, STAR, ARTCC, airways, regions (no category for them in the maps).");
            return result;
        }

        private static bool IsSctLatitude(string text) => text.Length > 1 && char.ToUpperInvariant(text[0]) is 'N' or 'S' && MapCoordinates.TryParseHemisphere(text, out _);

        private static bool IsSctLongitude(string text) => text.Length > 1 && char.ToUpperInvariant(text[0]) is 'E' or 'W' && MapCoordinates.TryParseHemisphere(text, out _);

        private static bool SctCoordinate(string latitudeText, string longitudeText, out double latitude, out double longitude)
        {
            latitude = longitude = double.NaN;
            if (!MapCoordinates.TryParseHemisphere(latitudeText, out double a) || !MapCoordinates.TryParseHemisphere(longitudeText, out double b)) return false;
            if (!MapCoordinates.IsValid(a, b)) return false;
            latitude = a;
            longitude = b;
            return true;
        }
    }
}
