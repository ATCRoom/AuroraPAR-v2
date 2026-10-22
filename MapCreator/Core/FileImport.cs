using System.Globalization;
using System.Text.RegularExpressions;

namespace ATCRoom.Maps
{
    /// <summary>
    /// A part of a file that goes into one category of the map: a whole small file (the FIX of an airport), or one group
    /// of a file (the "LIBA CTR" group of a .str, one sector of a lartcc). The category is chosen by the user; the
    /// <see cref="Suggested"/> one comes from the kind of file or the name (a suggestion, never a rule).
    /// </summary>
    public sealed class ImportEntry
    {
        /// <summary>Name of the file it comes from.</summary>
        public string File { get; init; } = "";
        public string Title { get; init; } = "";
        public string? Suggested { get; init; }
        /// <summary>The category chosen (a <see cref="MapCategory"/> id); null: not used.</summary>
        public string? Category { get; set; }
        /// <summary>The items, without a category.</summary>
        public MapFile Content { get; init; } = new();
        /// <summary>The points the user switched off ("Choose points"); they do not go into the map.</summary>
        public HashSet<MapPoint> Excluded { get; } = [];
        /// <summary>The points that were renamed in "Choose points", with the name they had in the file (for "Restore names").</summary>
        public Dictionary<MapPoint, string> OriginalNames { get; } = [];

        /// <summary>The content without the points switched off (the lines and labels are shared, not copied).</summary>
        public MapFile Selected()
        {
            if (Excluded.Count == 0) return Content;
            return new MapFile
            {
                Lines = Content.Lines,
                Labels = Content.Labels,
                Points = Content.Points.Where(p => !Excluded.Contains(p)).ToList()
            };
        }
    }

    /// <summary>What was read from one file: its parts (entries) and notes on what was skipped.</summary>
    public sealed class FileImport
    {
        public string FileName { get; init; } = "";
        public string FormatName { get; init; } = "";
        public List<ImportEntry> Entries { get; } = [];
        public List<string> Notes { get; } = [];
        /// <summary>Airport (ICAO) guessed from the file, for the files that are made for one airport.</summary>
        public string? Airport { get; set; }
    }

    public static partial class MapImport
    {
        private static readonly Regex Icao = new("^[A-Z0-9]{4}$", RegexOptions.Compiled);
        private static readonly Regex NotAnAirport = new("^[A-Z]{2}ZZ$", RegexOptions.Compiled);
        private static readonly Regex Altitude = new("^\\d{3,5}(\\s*-\\s*\\d{3,5})?$", RegexOptions.Compiled);

