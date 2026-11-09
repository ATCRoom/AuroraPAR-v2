using System.Text.RegularExpressions;

namespace ATCRoom.Maps
{
    public static partial class MapImport
    {
        /// <summary>
        /// The ground category suggested by a name (a KML folder, a tag of a .geo, the type of a .pol block): "Twys" →
        /// taxiways, "Rwy_Boundaries" → runway outlines, "TwyCtrls" / "TAXI_CENTER" → centrelines, "Stopbars" → stop bars...
        /// Null: no suggestion. Only a suggestion: the user chooses.
        /// </summary>
        public static string? SuggestGround(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n.Length == 0) return null;
            if (Regex.IsMatch(n, "stop.?(bar|line)")) return MapCategory.GroundStopBar;
            if (Regex.IsMatch(n, "(rwy|runway).*(bound|edge|outline)")) return MapCategory.GroundRunwayOutline;
            if (Regex.IsMatch(n, "ctrl|cent(re|er).?line|taxi.?cent(re|er)")) return MapCategory.GroundCentreline;
            if (Regex.IsMatch(n, "rwy|runway")) return MapCategory.GroundRunway;
            if (n.Contains("apron")) return MapCategory.GroundApron;
            if (Regex.IsMatch(n, "twy|taxi")) return MapCategory.GroundTaxiway;
            if (Regex.IsMatch(n, "concrete|run.?up")) return MapCategory.GroundConcrete;
            if (n.Contains("building")) return MapCategory.GroundBuilding;
            if (Regex.IsMatch(n, "grass|\\bad.?bound|aerodrome.?bound")) return MapCategory.GroundGrass;
            return null;
        }

        // ----- Filled polygons (.pol) ------------------------------------------------------------------------------

        /// <summary>
        /// Filled polygons of the ground layout: a header "STATIC;TYPE;1;TYPE;" (TYPE = GRASS, TAXIWAY, APRON, CONCRETE,
        /// BUILDING, RUNWAY...; the other fields are not used) starts a polygon, then one corner per row "lat;lon;"; a
        /// blank row, a comment or the next header ends it; the comment before a block is its name. One part per TYPE,
        /// with the ground category suggested by it; the polygon is closed by the reader.
        /// </summary>
        public static FileImport ImportPol(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "Filled polygons of the ground (.pol)" };
            Dictionary<string, MapFile> byType = new(StringComparer.OrdinalIgnoreCase);
            List<string> order = [];
            string name = "";
            MapLine? current = null;
            int bad = 0, polygons = 0, outside = 0;

            void Close()
            {
                if (current != null && current.Points.Count >= 3)
                {
                    double[] first = current.Points[0];
                    double[] last = current.Points[^1];
                    if (first[0] != last[0] || first[1] != last[1]) current.Points.Add([first[0], first[1]]);
                }
                current = null;
            }

            foreach (string raw in lines)
            {
                string row = raw.Trim();
                if (row.Length == 0)
                {
                    Close();
                    name = "";
                    continue;
                }
                if (IsComment(row))
                {
                    Close();
                    string text = row.TrimStart('/').Trim().Trim('*').Trim();
                    if (text.Length > 0) name = text;
                    continue;
                }
                string[] f = Fields(row);
                if (f.Length >= 2 && Coordinate(f[0], f[1], out double lat, out double lon))
                {
                    if (current == null)
                    {
                        outside++;
                        continue;
                    }
                    AddPoint(current, lat, lon);
                    continue;
                }
                if (f.Length >= 3 && f[0].Length > 0 && f[1].Length > 0 && f[0].All(char.IsLetter))
                {
                    Close();
                    string type = f[1].ToUpperInvariant();
                    if (!byType.TryGetValue(type, out MapFile? file))
                    {
                        file = new MapFile();
                        byType[type] = file;
                        order.Add(type);
                    }
                    current = new MapLine { Name = name };
                    file.Lines.Add(current);
                    polygons++;
                    continue;
                }
                bad++;
            }
            Close();
            foreach (string type in order)
            {
                byType[type].Lines.RemoveAll(l => l.Points.Count < 3);
                result.Entries.Add(NewEntry(fileName, type, SuggestGround(type), byType[type]));
            }
            result.Notes.Add($"{Plural(polygons, "polygon", "polygons")} read.");
            if (outside > 0) result.Notes.Add($"{Plural(outside, "corner", "corners")} before any header (STATIC;TYPE;...) skipped.");
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            return result;
        }

        // ----- Labels of taxiways and aprons (.txi) ----------------------------------------------------------------

        /// <summary>
        /// The labels of the taxiways and aprons of an airport: "TEXT;ICAO;lat;lon;", the text exactly as written ("M west").
        /// The airport is the one of the file name (libn.txi), else the most frequent in the rows; a row with another
        /// ICAO is kept and named in the notes (it may be a typing error of the file).
        /// </summary>
        public static FileImport ImportTxi(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "Labels of taxiways and aprons (.txi)" };
            MapFile content = new();
            List<(string Text, string Icao)> read = [];
            int bad = 0;
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0 || IsComment(raw)) continue;
                string[] f = Fields(raw);
                if (f.Length < 4 || f[0].Length == 0 || !Coordinate(f[2], f[3], out double lat, out double lon))
                {
                    bad++;
                    continue;
                }
                content.Labels.Add(new MapLabel { Text = f[0], Latitude = lat, Longitude = lon });
                read.Add((f[0], f[1].ToUpperInvariant()));
            }
            Match fromName = Regex.Match(Path.GetFileNameWithoutExtension(fileName), "^([A-Za-z]{4})(?:[^A-Za-z]|$)");
            string? airport = fromName.Success ? fromName.Groups[1].Value.ToUpperInvariant()
                : read.Where(r => Icao.IsMatch(r.Icao)).GroupBy(r => r.Icao).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            result.Airport = airport;
            result.Entries.Add(NewEntry(fileName, "Labels of taxiways and aprons", MapCategory.GroundLabels, content));
            if (airport != null)
            {
                List<string> other = read.Where(r => r.Icao.Length > 0 && r.Icao != airport).Select(r => $"{r.Text} ({r.Icao})").ToList();
                if (other.Count > 0) result.Notes.Add($"Rows with an ICAO other than {airport} (kept, check them): {string.Join(", ", other)}.");
            }
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            return result;
        }
    }
}
