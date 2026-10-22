using System.Globalization;
using System.Text.RegularExpressions;

namespace ATCRoom.Maps
{
    /// <summary>
    /// The lists of the Italian AIP (PDF): visual reporting points (ENR 6.3.1), significant points (ENR 4.4) and radio
    /// navigation aids (ENR 4.1.1 and 4.1.2). The first two are read from the lines of text, the third from the place of
    /// the words on the page (its columns are cells that are written over several lines).
    /// </summary>
    public static partial class MapImport
    {
        private static readonly Regex AipVrpTitle = new(@"ENR\s*6\.3\.1", RegexOptions.Compiled);
        private static readonly Regex AipFixTitle = new(@"ENR\s*4\.4(?![\d.])", RegexOptions.Compiled);
        private static readonly Regex AipNavTitle = new(@"ENR\s*4\.1\.([12])", RegexOptions.Compiled);
        private static readonly Regex AipCode = new("^[A-Z0-9]{2,8}$", RegexOptions.Compiled);
        private static readonly Regex AipLatWord = new(@"^(\d{2})°(\d{2})['’′](\d{2}(?:[.,]\d+)?)(?:''|[""”″’]{1,2})([NS])$", RegexOptions.Compiled);
        private static readonly Regex AipLonWord = new(@"^(\d{3})°(\d{2})['’′](\d{2}(?:[.,]\d+)?)(?:''|[""”″’]{1,2})([EW])$", RegexOptions.Compiled);
        private static readonly Regex AipIdWord = new("^[A-Z]{2,3}$", RegexOptions.Compiled);
        private static readonly Regex AipArcText = new(@"arc\s+of\s+circle\s+in\s+(anti[- ]?clockwise|counter[- ]?clockwise|clockwise)\s+direction\s+radius\s+([\d.,]+)\s*(NM|KM)\s+cent(?:red|ered)\s+on", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AipCircleText = new(@"Circular\s+area\s+cent(?:red|ered)\s+on", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AipWithinText = new(@"within\s+a\s+([\d.,]+)\s*NM\s+radius", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AipOriginText = new(@"(till|to)\s+point\s+of\s+origin", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AipTillText = new(@"till\s+point(?!\s+of\s+origin)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AipZoneText = new(@"Zona\s*/\s*Zone\s*['’]\s*(\d+)\s*['’]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AipDesignator = new(@"^[A-Z][A-Za-z0-9'’./()\- ]*? (CTR|ATZ)$", RegexOptions.Compiled);
        private static readonly Regex AipAtzHeading = new(@"^ENR 2\.1\.3\.\d+\s+([^\d.\s].*)$", RegexOptions.Compiled);
        private static readonly Regex AipTypeWord = new("^(D?VOR|DME|TACAN|VORTAC|NDB|L|ILS|LOC|TLS)(/(D?VOR|DME|TACAN|VORTAC|NDB|L|ILS|LOC|TLS))*$", RegexOptions.Compiled);

        /// <summary>Which list a PDF is, from the title on its first page: "vrp", "fix", "vor1", "vor2"; null: not one of these.</summary>
        internal static string? AipKind(IReadOnlyList<PdfWord> words)
        {
            if (words.Count == 0) return null;
            int first = words.Min(w => w.Page);
            string head = string.Join(" ", words.Where(w => w.Page == first).OrderBy(w => w.Y).ThenBy(w => w.X).Take(120).Select(w => w.Text));
            // The title of the CTR and ATZ lists is the page header (the first words): the ATZ list mentions ENR 2.1.2 in its text.
            string top = string.Join(" ", words.Where(w => w.Page == first).OrderBy(w => w.Y).ThenBy(w => w.X).Take(8).Select(w => w.Text));
            if (Regex.IsMatch(top, @"ENR\s*2\.1\.3")) return "atz";
            if (Regex.IsMatch(top, @"ENR\s*2\.1\.2")) return "ctr";
            if (AipVrpTitle.IsMatch(head)) return "vrp";
            if (AipFixTitle.IsMatch(head)) return "fix";
            // The radio aids: the title anywhere on the first page, else the columns of the table (ID, frequency, coordinates).
            string page = string.Join(" ", words.Where(w => w.Page == first).OrderBy(w => w.Y).ThenBy(w => w.X).Select(w => w.Text));
            Match nav = AipNavTitle.Match(page);
            if (nav.Success) return "vor" + nav.Groups[1].Value;
            if (Regex.IsMatch(page, @"\bID\b") && Regex.IsMatch(page, @"\bFREQ\b") && Regex.IsMatch(page, @"Coordinate|coordinates", RegexOptions.IgnoreCase)) return "vor0";
            return null;
        }

        /// <summary>The words of a PDF as lines of text (a wide gap between two words is written as three spaces).</summary>
        internal static List<string> AipLines(IReadOnlyList<PdfWord> words)
        {
            List<string> lines = [];
            foreach (IGrouping<int, PdfWord> page in words.GroupBy(w => w.Page).OrderBy(g => g.Key))
            {
                List<List<PdfWord>> rows = [];
                foreach (PdfWord word in page.OrderBy(w => w.Y).ThenBy(w => w.X))
                {
                    if (rows.Count > 0 && Math.Abs(rows[^1][0].Y - word.Y) <= 2.5) rows[^1].Add(word);
                    else rows.Add([word]);
                }
                foreach (List<PdfWord> row in rows)
                {
                    System.Text.StringBuilder text = new();
                    PdfWord? previous = null;
                    foreach (PdfWord word in row.OrderBy(w => w.X))
                    {
                        if (previous != null)
                        {
                            double gap = previous.Right > 0 ? word.X - previous.Right : 4;
                            text.Append(gap > 12 ? "   " : " ");
                        }
                        text.Append(word.Text);
                        previous = word;
                    }
                    lines.Add(text.ToString());
                }
            }
            return lines;
        }

        /// <summary>The first AIP coordinate pair of a line and the text before it; false when the line has none.</summary>
        private static bool AipRow(string line, out string before, out double latitude, out double longitude)
        {
            before = "";
            latitude = longitude = double.NaN;
            // The seconds are written with two apostrophes (45'12''N): one quotation mark for the coordinate reader.
            string text = line.Replace("''", "\"").Replace("’’", "\"");
            List<(int Index, double Latitude, double Longitude)> found = MapCoordinates.FindAllAip(text);
            if (found.Count == 0) return false;
            before = text[..found[0].Index];
            latitude = found[0].Latitude;
            longitude = found[0].Longitude;
            return true;
        }

        /// <summary>
        /// ENR 6.3.1: "name   CODE   lat   long   radio aid" in the first list, "CODE   name   lat   long" in the second one (the
        /// header of each page says which). The label is the code, the name is kept as the description; a point listed in both
        /// lists is kept once.
        /// </summary>
        public static FileImport ImportAipVrp(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "AIP visual reporting points (ENR 6.3.1)" };
            MapFile content = new();
            HashSet<string> seen = new(StringComparer.Ordinal);
            bool codeFirst = false;
            int bad = 0;
            foreach (string line in lines)
            {
                int nameAt = line.IndexOf("Geographical Name", StringComparison.Ordinal);
                int codeAt = line.IndexOf("Code", StringComparison.Ordinal);
                if (nameAt >= 0 && codeAt >= 0)
                {
                    codeFirst = codeAt < nameAt;
                    continue;
                }
                if (!AipRow(line, out string before, out double lat, out double lon)) continue;
                string[] parts = Regex.Split(before.Trim(), @"\s{2,}").Where(p => p.Length > 0).ToArray();
                if (parts.Length != 2)
                {
                    bad++;
                    continue;
                }
                string code = (codeFirst ? parts[0] : parts[1]).Trim();
                string name = (codeFirst ? parts[1] : parts[0]).Trim();
                if (!AipCode.IsMatch(code))
                {
                    bad++;
                    continue;
                }
                if (!seen.Add(code)) continue;
                content.Points.Add(new MapPoint { Name = code, Description = name, Latitude = lat, Longitude = lon, Symbol = MapSymbol.Vrp });
            }
            if (content.Points.Count > 0) result.Entries.Add(NewEntry(fileName, "VRP (AIP ENR 6.3.1)", MapCategory.Vrp, content, "AIP Italia ENR 6.3.1"));
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            if (content.Points.Count == 0) result.Notes.Add("No visual reporting points found in this file.");
            return result;
        }

        /// <summary>ENR 4.4: "CODE   lat   long   routes   procedures   FRA": a point per code (the label), as a FIX.</summary>
        public static FileImport ImportAipFixes(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "AIP significant points (ENR 4.4)" };
            MapFile content = new();
            HashSet<string> seen = new(StringComparer.Ordinal);
            int bad = 0;
            foreach (string line in lines)
            {
                if (!AipRow(line, out string before, out double lat, out double lon)) continue;
                string code = before.Trim();
                if (!AipCode.IsMatch(code))
                {
                    bad++;
                    continue;
                }
                if (!seen.Add(code)) continue;
                content.Points.Add(new MapPoint { Name = code, Latitude = lat, Longitude = lon, Symbol = MapSymbol.Point });
            }
            if (content.Points.Count > 0) result.Entries.Add(NewEntry(fileName, "Significant points (AIP ENR 4.4)", MapCategory.Fix, content, "AIP Italia ENR 4.4"));
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            if (content.Points.Count == 0) result.Notes.Add("No significant points found in this file.");
            return result;
        }

        /// <summary>Words that touch each other (a coordinate that the PDF reader split) are put together again.</summary>
        private static List<PdfWord> AipMerge(IEnumerable<PdfWord> page)
        {
            List<PdfWord> merged = [];
            foreach (PdfWord word in page.OrderBy(w => w.Y).ThenBy(w => w.X))
            {
                PdfWord? last = merged.Count > 0 ? merged[^1] : null;
                if (last != null && last.Right > 0 && Math.Abs(last.Y - word.Y) <= 1.5 && word.X - last.Right < 0.8 && word.X >= last.X)
                {
                    merged[^1] = last with { Text = last.Text + word.Text, Right = word.Right };
                }
                else merged.Add(word);
            }
            return merged;
        }

        private static double AipDms(Match m)
        {
            double value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 60
                + double.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture) / 3600;
            return m.Groups[4].Value is "S" or "W" ? -value : value;
        }

        /// <summary>
        /// ENR 4.1.1 / 4.1.2: a table with a cell for each column: name and type of the aid (first column, over several
        /// lines), ID (second column), coordinates (the latitude over the longitude; a second pair is the DME or TACAN
        /// antenna). Read by the place of the words: every ID (the column under the header "ID") is an aid, the lines of the
        /// first column and the coordinate pair nearest to it belong to it. The label is the ID; the name and type are kept as
        /// the description. VOR (VORTAC, DVOR) and TACAN as such, NDB and L as NDB; a DME alone is skipped.
        /// </summary>
        public static FileImport ImportAipNavaids(string fileName, IReadOnlyList<PdfWord> words, string table)
        {
            FileImport result = new() { FileName = fileName, FormatName = $"AIP radio navigation aids ({table})" };
            MapFile content = new();
            int skipped = 0;
            double? idColumn = null;
            foreach (IGrouping<int, PdfWord> pageWords in words.GroupBy(w => w.Page).OrderBy(g => g.Key))
            {
                List<PdfWord> ws = AipMerge(pageWords);
                PdfWord? header = ws.FirstOrDefault(w => w.Text == "ID" && w.Y < 260);
                if (header != null) idColumn = header.X;
                if (idColumn == null) continue;
                double column = idColumn.Value;
                // The body of the table starts under the row of the column numbers (1 ... 9).
                double bodyTop = ws.Where(w => w.Text == "9" && w.Y < 300).Select(w => w.Y).DefaultIfEmpty(header != null ? header.Y + 60 : 0).Max() + 4;
                List<PdfWord> ids = ws.Where(w => w.Y > bodyTop && AipIdWord.IsMatch(w.Text) && Math.Abs(w.X - column) < 14).ToList();
                if (ids.Count == 0) continue;

                // Coordinates: a latitude with the longitude just under it.
                List<(double Y, double Latitude, double Longitude)> pairs = [];
                foreach (PdfWord a in ws)
                {
                    Match latMatch = AipLatWord.Match(a.Text);
                    if (!latMatch.Success) continue;
                    PdfWord? lonWord = ws.Where(b => AipLonWord.IsMatch(b.Text) && Math.Abs(b.X - a.X) < 8 && b.Y - a.Y > 0 && b.Y - a.Y <= 16)
                        .OrderBy(b => b.Y).FirstOrDefault();
                    if (lonWord == null) continue;
                    double lat = AipDms(latMatch), lon = AipDms(AipLonWord.Match(lonWord.Text));
                    if (MapCoordinates.IsValid(lat, lon)) pairs.Add((a.Y, lat, lon));
                }
                Dictionary<PdfWord, List<(double Y, double Latitude, double Longitude)>> pairsOf = ids.ToDictionary(w => w, _ => new List<(double, double, double)>());
                foreach ((double Y, double Latitude, double Longitude) pair in pairs)
                {
                    PdfWord near = ids.OrderBy(w => Math.Abs(w.Y - pair.Y)).First();
                    if (Math.Abs(near.Y - pair.Y) <= 30) pairsOf[near].Add(pair);
                }

                // Lines of the first column, each one to the nearest ID.
                List<PdfWord> first = ws.Where(w => w.Y > bodyTop && (w.Right > 0 ? w.Right : w.X) < column - 6).OrderBy(w => w.Y).ThenBy(w => w.X).ToList();
                List<(double Y, string Text)> textLines = [];
                List<PdfWord> row = [];
                void Flush()
                {
                    if (row.Count == 0) return;
                    textLines.Add((row.Average(w => w.Y), string.Join(" ", row.OrderBy(w => w.X).Select(w => w.Text))));
                    row = [];
                }
                foreach (PdfWord w in first)
                {
                    if (row.Count > 0 && Math.Abs(row[0].Y - w.Y) > 2.5) Flush();
                    row.Add(w);
                }
                Flush();
                Dictionary<PdfWord, List<(double Y, string Text)>> linesOf = ids.ToDictionary(w => w, _ => new List<(double, string)>());
                foreach ((double Y, string Text) line in textLines)
                {
                    PdfWord near = ids.OrderBy(w => Math.Abs(w.Y - line.Y)).First();
                    if (Math.Abs(near.Y - line.Y) <= 30) linesOf[near].Add(line);
                }

                foreach (PdfWord id in ids)
                {
                    List<(double Y, double Latitude, double Longitude)> mine = pairsOf[id].OrderBy(p => p.Y).ToList();
                    if (mine.Count == 0) continue;
                    List<string> ls = linesOf[id].OrderBy(l => l.Y).Select(l => l.Text).Where(t => !t.StartsWith('(')).ToList();
                    List<string> typeLines = ls.Where(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(x => AipTypeWord.IsMatch(x))).ToList();
                    string type = string.Join(" ", typeLines);
                    string name = string.Join(" ", ls.Where(t => !typeLines.Contains(t)));
                    MapSymbol? symbol = null;
                    if (type.Contains("VORTAC", StringComparison.Ordinal)) symbol = MapSymbol.Vortac;
                    else if (type.Contains("VOR", StringComparison.Ordinal)) symbol = MapSymbol.Vor;
                    else if (type.Contains("TACAN", StringComparison.Ordinal)) symbol = MapSymbol.Tacan;
                    else if (type.Contains("NDB", StringComparison.Ordinal) || type.Split(' ').Contains("L")) symbol = MapSymbol.Ndb;
                    if (symbol == null)
                    {
                        skipped++;
                        continue;
                    }
                    content.Points.Add(new MapPoint
                    {
                        Name = id.Text,
                        Description = (name + " " + type).Trim(),
                        Latitude = mine[0].Latitude,
                        Longitude = mine[0].Longitude,
                        Symbol = symbol.Value
                    });
                }
            }
            if (content.Points.Count > 0) result.Entries.Add(NewEntry(fileName, $"Radio navigation aids (AIP {table})", MapCategory.Navaids, content, "AIP Italia " + table));
            if (skipped > 0) result.Notes.Add($"{Plural(skipped, "aid", "aids")} skipped (DME alone or type not understood).");
            if (content.Points.Count == 0) result.Notes.Add("No radio navigation aids found in this file.");
            return result;
        }

        private sealed record AipToken(int Index, char Kind, double A = 0, double B = 0, bool Flag = false, string Text = "");

        /// <summary>
        /// The polygons written in the text of the lateral limits of an airspace: points "lat lon;", arcs "then arc of circle in
        /// clockwise direction radius 12 NM centred on (centre) till point (end)", "to point of origin." (or "till point of origin")
        /// to close, "Circular area centered on (centre) within a 5 NM radius", and the zones of a CTR "Zona/Zone '1'". After a
        /// polygon is closed nothing is read until the next zone: the rest of the page has coordinates that are not the area.
        /// </summary>
        internal static List<(string? Zone, List<double[]> Points)> AipPolygons(string stream, out int reversed)
        {
            List<AipToken> tokens = [];
            foreach ((int index, double lat, double lon) in MapCoordinates.FindAllAip(stream)) tokens.Add(new AipToken(index, 'C', lat, lon));
            foreach (Match m in AipArcText.Matches(stream))
            {
                double radius = double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                if (m.Groups[3].Value.Equals("KM", StringComparison.OrdinalIgnoreCase)) radius /= 1.852;
                tokens.Add(new AipToken(m.Index, 'A', radius, 0, m.Groups[1].Value.StartsWith("clock", StringComparison.OrdinalIgnoreCase)));
            }
            foreach (Match m in AipCircleText.Matches(stream)) tokens.Add(new AipToken(m.Index, 'O'));
            foreach (Match m in AipWithinText.Matches(stream)) tokens.Add(new AipToken(m.Index, 'W', double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture)));
            foreach (Match m in AipOriginText.Matches(stream)) tokens.Add(new AipToken(m.Index, 'G', 0, 0, m.Groups[1].Value.Equals("till", StringComparison.OrdinalIgnoreCase)));
            foreach (Match m in AipTillText.Matches(stream)) tokens.Add(new AipToken(m.Index, 'T'));
            foreach (Match m in AipZoneText.Matches(stream)) tokens.Add(new AipToken(m.Index, 'Z', 0, 0, false, m.Groups[1].Value));
            tokens.Sort((a, b) => a.Index.CompareTo(b.Index));

            List<(string? Zone, List<double[]> Points)> found = [];
            List<double[]> points = [];
            string? zone = null;
            bool collecting = true;
            double[]? circleCentre = null;
            bool inCircle = false;
            (double Radius, bool Clockwise, double[]? Centre, bool Till)? arc = null;
            bool needCentre = false;
            reversed = 0;
            void Finish()
            {
                if (points.Count >= 3)
                {
                    if (points[0][0] != points[^1][0] || points[0][1] != points[^1][1]) points.Add(points[0]);
                    found.Add((zone, points));
                }
                points = [];
                arc = null;
                needCentre = false;
                collecting = false;
            }
            foreach (AipToken t in tokens)
            {
                switch (t.Kind)
                {
                    case 'Z':
                        if (collecting) Finish();
                        zone = t.Text;
                        collecting = true;
                        inCircle = false;
                        circleCentre = null;
                        break;
                    case 'O':
                        if (collecting) { inCircle = true; circleCentre = null; }
                        break;
                    case 'A':
                        if (collecting) { arc = (t.A, t.Flag, null, false); needCentre = true; }
                        break;
                    case 'C':
                        if (!collecting) break;
                        double[] here = [t.A, t.B];
                        if (inCircle && circleCentre == null) circleCentre = here;
                        else if (arc != null && needCentre) { arc = (arc.Value.Radius, arc.Value.Clockwise, here, arc.Value.Till); needCentre = false; }
                        else if (arc != null && arc.Value.Till && arc.Value.Centre != null && points.Count > 0)
                        {
                            points.AddRange(MapCoordinates.ArcChecked(arc.Value.Centre, arc.Value.Radius, points[^1], here, arc.Value.Clockwise, out bool turned));
                            if (turned) reversed++;
                            points.Add(here);
                            arc = null;
                        }
                        else points.Add(here);
                        break;
                    case 'W':
                        if (collecting && inCircle && circleCentre != null)
                        {
                            points = MapCoordinates.Circle(circleCentre[0], circleCentre[1], t.A);
                            inCircle = false;
                            Finish();
                        }
                        break;
                    case 'T':
                        if (collecting && arc != null && !needCentre) arc = (arc.Value.Radius, arc.Value.Clockwise, arc.Value.Centre, true);
                        break;
                    case 'G':
                        if (!collecting) break;
                        if (t.Flag && arc != null && arc.Value.Centre != null && points.Count > 0)
                        {
                            points.AddRange(MapCoordinates.ArcChecked(arc.Value.Centre, arc.Value.Radius, points[^1], points[0], arc.Value.Clockwise, out bool turnedBack));
                            if (turnedBack) reversed++;
                        }
                        Finish();
                        break;
                }
            }
            if (collecting) Finish();
            return found;
        }

        /// <summary>
        /// CTR (ENR 2.1.2.x, with zones such as Catania) and ATZ (ENR 2.1.3, and the AD 2 sheets of the airports) as polygons.
        /// Read by the place of the words: only the first column (designator and lateral limits, left of the header "Limiti
        /// verticali") is used. A row of that column that ends with CTR or ATZ names the airspace, and so does the heading
        /// "ENR 2.1.3.n NAME" (an ATZ). Each polygon has its name as label.
        /// </summary>
        public static FileImport ImportAipAirspace(string fileName, IReadOnlyList<PdfWord> words)
        {
            FileImport result = new() { FileName = fileName, FormatName = "AIP airspaces (CTR, ATZ)" };
            List<(bool IsName, string Text)> events = [];
            double? split = null;
            foreach (IGrouping<int, PdfWord> page in words.GroupBy(w => w.Page).OrderBy(g => g.Key))
            {
                List<List<PdfWord>> rows = [];
                foreach (PdfWord word in page.OrderBy(w => w.Y).ThenBy(w => w.X))
                {
                    if (rows.Count > 0 && Math.Abs(rows[^1][0].Y - word.Y) <= 2.5) rows[^1].Add(word);
                    else rows.Add([word]);
                }
                foreach (List<PdfWord> row in rows)
                {
                    List<PdfWord> limits = row.Where(w => w.Text.Equals("limiti", StringComparison.OrdinalIgnoreCase)).OrderBy(w => w.X).ToList();
                    if (limits.Count >= 2) split = limits[^1].X - 8;
                }
                if (split == null) continue;
                foreach (List<PdfWord> row in rows)
                {
                    List<PdfWord> ordered = row.OrderBy(w => w.X).ToList();
                    string full = string.Join(" ", ordered.Select(w => w.Text)).Trim();
                    string left = string.Join(" ", ordered.Where(w => w.X < split.Value).Select(w => w.Text)).Trim();
                    Match heading = AipAtzHeading.Match(full);
                    if (heading.Success)
                    {
                        string title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(heading.Groups[1].Value.Trim().ToLowerInvariant());
                        events.Add((true, title + " ATZ"));
                        continue;
                    }
                    if (ordered[0].X < split.Value && AipDesignator.IsMatch(left))
                    {
                        events.Add((true, left));
                        continue;
                    }
                    events.Add((false, left));
                }
            }

            MapFile ctr = new(), atz = new();
            int reversedTotal = 0;
            void Flush(string? name, System.Text.StringBuilder text)
            {
                if (name == null) return;
                string stream = Regex.Replace(text.ToString().Replace("''", "\"").Replace("’’", "\""), @"\s+", " ");
                List<(string? Zone, List<double[]> Points)> polygons = AipPolygons(stream, out int reversed);
                reversedTotal += reversed;
                foreach ((string? zone, List<double[]> points) in polygons)
                {
                    MapFile target = name.EndsWith(" CTR", StringComparison.Ordinal) ? ctr : atz;
                    string label = zone == null ? name : $"{name} {zone}";
                    target.Lines.Add(new MapLine { Name = label, Points = points });
                    double[] centre = MapCoordinates.Centroid(points);
                    target.Labels.Add(new MapLabel { Text = label, Latitude = centre[0], Longitude = centre[1] });
                }
            }
            string? current = null;
            System.Text.StringBuilder buffer = new();
            foreach ((bool isName, string text) in events)
            {
                if (isName)
                {
                    Flush(current, buffer);
                    current = text;
                    buffer = new System.Text.StringBuilder();
                }
                else if (current != null) buffer.Append(text).Append(' ');
            }
            Flush(current, buffer);

            if (ctr.Lines.Count > 0) result.Entries.Add(NewEntry(fileName, "CTR (AIP)", MapCategory.Ctr, ctr, "AIP Italia"));
            if (atz.Lines.Count > 0) result.Entries.Add(NewEntry(fileName, "ATZ (AIP)", MapCategory.Atz, atz, "AIP Italia"));
            if (reversedTotal > 0) result.Notes.Add($"{Plural(reversedTotal, "arc of circle was", "arcs of circle were")} turned the other way (the AIP text sends it more than 300° round, which is an error): check the picture.");
            if (result.Entries.Count == 0) result.Notes.Add("No CTR or ATZ found in this file.");
            else result.Notes.Add($"{ctr.Lines.Count} CTR and {atz.Lines.Count} ATZ polygons, each with its name as a label.");
            return result;
        }
    }
}
