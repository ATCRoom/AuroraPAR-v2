using System.Globalization;
using System.Text.RegularExpressions;

namespace ATCRoom.Maps
{
    /// <summary>The formats the importers know.</summary>
    public enum MapSourceFormat
    {
        Unknown,
        /// <summary>Aurora (IVAO) map file: lines "T;name;lat;lon;" and labels "L;name;lat;lon;text;size;".</summary>
        Aurora,
        /// <summary>EuroScope (VATSIM) .ese file: [FREETEXT], sector lines, circles and MSAW areas of [AIRSPACE].</summary>
        EuroScope,
        /// <summary>AIP table of minimum altitudes (PDF): "Line joining following points:" with coordinates and arcs.</summary>
        AipTable
    }

    /// <summary>What an importer made of a file: the map, and notes on what it skipped.</summary>
    public sealed class ImportResult
    {
        public MapSourceFormat Format { get; init; }
        public MapFile Map { get; init; } = new();
        public List<string> Notes { get; } = [];
    }

    /// <summary>Which parts of a EuroScope file become the map.</summary>
    public sealed class EseOptions
    {
        public bool SectorLines { get; set; } = true;
        public bool FreeText { get; set; } = true;
        public bool MsawAreas { get; set; } = true;
    }

    /// <summary>A word of a PDF page with its place (points, Y from the top of the page), for the AIP tables.</summary>
    public sealed record PdfWord(int Page, double X, double Y, string Text);

    public static partial class MapImport
    {
        /// <summary>The format of a text file, from its content.</summary>
        public static MapSourceFormat Detect(IReadOnlyList<string> lines)
        {
            int aurora = 0;
            foreach (string raw in lines.Take(5000))
            {
                string line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2) return MapSourceFormat.EuroScope;
                if (line.StartsWith("SECTORLINE:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("MSAW:", StringComparison.OrdinalIgnoreCase)) return MapSourceFormat.EuroScope;
                if ((line.StartsWith("T;", StringComparison.OrdinalIgnoreCase) || line.StartsWith("L;", StringComparison.OrdinalIgnoreCase)) && line.Count(c => c == ';') >= 3) aurora++;
            }
            return aurora > 0 ? MapSourceFormat.Aurora : MapSourceFormat.Unknown;
        }

        // ----- Aurora ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Aurora map file. "T;NAME;lat;lon;": one point of a line; consecutive points with the same NAME are one line
        /// (a blank line, a comment or another NAME starts a new one). "L;NAME;lat;lon;TEXT;SIZE;": a label.
        /// </summary>
        public static ImportResult ImportAurora(IReadOnlyList<string> lines)
        {
            ImportResult result = new() { Format = MapSourceFormat.Aurora };
            MapFile map = result.Map;
            MapLine? current = null;
            int skipped = 0;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//"))
                {
                    current = null;
                    continue;
                }
                string[] f = line.Split(';');
                if (f.Length >= 4 && f[0].Equals("T", StringComparison.OrdinalIgnoreCase)
                    && MapCoordinates.TryParseHemisphere(f[2], out double lat) && MapCoordinates.TryParseHemisphere(f[3], out double lon))
                {
                    string name = f[1].Trim();
                    if (current == null || current.Name != name)
                    {
                        current = new MapLine { Name = name };
                        map.Lines.Add(current);
                    }
                    current.Points.Add([lat, lon]);
                }
                else if (f.Length >= 4 && f[0].Equals("L", StringComparison.OrdinalIgnoreCase)
                    && MapCoordinates.TryParseHemisphere(f[2], out double labelLat) && MapCoordinates.TryParseHemisphere(f[3], out double labelLon))
                {
                    string text = f.Length > 4 && f[4].Trim().Length > 0 ? f[4].Trim() : f[1].Trim();
                    double size = f.Length > 5 && double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : MapLabel.DefaultSize;
                    map.Labels.Add(new MapLabel { Text = text, Latitude = labelLat, Longitude = labelLon, Size = size });
                    current = null;
                }
                else
                {
                    skipped++;
                    current = null;
                }
            }
            if (skipped > 0) result.Notes.Add($"{skipped} line{(skipped == 1 ? "" : "s")} not understood, skipped.");
            map.Normalize();
            return result;
        }

        // ----- EuroScope .ese --------------------------------------------------------------------------------------