        /// <summary>
        /// Reads a file (the lines of a text file, or the words of a PDF) with the importer of its kind, from its
        /// extension, else from its content. Kinds: IVAO geographic lines (.geo), FIX (.fix), VOR (.vor), NDB (.ndb),
        /// airports (.ap), VRP (.vfi), maps of an airport (.str: its MAPS groups), polygons (lartcc / hartcc), Aurora
        /// map (.mva, T and L lines), EuroScope .ese, AIP table (PDF).
        /// </summary>
        public static FileImport Read(string fileName, IReadOnlyList<string>? lines, IReadOnlyList<PdfWord>? words)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (words != null) return FromAip(fileName, words);
            if (lines == null) return Unknown(fileName);
            FileImport result;
            switch (extension)
            {
                case ".geo": result = ImportGeo(fileName, lines); break;
                case ".fix": result = ImportFixes(fileName, lines); break;
                case ".vor": result = ImportNavaids(fileName, lines, ndb: false); break;
                case ".ndb": result = ImportNavaids(fileName, lines, ndb: true); break;
                case ".ap": case ".apt": result = ImportAirports(fileName, lines); break;
                case ".vfi": result = ImportVrp(fileName, lines); break;
                case ".str": result = ImportStr(fileName, lines); break;
                case ".lartcc": case ".hartcc": case ".artcc": result = ImportPolygons(fileName, lines); break;
                case ".mva": result = FromAurora(fileName, lines); break;
                case ".ese": result = FromEuroScope(fileName, lines); break;
                default:
                    switch (Sniff(lines))
                    {
                        case "ese": result = FromEuroScope(fileName, lines); break;
                        case "aurora": result = FromAurora(fileName, lines); break;
                        case "str": result = ImportStr(fileName, lines); break;
                        case "geo": result = ImportGeo(fileName, lines); break;
                        case "ap": result = ImportAirports(fileName, lines); break;
                        case "fix": result = ImportFixes(fileName, lines); break;
                        case "vor": result = ImportNavaids(fileName, lines, ndb: false); break;
                        case "ndb": result = ImportNavaids(fileName, lines, ndb: true); break;
                        case "vrp": result = ImportVrp(fileName, lines); break;
                        default: return Unknown(fileName);
                    }
                    break;
            }
            // Files made for one airport carry its ICAO in the name (libc.vfi, libf.str); the national files do not.
            if (result.Airport == null && extension is ".vfi" or ".str" or ".mva")
            {
                Match m = Regex.Match(Path.GetFileNameWithoutExtension(fileName), "^([A-Za-z]{4})(?:[^A-Za-z]|$)");
                if (m.Success) result.Airport = m.Groups[1].Value.ToUpperInvariant();
            }
            // A part with nothing in it (all rows hidden or skipped) is not offered: it would otherwise erase a category already saved.
            result.Entries.RemoveAll(e => e.Content.Lines.Count == 0 && e.Content.Labels.Count == 0 && e.Content.Points.Count == 0);
            return result;
        }

        private static FileImport Unknown(string fileName)
        {
            FileImport result = new() { FileName = fileName, FormatName = "not recognised" };
            result.Notes.Add("Format not recognised. Known: IVAO files .geo, .fix, .vor, .ndb, .ap, .vfi, .str, .mva, lartcc / hartcc; EuroScope .ese; AIP table of minimum altitudes (PDF).");
            return result;
        }

        private static ImportEntry NewEntry(string file, string title, string? suggested, MapFile content)
        {
            content.Normalize();
            return new ImportEntry { File = file, Title = title, Suggested = suggested, Category = suggested, Content = content };
        }

