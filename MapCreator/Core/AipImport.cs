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
        private static readonly Regex AipTypeWord = new("^(D?VOR|DME|TACAN|VORTAC|NDB|L|ILS|LOC|TLS)(/(D?VOR|DME|TACAN|VORTAC|NDB|L|ILS|LOC|TLS))*$", RegexOptions.Compiled);

        /// <summary>Which list a PDF is, from the title on its first page: "vrp", "fix", "vor1", "vor2"; null: not one of these.</summary>
        internal static string? AipKind(IReadOnlyList<PdfWord> words)
        {
            if (words.Count == 0) return null;
            int first = words.Min(w => w.Page);
            string head = string.Join(" ", words.Where(w => w.Page == first).OrderBy(w => w.Y).ThenBy(w => w.X).Take(120).Select(w => w.Text));
            if (AipVrpTitle.IsMatch(head)) return "vrp";
            if (AipFixTitle.IsMatch(head)) return "fix";
            Match nav = AipNavTitle.Match(head);
            if (nav.Success) return "vor" + nav.Groups[1].Value;
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
    }
}