        /// <summary>
        /// EuroScope .ese file (see https://www.euroscope.hu/wp/ese-files-description/). Used for the map:
        /// [FREETEXT] "lat:lon:[group:]text" → labels; [AIRSPACE] "SECTORLINE:name" with its "COORD:lat:lon" → lines,
        /// "CIRCLE_SECTORLINE:name:lat:lon:radius" → circles, "MSAW:name:altitude" with its COORD → closed areas with
        /// their altitude as a label. OFFSET lines move the coordinates that follow. ";" starts a comment.
        /// </summary>
        public static ImportResult ImportEuroScope(IReadOnlyList<string> lines, EseOptions? options = null)
        {
            options ??= new EseOptions();
            ImportResult result = new() { Format = MapSourceFormat.EuroScope };
            MapFile map = result.Map;
            string section = "";
            double offsetLat = 0, offsetLon = 0;
            MapLine? current = null;
            string? msawAltitude = null;
            int circlesByName = 0, badCoordinates = 0, sectorLines = 0, msaw = 0, texts = 0;

            void FinishMsaw()
            {
                if (current != null && msawAltitude != null && current.Points.Count >= 3)
                {
                    double[] first = current.Points[0];
                    double[] last = current.Points[^1];
                    if (first[0] != last[0] || first[1] != last[1]) current.Points.Add([first[0], first[1]]);
                    double[] centre = MapCoordinates.Centroid(current.Points);
                    map.Labels.Add(new MapLabel { Text = MapCoordinates.ShortAltitude(msawAltitude), Latitude = centre[0], Longitude = centre[1] });
                }
                msawAltitude = null;
            }
            void Close()
            {
                FinishMsaw();
                current = null;
            }
            bool Coordinate(string latText, string lonText, out double lat, out double lon)
            {
                lat = lon = double.NaN;
                if (!MapCoordinates.TryParseHemisphere(latText, out double a) || !MapCoordinates.TryParseHemisphere(lonText, out double b))
                {
                    badCoordinates++;
                    return false;
                }
                lat = a + offsetLat;
                lon = b + offsetLon;
                return true;
            }

            foreach (string raw in lines)
            {
                string line = raw;
                int comment = line.IndexOf(';');
                if (comment >= 0) line = line[..comment];
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    Close();
                    section = line.ToUpperInvariant();
                    continue;
                }
                string[] f = line.Split(':');
                string key = f[0].Trim().ToUpperInvariant();
                if (key == "OFFSET")
                {
                    if (f.Length == 3
                        && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dLat)
                        && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double dLon))
                    {
                        offsetLat = dLat;
                        offsetLon = dLon;
                    }
                    else if (f.Length == 5
                        && MapCoordinates.TryParseHemisphere(f[1], out double fromLat) && MapCoordinates.TryParseHemisphere(f[2], out double fromLon)
                        && MapCoordinates.TryParseHemisphere(f[3], out double toLat) && MapCoordinates.TryParseHemisphere(f[4], out double toLon))
                    {
                        offsetLat = toLat - fromLat;
                        offsetLon = toLon - fromLon;
                    }
                    continue;
                }
                if (section == "[FREETEXT]")
                {
                    if (!options.FreeText || f.Length < 3) continue;
                    if (!Coordinate(f[0], f[1], out double lat, out double lon)) continue;
                    // With a group the text is the fourth field (texts may contain ":" too).
                    string text = f.Length >= 4 ? string.Join(":", f.Skip(3)) : f[2];
                    if (text.Trim().Length == 0) continue;
                    map.Labels.Add(new MapLabel { Text = text.Trim(), Latitude = lat, Longitude = lon });
                    texts++;
                    continue;
                }
                if (section != "[AIRSPACE]") continue;
                switch (key)
                {
                    case "SECTORLINE":
                        Close();
                        if (!options.SectorLines) break;
                        current = new MapLine { Name = f.Length > 1 ? f[1].Trim() : "" };
                        map.Lines.Add(current);
                        sectorLines++;
                        break;
                    case "MSAW":
                        Close();
                        if (!options.MsawAreas) break;
                        current = new MapLine { Name = f.Length > 1 ? f[1].Trim() : "" };
                        map.Lines.Add(current);
                        msawAltitude = f.Length > 2 ? f[2].Trim() : "";
                        msaw++;
                        break;
                    case "COORD":
                        if (current != null && f.Length >= 3 && Coordinate(f[1], f[2], out double lat, out double lon)) current.Points.Add([lat, lon]);
                        break;
                    case "CIRCLE_SECTORLINE":
                        Close();
                        if (!options.SectorLines) break;
                        if (f.Length >= 5 && Coordinate(f[2], f[3], out double cLat, out double cLon)
                            && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double radius) && radius > 0)
                        {
                            map.Lines.Add(new MapLine { Name = f[1].Trim(), Points = MapCoordinates.Circle(cLat, cLon, radius) });
                            sectorLines++;
                        }
                        else
                        {
                            // The form with the name of a point needs the .sct file, where the point is.
                            circlesByName++;
                        }
                        break;
                    // These lines do not end the line they belong to.
                    case "DISPLAY":
                    case "DISPLAY_SECTORLINE":
                        break;
                    default:
                        // Sectors, owners, coordination points, radar holes...: not for the map.
                        Close();
                        break;
                }
            }
            Close();
            if (options.SectorLines) result.Notes.Add($"{sectorLines} sector line{(sectorLines == 1 ? "" : "s")} and circles.");
            if (options.MsawAreas) result.Notes.Add($"{msaw} MSAW area{(msaw == 1 ? "" : "s")} (minimum altitudes), with their altitude as a label.");
            if (options.FreeText) result.Notes.Add($"{texts} free text{(texts == 1 ? "" : "s")}.");
            if (circlesByName > 0) result.Notes.Add($"{circlesByName} circle{(circlesByName == 1 ? "" : "s")} around a named point skipped (the point is in the .sct file).");
            if (badCoordinates > 0) result.Notes.Add($"{badCoordinates} coordinate{(badCoordinates == 1 ? "" : "s")} not understood, skipped.");
            map.Normalize();
            return result;
        }

        // ----- AIP table -------------------------------------------------------------------------------------------

        [GeneratedRegex("^(\\d{3,5}\\s*FT(\\s+[A-Z]+)?|FL\\s*\\d{2,3}|TRL|TA)$", RegexOptions.IgnoreCase)]
        private static partial Regex AltitudeText();

        [GeneratedRegex("radius\\s+([\\d.,]+)\\s*(NM|KM)", RegexOptions.IgnoreCase)]
        private static partial Regex ArcRadius();

        /// <summary>
        /// AIP table of minimum altitudes (e.g. "ATC surveillance minimum altitude chart", ENAV): each area starts with
        /// "Line joining following points:" followed by its coordinates (44°16'56" N 008°39'51" E), with arcs written
        /// "then arc of circle in clockwise direction radius 17 NM centred on: (centre) till point (end)". The altitude
        /// of the area (2000 FT, FL90, TRL) is in the left column beside it. Words of the pages with their place.
        /// </summary>
        public static ImportResult ImportAipTable(IReadOnlyList<PdfWord> words)
        {
            ImportResult result = new() { Format = MapSourceFormat.AipTable };
            MapFile map = result.Map;
            // Text lines of each page: words at the same height, left to right.
            List<(int Page, double Y, List<PdfWord> Words)> rows = [];
            foreach (IGrouping<int, PdfWord> page in words.GroupBy(w => w.Page).OrderBy(g => g.Key))
            {
                foreach (PdfWord word in page.OrderBy(w => w.Y).ThenBy(w => w.X))
                {
                    var row = rows.Count > 0 && rows[^1].Page == page.Key && Math.Abs(rows[^1].Y - word.Y) <= 3 ? rows[^1] : default;
                    if (row.Words == null)
                    {
                        rows.Add((page.Key, word.Y, [word]));
                    }
                    else
                    {
                        row.Words.Add(word);
                    }
                }
            }
            // The right column starts where "Line joining" starts (on each page, else as on the page before).
            Dictionary<int, double> split = [];
            foreach (var row in rows)
            {
                List<PdfWord> ordered = row.Words.OrderBy(w => w.X).ToList();
                for (int i = 0; i + 1 < ordered.Count; i++)
                {
                    if (ordered[i].Text.Equals("Line", StringComparison.OrdinalIgnoreCase) && ordered[i + 1].Text.StartsWith("joining", StringComparison.OrdinalIgnoreCase))
                    {
                        double x = ordered[i].X - 5;
                        split[row.Page] = split.TryGetValue(row.Page, out double old) ? Math.Min(old, x) : x;
                    }
                }
            }
            if (split.Count == 0)
            {
                result.Notes.Add("No \"Line joining following points\" found: this PDF is not an AIP table of areas (the charts themselves are drawings and cannot be converted).");
                return result;
            }

            List<(MapLine Line, string? Altitude)> areas = [];
            List<(int Page, double Y, int Area)> rightRows = [];
            List<(int Page, double Y, string Text)> leftTexts = [];
            double lastSplit = split.OrderBy(s => s.Key).First().Value;
            (double[] Centre, double Radius, bool Clockwise, bool WaitingEnd)? arc = null;
            bool arcNeedsCentre = false;
            int arcs = 0;
            foreach (var row in rows)
            {
                if (split.TryGetValue(row.Page, out double s)) lastSplit = s;
                List<PdfWord> ordered = row.Words.OrderBy(w => w.X).ToList();
                string left = string.Join(" ", ordered.Where(w => w.X < lastSplit).Select(w => w.Text)).Trim();
                string right = string.Join(" ", ordered.Where(w => w.X >= lastSplit).Select(w => w.Text)).Trim();
                if (left.Length > 0 && AltitudeText().IsMatch(left)) leftTexts.Add((row.Page, row.Y, left));
                if (right.Length == 0) continue;
                if (right.Contains("Line joining", StringComparison.OrdinalIgnoreCase))
                {
                    areas.Add((new MapLine(), null));
                    arc = null;
                    arcNeedsCentre = false;
                    rightRows.Add((row.Page, row.Y, areas.Count - 1));
                    continue;
                }
                if (areas.Count == 0) continue;
                MapLine area = areas[^1].Line;
                rightRows.Add((row.Page, row.Y, areas.Count - 1));
                if (right.Contains("arc", StringComparison.OrdinalIgnoreCase) && right.Contains("clockwise", StringComparison.OrdinalIgnoreCase))
                {
                    Match r = ArcRadius().Match(right);
                    if (r.Success && double.TryParse(r.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double radius))
                    {
                        if (r.Groups[2].Value.Equals("KM", StringComparison.OrdinalIgnoreCase)) radius /= 1.852;
                        bool counter = Regex.IsMatch(right, "anti[- ]?clockwise|counter[- ]?clockwise", RegexOptions.IgnoreCase);
                        arc = (new[] { double.NaN, double.NaN }, radius, !counter, false);
                        arcNeedsCentre = true;
                    }
                }
                // "till point" may be alone, after the centre or before the end point, on the same line.
                int till = right.IndexOf("till point", StringComparison.OrdinalIgnoreCase);
                foreach ((int index, double lat, double lon) in MapCoordinates.FindAllAip(right))
                {
                    if (arc != null && arcNeedsCentre)
                    {
                        arc = (new[] { lat, lon }, arc.Value.Radius, arc.Value.Clockwise, false);
                        arcNeedsCentre = false;
                        continue;
                    }
                    if (arc != null && (arc.Value.WaitingEnd || (till >= 0 && index > till)))
                    {
                        if (area.Points.Count > 0)
                        {
                            area.Points.AddRange(MapCoordinates.Arc(arc.Value.Centre, arc.Value.Radius, area.Points[^1], [lat, lon], arc.Value.Clockwise));
                            arcs++;
                        }
                        arc = null;
                    }
                    area.Points.Add([lat, lon]);
                }
                if (till >= 0 && arc != null && !arcNeedsCentre) arc = (arc.Value.Centre, arc.Value.Radius, arc.Value.Clockwise, true);
            }
            // Each altitude of the left column belongs to the area of the right-column line nearest to it.
            foreach ((int page, double y, string text) in leftTexts)
            {
                var near = rightRows.Where(r => r.Page == page).OrderBy(r => Math.Abs(r.Y - y)).FirstOrDefault();
                if (near.Page != page || near.Area < 0 || near.Area >= areas.Count) continue;
                if (areas[near.Area].Altitude == null) areas[near.Area] = (areas[near.Area].Line, text);
            }
            int unnamed = 0, open = 0;
            foreach ((MapLine line, string? altitude) in areas)
            {
                if (line.Points.Count < 3) continue;
                line.Name = altitude ?? "";
                if (altitude == null) unnamed++;
                double[] first = line.Points[0];
                double[] last = line.Points[^1];
                if (first[0] != last[0] || first[1] != last[1]) open++;
                map.Lines.Add(line);
                if (altitude != null)
                {
                    double[] centre = MapCoordinates.Centroid(line.Points);
                    map.Labels.Add(new MapLabel { Text = MapCoordinates.ShortAltitude(altitude), Latitude = centre[0], Longitude = centre[1] });
                }
            }
            result.Notes.Add($"{map.Lines.Count} area{(map.Lines.Count == 1 ? "" : "s")} with their minimum altitude as a label{(arcs > 0 ? $"; {arcs} arc{(arcs == 1 ? "" : "s")} of circle" : "")}.");
            if (unnamed > 0) result.Notes.Add($"{unnamed} area{(unnamed == 1 ? "" : "s")} without an altitude found beside it.");
            if (open > 0) result.Notes.Add($"{open} area{(open == 1 ? " is" : "s are")} not closed (last point different from the first): check them.");
            result.Notes.Add("The labels are at the centre of each area: for odd shapes they may need moving.");
            map.Normalize();
            return result;
        }
    }
}