        /// <summary>One row of a sector file: the comment at the end of the row removed, the fields trimmed.</summary>
        private static string[] Fields(string row)
        {
            int comment = row.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) row = row[..comment];
            return row.Split(';').Select(s => s.Trim()).ToArray();
        }

        private static bool IsComment(string row) => row.TrimStart().StartsWith("//", StringComparison.Ordinal);

        private static bool Coordinate(string latitudeText, string longitudeText, out double latitude, out double longitude)
        {
            latitude = longitude = double.NaN;
            if (!MapCoordinates.TryParseAny(latitudeText, out double a) || !MapCoordinates.TryParseAny(longitudeText, out double b)) return false;
            if (!MapCoordinates.IsValid(a, b)) return false;
            latitude = a;
            longitude = b;
            return true;
        }

        private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

        private static bool IsStrHeader(string[] f) =>
            f.Length >= 6 && Icao.IsMatch(f[0].ToUpperInvariant()) && f[1].Length > 0 && f[2].Length > 0 && f[3].Length == 0 && f[4].Length == 0;

        private static string? SuggestByName(string name)
        {
            if (Regex.IsMatch(name, "\\bCTR\\b", RegexOptions.IgnoreCase)) return MapCategory.Ctr;
            if (Regex.IsMatch(name, "\\bATZ\\b", RegexOptions.IgnoreCase)) return MapCategory.Atz;
            return null;
        }

        private static string Sniff(IReadOnlyList<string> lines)
        {
            if (Detect(lines) == MapSourceFormat.EuroScope) return "ese";
            foreach (string raw in lines.Take(600))
            {
                if (raw.Trim().Length == 0 || IsComment(raw)) continue;
                string[] f = Fields(raw);
                if (f.Length >= 4 && (f[0] == "T" || f[0] == "L")) return "aurora";
                if (IsStrHeader(f)) return "str";
                if (f.Length >= 4 && Coordinate(f[0], f[1], out _, out _) && Coordinate(f[2], f[3], out _, out _)) return "geo";
                if (f.Length >= 6 && Icao.IsMatch(f[0].ToUpperInvariant()) && Coordinate(f[3], f[4], out _, out _)) return "ap";
                if (f.Length >= 4 && Coordinate(f[2], f[3], out _, out _))
                {
                    if (double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double frequency))
                    {
                        if (frequency >= 190 && frequency <= 1800) return "ndb";
                        if (frequency >= 100 && frequency <= 140) return "vor";
                    }
                    return "vrp";
                }
                if (f.Length >= 3 && Coordinate(f[1], f[2], out _, out _)) return "fix";
            }
            return "";
        }

        // ----- Wrappers of the first importers -----------------------------------------------------------------------

        private static FileImport FromAurora(string fileName, IReadOnlyList<string> lines)
        {
            ImportResult r = ImportAurora(lines);
            FileImport result = new() { FileName = fileName, FormatName = "Aurora map (IVAO): lines T; and labels L;" };
            result.Entries.Add(NewEntry(fileName, "Lines and labels", MapCategory.Mva, r.Map));
            result.Notes.AddRange(r.Notes);
            return result;
        }

        private static FileImport FromEuroScope(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "EuroScope .ese (VATSIM)" };
            ImportResult msaw = ImportEuroScope(lines, new EseOptions { SectorLines = false, FreeText = false, MsawAreas = true });
            ImportResult sectors = ImportEuroScope(lines, new EseOptions { SectorLines = true, FreeText = false, MsawAreas = false });
            ImportResult texts = ImportEuroScope(lines, new EseOptions { SectorLines = false, FreeText = true, MsawAreas = false });
            result.Entries.Add(NewEntry(fileName, "MSAW areas (minimum altitudes), with their altitude", MapCategory.Mva, msaw.Map));
            result.Entries.Add(NewEntry(fileName, "Sector lines and circles", null, sectors.Map));
            result.Entries.Add(NewEntry(fileName, "Free texts", null, texts.Map));
            result.Notes.AddRange(msaw.Notes.Concat(sectors.Notes).Concat(texts.Notes).Where(n => n.Contains("skipped") || n.Contains("not understood")).Distinct());
            return result;
        }

        private static FileImport FromAip(string fileName, IReadOnlyList<PdfWord> words)
        {
            ImportResult r = ImportAipTable(words);
            FileImport result = new() { FileName = fileName, FormatName = "AIP table of minimum altitudes (PDF)" };
            if (r.Map.Lines.Count > 0) result.Entries.Add(NewEntry(fileName, "Areas with their minimum altitude", MapCategory.Mva, r.Map));
            result.Notes.AddRange(r.Notes);
            return result;
        }

        // ----- IVAO geographic lines (.geo) --------------------------------------------------------------------------

        /// <summary>
        /// "lat;lon;lat;lon;TAG;": one segment of a line (TAG COAST for the coast). The end of a segment that is the start of
        /// the next one joins them into one line; a blank row or a comment starts a new line, and the comment before a block
        /// is its name. Coordinates with or without dots.
        /// </summary>
        public static FileImport ImportGeo(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "IVAO geographic lines (.geo)" };
            Dictionary<string, MapFile> byTag = new(StringComparer.OrdinalIgnoreCase);
            List<string> order = [];
            string name = "";
            MapLine? current = null;
            string currentTag = "";
            double lastLatitude = double.NaN, lastLongitude = double.NaN;
            int skipped = 0, segments = 0;
            foreach (string raw in lines)
            {
                string row = raw.Trim();
                if (row.Length == 0)
                {
                    current = null;
                    name = "";
                    continue;
                }
                if (IsComment(row))
                {
                    name = row.TrimStart('/').Trim();
                    current = null;
                    continue;
                }
                string[] f = Fields(row);
                if (f.Length < 4 || !Coordinate(f[0], f[1], out double lat1, out double lon1) || !Coordinate(f[2], f[3], out double lat2, out double lon2))
                {
                    skipped++;
                    current = null;
                    continue;
                }
                string tag = f.Length > 4 && f[4].Length > 0 ? f[4].ToUpperInvariant() : "GEO";
                if (!byTag.TryGetValue(tag, out MapFile? file))
                {
                    file = new MapFile();
                    byTag[tag] = file;
                    order.Add(tag);
                }
                bool continues = current != null && currentTag == tag && Math.Abs(lastLatitude - lat1) < 1e-7 && Math.Abs(lastLongitude - lon1) < 1e-7;
                if (!continues)
                {
                    current = new MapLine { Name = name };
                    current.Points.Add([lat1, lon1]);
                    file.Lines.Add(current);
                    currentTag = tag;
                }
                current!.Points.Add([lat2, lon2]);
                lastLatitude = lat2;
                lastLongitude = lon2;
                segments++;
            }
            foreach (string tag in order)
            {
                string title = tag == "COAST" ? "Coast (COAST)" : tag;
                result.Entries.Add(NewEntry(fileName, title, tag == "COAST" ? MapCategory.Boundaries : null, byTag[tag]));
            }
            result.Notes.Add($"{Plural(segments, "segment", "segments")} joined into lines.");
            if (skipped > 0) result.Notes.Add($"{Plural(skipped, "row", "rows")} not understood, skipped.");
            return result;
        }

        // ----- FIX (.fix) --------------------------------------------------------------------------------------------

        /// <summary>
        /// "NAME;lat;lon;TYPE;BOUNDARY;[HLD-NAME;]": TYPE 0 en-route, 1 terminal, 2 both, 3 hidden (the procedure points of the airports and
        /// of the military ones). Terminal and "both" fixes are one part (suggested: FIX), the en-route ones another, the hidden
        /// ones a third (suggested: FIX); the comment before a block is the group of its points; the holding tag is ignored.
        /// </summary>
        public static FileImport ImportFixes(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "IVAO fixes (.fix)" };
            MapFile terminal = new(), enroute = new(), hiddenFixes = new();
            int bad = 0;
            string group = "";
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) continue;
                if (IsComment(raw))
                {
                    // A comment before a block names it: "//LIBC", "//<!--LIPX-->", "//nuovi LIRN" (the airport), or any other text.
                    string text = raw.Trim().Replace("<!--", " ").Replace("-->", " ").Replace("<", " ").Replace(">", " ").Trim('/', ' ', ';', '\t');
                    if (text.Length == 0) continue;
                    Match code = Regex.Match(text, "\\b[A-Z]{4}\\b");
                    group = code.Success ? code.Value : text;
                    continue;
                }
                string[] f = Fields(raw);
                if (f.Length < 3 || f[0].Length == 0 || !Coordinate(f[1], f[2], out double lat, out double lon))
                {
                    bad++;
                    continue;
                }
                // The type is its first digits ("3:" is a typo for "3"); no type: terminal and en-route both.
                string digits = f.Length > 3 ? new string(f[3].TakeWhile(char.IsDigit).ToArray()) : "";
                int type = digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int t) ? t : 2;
                MapPoint point = new() { Name = f[0], Latitude = lat, Longitude = lon, Symbol = MapSymbol.Point, Group = group };
                (type == 3 ? hiddenFixes : type == 0 ? enroute : terminal).Points.Add(point);
            }
            result.Entries.Add(NewEntry(fileName, "Terminal fixes and fixes for both", MapCategory.Fix, terminal));
            result.Entries.Add(NewEntry(fileName, "En-route fixes", null, enroute));
            result.Entries.Add(NewEntry(fileName, "Hidden fixes (type 3): procedure points of airports and military", MapCategory.Fix, hiddenFixes));
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            return result;
        }

        // ----- VOR (.vor) and NDB (.ndb) -----------------------------------------------------------------------------

        /// <summary>
        /// VOR "ID;frequency;lat;lon;[visibility];[TACAN type];[TACAN channel];[HLD-ID;]" (TACAN type 2: VOR with TACAN =
        /// VORTAC, 3: TACAN alone); NDB "ID;frequency;lat;lon;[1];". Visibility 1 = hidden (the foreign navaids kept for the
        /// airways): skipped. A row with impossible coordinates is skipped and named in the notes.
        /// </summary>
        public static FileImport ImportNavaids(string fileName, IReadOnlyList<string> lines, bool ndb)
        {
            FileImport result = new() { FileName = fileName, FormatName = ndb ? "IVAO NDB (.ndb)" : "IVAO VOR and TACAN (.vor)" };
            MapFile content = new();
            int hidden = 0, bad = 0;
            List<string> badNames = [];
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0 || IsComment(raw)) continue;
                string[] f = Fields(raw);
                if (f.Length < 4 || f[0].Length == 0)
                {
                    bad++;
                    continue;
                }
                if (f.Length > 4 && f[4] == "1")
                {
                    hidden++;
                    continue;
                }
                if (!Coordinate(f[2], f[3], out double lat, out double lon))
                {
                    bad++;
                    if (badNames.Count < 5) badNames.Add(f[0]);
                    continue;
                }
                MapSymbol symbol = MapSymbol.Ndb;
                if (!ndb)
                {
                    string tacan = f.Length > 5 ? f[5] : "";
                    symbol = tacan == "2" ? MapSymbol.Vortac : tacan == "3" ? MapSymbol.Tacan : MapSymbol.Vor;
                }
                content.Points.Add(new MapPoint { Name = f[0], Latitude = lat, Longitude = lon, Symbol = symbol });
            }
            result.Entries.Add(NewEntry(fileName, ndb ? "NDB" : "VOR, VORTAC and TACAN", MapCategory.Navaids, content));
            if (hidden > 0) result.Notes.Add($"{Plural(hidden, "hidden navaid", "hidden navaids")} (the foreign ones for the airways) skipped.");
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} skipped: not understood or impossible coordinates{(badNames.Count > 0 ? " (" + string.Join(", ", badNames) + ")" : "")}.");
            return result;
        }

        // ----- Airports (.ap) ----------------------------------------------------------------------------------------

        /// <summary>
        /// "ICAO;elevation;transition altitude;lat;lon;NAME;[hide];": a point per airport, labelled with the ICAO code (the name
        /// is kept as its description). A row that is not an airport (ICAO ending in ZZ, "AIR DEFENCE") is skipped.
        /// </summary>
        public static FileImport ImportAirports(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "IVAO airports (.ap)" };
            MapFile content = new();
            List<string> skippedNames = [];
            int bad = 0;
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0 || IsComment(raw)) continue;
                string[] f = Fields(raw);
                string code = f.Length > 0 ? f[0].ToUpperInvariant() : "";
                if (f.Length < 6 || !Icao.IsMatch(code) || !Coordinate(f[3], f[4], out double lat, out double lon))
                {
                    bad++;
                    continue;
                }
                if (NotAnAirport.IsMatch(code) || (f.Length > 6 && f[6] == "1"))
                {
                    skippedNames.Add(f[5].Length > 0 ? $"{code} ({f[5]})" : code);
                    continue;
                }
                content.Points.Add(new MapPoint { Name = code, Description = f[5], Latitude = lat, Longitude = lon, Symbol = MapSymbol.Airport });
            }
            result.Entries.Add(NewEntry(fileName, "Airports", MapCategory.Airports, content));
            if (skippedNames.Count > 0) result.Notes.Add($"Skipped (not airports or hidden): {string.Join(", ", skippedNames)}.");
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            return result;
        }

        // ----- VRP (.vfi) --------------------------------------------------------------------------------------------

        /// <summary>
        /// "NAME;CODE;lat;lon;" (the Italian files: a short code of the point) or "NAME;ALTITUDE;lat;lon;" (the IVAO
        /// description: an altitude or a range). The label is the code (short), the name is kept as the description; with
        /// an altitude the label is the name.
        /// </summary>
        public static FileImport ImportVrp(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "IVAO VFR points (.vfi)" };
            MapFile content = new();
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
                bool altitude = Altitude.IsMatch(f[1]);
                string label = altitude || f[1].Length == 0 ? f[0] : f[1];
                string description = altitude ? f[1] : f[0];
                content.Points.Add(new MapPoint { Name = label, Description = description, Latitude = lat, Longitude = lon, Symbol = MapSymbol.Vrp });
            }
            result.Entries.Add(NewEntry(fileName, "VFR reporting points", MapCategory.Vrp, content));
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            return result;
        }

        // ----- Polygons of lartcc / hartcc ---------------------------------------------------------------------------

        /// <summary>
        /// Airspace file of the sector file ([ARTCC LOW] / [ARTCC HIGH]): "T;ID;lat;lon;" one point of the border of the
        /// airspace ID, "L;ID;lat;lon;[size];" a label. One part per ID. What an ID is (a sector, a CTR, an ATZ) is
        /// decided by the user: the name only suggests it.
        /// </summary>
        public static FileImport ImportPolygons(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "IVAO airspaces (lartcc / hartcc)" };
            Dictionary<string, MapFile> byId = new(StringComparer.Ordinal);
            List<string> order = [];
            MapLine? current = null;
            string currentId = "";
            int bad = 0;
            foreach (string raw in lines)
            {
                string row = raw.Trim();
                if (row.Length == 0)
                {
                    current = null;
                    continue;
                }
                if (IsComment(row)) continue;
                string[] f = Fields(row);
                bool track = f.Length >= 4 && f[0].Equals("T", StringComparison.OrdinalIgnoreCase);
                bool label = f.Length >= 4 && f[0].Equals("L", StringComparison.OrdinalIgnoreCase);
                if (!(track || label) || f[1].Length == 0 || !Coordinate(f[2], f[3], out double lat, out double lon))
                {
                    bad++;
                    current = null;
                    continue;
                }
                string id = f[1];
                if (!byId.TryGetValue(id, out MapFile? file))
                {
                    file = new MapFile();
                    byId[id] = file;
                    order.Add(id);
                }
                if (label)
                {
                    string text = id;
                    double size = MapLabel.DefaultSize;
                    if (f.Length > 4 && f[4].Length > 0)
                    {
                        if (double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) size = s;
                        else text = f[4];
                    }
                    file.Labels.Add(new MapLabel { Text = text, Latitude = lat, Longitude = lon, Size = size });
                    current = null;
                    continue;
                }
                if (current == null || currentId != id)
                {
                    current = new MapLine { Name = id };
                    file.Lines.Add(current);
                    currentId = id;
                }
                AddPoint(current, lat, lon);
            }
            foreach (string id in order)
            {
                MapFile file = byId[id];
                if (file.Labels.Count == 0) AddCentroidLabels(file, id, onlyFirst: true);
                result.Entries.Add(NewEntry(fileName, id, SuggestByName(id), file));
            }
            if (bad > 0) result.Notes.Add($"{Plural(bad, "row", "rows")} not understood, skipped.");
            return result;
        }

        // ----- Maps of an airport (.str) -----------------------------------------------------------------------------

        /// <summary>
        /// The .str file of an airport is a list of groups: a header "ICAO;RUNWAYS;NAME; ; ;TYPE;" and the rows of its
        /// path. Only the groups of the maps (RUNWAYS = MAPS) are read here: the polygons of the CTR, the ATZ, the extended
        /// centrelines. The STAR, IAP and holding groups are not used in this version. A row is "lat;lon;" (also with
        /// decimal degrees), "&lt;br&gt;" in the third field starts a new path at that point, a blank row breaks the path,
        /// a comment at the end of the row is ignored. A group written with the names of points (NAME;NAME;) is a
        /// procedure drawn as a map: not used in this version. The TYPE is not used: what a group is (CTR, ATZ...) is decided by the
        /// user, the name only suggests it.
        /// </summary>
        public static FileImport ImportStr(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "IVAO airport maps (.str)" };
            string? groupName = null;
            bool inMaps = false;
            MapFile? content = null;
            MapLine? current = null;
            bool namesInGroup = false;
            int otherGroups = 0, unresolved = 0;

            void Finish()
            {
                // A group whose path is written with the names of points (a STAR drawn as a map) needs the fix files: later.
                if (content != null && namesInGroup) otherGroups++;
                else if (content != null && groupName != null && content.Lines.Count > 0)
                {
                    foreach (MapLine line in content.Lines) line.Name = groupName;
                    AddCentroidLabels(content, groupName, onlyFirst: false);
                    result.Entries.Add(NewEntry(fileName, groupName, SuggestByName(groupName), content));
                }
                content = null;
                current = null;
                namesInGroup = false;
            }

            foreach (string raw in lines)
            {
                string row = raw.Trim();
                if (row.Length > 0 && !IsComment(row))
                {
                    string[] header = Fields(row);
                    if (IsStrHeader(header))
                    {
                        Finish();
                        inMaps = header[1].Equals("MAPS", StringComparison.OrdinalIgnoreCase);
                        groupName = header[2];
                        if (inMaps) content = new MapFile();
                        else otherGroups++;
                        result.Airport ??= header[0].ToUpperInvariant();
                        continue;
                    }
                }
                if (!inMaps || content == null) continue;
                if (row.Length == 0)
                {
                    current = null;
                    continue;
                }
                if (IsComment(row)) continue;
                string[] f = Fields(row);
                if (f.Length < 2 || !Coordinate(f[0], f[1], out double lat, out double lon))
                {
                    if (f.Length >= 2 && f[0].Length > 0 && f[0] == f[1]) namesInGroup = true;
                    else unresolved++;
                    current = null;
                    continue;
                }
                bool newPath = f.Length > 2 && f[2].Equals("<br>", StringComparison.OrdinalIgnoreCase);
                if (current == null || newPath)
                {
                    current = new MapLine();
                    content.Lines.Add(current);
                }
                AddPoint(current, lat, lon);
            }
            Finish();
            if (otherGroups > 0) result.Notes.Add($"{Plural(otherGroups, "group", "groups")} of procedures (STAR, IAP, holdings) not used in this version.");
            if (unresolved > 0) result.Notes.Add($"{Plural(unresolved, "row", "rows")} of the maps without coordinates skipped.");
            if (result.Entries.Count == 0) result.Notes.Add("No maps found (groups with MAPS in the second field).");
            return result;
        }

        // ----- Shared ------------------------------------------------------------------------------------------------

        /// <summary>Adds a point to a line, except when it is the same as the last one.</summary>
        private static void AddPoint(MapLine line, double latitude, double longitude)
        {
            if (line.Points.Count > 0)
            {
                double[] last = line.Points[^1];
                if (Math.Abs(last[0] - latitude) < 1e-9 && Math.Abs(last[1] - longitude) < 1e-9) return;
            }
            line.Points.Add([latitude, longitude]);
        }

        /// <summary>A label with the name at the centre of each closed area (of the first one only, when asked).</summary>
        private static void AddCentroidLabels(MapFile file, string text, bool onlyFirst)
        {
            foreach (MapLine line in file.Lines)
            {
                if (line.Points.Count < 4) continue;
                double[] first = line.Points[0];
                double[] last = line.Points[^1];
                if (Math.Abs(first[0] - last[0]) > 1e-9 || Math.Abs(first[1] - last[1]) > 1e-9) continue;
                double[] centre = MapCoordinates.Centroid(line.Points);
                if (!MapCoordinates.IsValid(centre[0], centre[1])) continue;
                file.Labels.Add(new MapLabel { Text = text, Latitude = centre[0], Longitude = centre[1] });
                if (onlyFirst) break;
            }
        }
    }
}
