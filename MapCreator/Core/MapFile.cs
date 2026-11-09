using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATCRoom.Maps
{
    /// <summary>
    /// A radar map in the open ATCRoom format (JSON, see docs/MAP-FORMAT.md): lines, labels and points, each with its
    /// category (version 2: one file per airport, see <see cref="MapCategory"/>), or a whole map with the colours of its
    /// lines and labels (version 1). Version 3 adds the block "Ground" (the ground layout of the aerodrome: areas, lines,
    /// labels): in memory its items are in <see cref="Lines"/> and <see cref="Labels"/> with their ground categories like
    /// all the others; only the file keeps them apart, so the programs made before version 3 do not see them and cannot
    /// spoil them. Written by ATCRoom Map Creator, read by AuroraPAR v2 Trainer and TWR Trainer. Coordinates are latitude
    /// and longitude in degrees (WGS 84, north and east positive). Nothing a program does not know is lost: unknown
    /// fields and categories are kept as they are.
    /// </summary>
    public sealed class MapFile
    {
        /// <summary>The version of a file without the ground layout (unchanged since version 2).</summary>
        public const int CurrentFormatVersion = 2;
        /// <summary>The version of a file with the block "Ground".</summary>
        public const int GroundFormatVersion = 3;
        public const string DefaultColour = "#2F6F86";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new JsonStringEnumConverter() }
        };

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        /// <summary>Name shown in the list of maps.</summary>
        public string Name { get; set; } = "";
        /// <summary>Version 2: the airport (ICAO) the map is for; empty: a map of version 1, shown for every airport.</summary>
        public string Airport { get; set; } = "";
        /// <summary>Optional: where the data come from, who made the map, its licence (free text).</summary>
        public string Source { get; set; } = "";
        /// <summary>Optional, version 2: the source of each category (by category id), free text.</summary>
        public Dictionary<string, string> Sources { get; set; } = [];
        /// <summary>Colour of all the lines, #RRGGBB.</summary>
        public string LineColour { get; set; } = DefaultColour;
        /// <summary>Colour of all the labels, #RRGGBB.</summary>
        public string LabelColour { get; set; } = DefaultColour;
        public List<MapLine> Lines { get; set; } = [];
        public List<MapLabel> Labels { get; set; } = [];
        public List<MapPoint> Points { get; set; } = [];

        /// <summary>
        /// Only in the file (version 3): the ground layout. When a file is read its items are moved into <see cref="Lines"/>
        /// and <see cref="Labels"/> (with their categories); when it is written they are taken out of them again.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MapGround? Ground { get; set; }

        /// <summary>Fields of a newer version of the format, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>The fields of the block "Ground" that this version does not know, written back as they were.</summary>
        private Dictionary<string, JsonElement>? groundExtra;

        /// <summary>Reads a map file. Throws (with a readable message) when it is not a map.</summary>
        public static MapFile Load(string path)
        {
            MapFile map = Parse(File.ReadAllText(path));
            if (map.Name.Length == 0) map.Name = Path.GetFileNameWithoutExtension(path);
            return map;
        }

        /// <summary>Reads a map from its JSON text. Throws (with a readable message) when it is not a map.</summary>
        public static MapFile Parse(string json)
        {
            MapFile? map;
            try
            {
                map = JsonSerializer.Deserialize<MapFile>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Not a map file ({ex.Message}).", ex);
            }
            if (map == null) throw new InvalidDataException("Empty map file.");
            map.TakeGround();
            map.Normalize();
            return map;
        }

        /// <summary>Moves the items of the block "Ground" into the lines and labels, marked so they go back there when saved.</summary>
        private void TakeGround()
        {
            if (Ground == null) return;
            Lines ??= [];
            Labels ??= [];
            foreach (MapLine area in Ground.Areas ?? [])
            {
                if (area == null) continue;
                area.GroundList = MapGround.AreasList;
                Lines.Add(area);
            }
            foreach (MapLine line in Ground.Lines ?? [])
            {
                if (line == null) continue;
                line.GroundList = MapGround.LinesList;
                Lines.Add(line);
            }
            foreach (MapLabel label in Ground.Labels ?? [])
            {
                if (label == null) continue;
                label.InGround = true;
                Labels.Add(label);
            }
            groundExtra = Ground.Extra;
            Ground = null;
        }

        public void Save(string path)
        {
            string json = ToJson();
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, json);
        }

        private bool IsGroundLine(MapLine line) => line.GroundList != null || MapCategory.IsGround(line.Category);

        private bool IsGroundLabel(MapLabel label) => label.InGround || MapCategory.IsGround(label.Category);

        /// <summary>
        /// The text of the file: the ground items (ground categories, or read from the block "Ground") in the block "Ground"
        /// (version 3: the areas in the drawing order of their categories), everything else as in version 2.
        /// </summary>
        public string ToJson()
        {
            Normalize();
            MapGround ground = new()
            {
                Areas = Lines.Where(l => IsGroundLine(l) && (l.GroundList == MapGround.AreasList || (l.GroundList == null && MapCategory.IsArea(l.Category))))
                    .OrderBy(l => MapCategory.OrderOf(l.Category)).ToList(),
                Lines = Lines.Where(l => IsGroundLine(l) && !(l.GroundList == MapGround.AreasList || (l.GroundList == null && MapCategory.IsArea(l.Category)))).ToList(),
                Labels = Labels.Where(IsGroundLabel).ToList(),
                Extra = groundExtra
            };
            bool hasGround = ground.Areas.Count + ground.Lines.Count + ground.Labels.Count > 0 || (groundExtra?.Count ?? 0) > 0;
            MapFile file = new()
            {
                FormatVersion = hasGround ? Math.Max(FormatVersion, GroundFormatVersion) : FormatVersion,
                Name = Name,
                Airport = Airport,
                Source = Source,
                Sources = Sources,
                LineColour = LineColour,
                LabelColour = LabelColour,
                Lines = Lines.Where(l => !IsGroundLine(l)).ToList(),
                Labels = Labels.Where(l => !IsGroundLabel(l)).ToList(),
                Points = Points,
                Ground = hasGround ? ground : null,
                Extra = Extra
            };
            return JsonSerializer.Serialize(file, JsonOptions);
        }

        /// <summary>The category as written in <see cref="MapCategory.All"/>; one that is not known is kept as it is (a newer program may know it).</summary>
        private static string NormalCategory(string? category) => MapCategory.Normal(category) ?? (category ?? "").Trim();

        /// <summary>Repairs what a hand edit may have left: no invalid coordinates, no line with fewer than two points.</summary>
        public void Normalize()
        {
            Name = (Name ?? "").Trim();
            Airport = (Airport ?? "").Trim().ToUpperInvariant();
            Source = (Source ?? "").Trim();
            Dictionary<string, string> sources = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in Sources ?? [])
            {
                if (pair.Key == null || (pair.Value ?? "").Trim().Length == 0) continue;
                sources[MapCategory.Normal(pair.Key) ?? pair.Key.Trim()] = pair.Value.Trim();
            }
            Sources = sources;
            LineColour = NormalColour(LineColour, DefaultColour);
            LabelColour = NormalColour(LabelColour, LineColour);
            Lines = (Lines ?? []).Where(l => l != null).ToList();
            foreach (MapLine line in Lines)
            {
                line.Name = (line.Name ?? "").Trim();
                line.Category = NormalCategory(line.Category);
                line.Points = (line.Points ?? [])
                    .Where(p => p != null && p.Length >= 2 && MapCoordinates.IsValid(p[0], p[1]))
                    .Select(p => new[] { Math.Round(p[0], 6), Math.Round(p[1], 6) })
                    .ToList();
            }
            Lines.RemoveAll(l => l.Points.Count < 2);
            Labels = (Labels ?? []).Where(l => l != null && !string.IsNullOrWhiteSpace(l.Text) && MapCoordinates.IsValid(l.Latitude, l.Longitude)).ToList();
            foreach (MapLabel label in Labels)
            {
                label.Text = label.Text.Trim();
                label.Category = NormalCategory(label.Category);
                label.Latitude = Math.Round(label.Latitude, 6);
                label.Longitude = Math.Round(label.Longitude, 6);
                if (!double.IsFinite(label.Size) || label.Size <= 0) label.Size = MapLabel.DefaultSize;
                label.Size = Math.Clamp(label.Size, 4, 40);
            }
            Points = (Points ?? []).Where(p => p != null && MapCoordinates.IsValid(p.Latitude, p.Longitude)).ToList();
            foreach (MapPoint point in Points)
            {
                point.Name = (point.Name ?? "").Trim();
                point.Description = (point.Description ?? "").Trim();
                point.Category = NormalCategory(point.Category);
                point.Latitude = Math.Round(point.Latitude, 6);
                point.Longitude = Math.Round(point.Longitude, 6);
            }
        }

        /// <summary>A colour as #RRGGBB (upper case), or the fallback when it is not one.</summary>
        public static string NormalColour(string? text, string fallback)
        {
            string t = (text ?? "").Trim();
            if (t.StartsWith('#')) t = t[1..];
            if (t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return "#" + t.ToUpperInvariant();
            return fallback;
        }

        /// <summary>The categories that have something in this map, in the order of <see cref="MapCategory.All"/>.</summary>
        public IEnumerable<string> UsedCategories() =>
            MapCategory.All.Select(c => c.Id).Where(id =>
                Lines.Any(l => l.Category == id) || Labels.Any(l => l.Category == id) || Points.Any(p => p.Category == id));

        /// <summary>A map with only what belongs to one category (the items are shared, not copied).</summary>
        public MapFile OfCategory(string category) => new()
        {
            FormatVersion = FormatVersion,
            Name = Name,
            Airport = Airport,
            Lines = Lines.Where(l => l.Category == category).ToList(),
            Labels = Labels.Where(l => l.Category == category).ToList(),
            Points = Points.Where(p => p.Category == category).ToList()
        };

        /// <summary>
        /// Adds the items of another map (read from a file, without categories) as one category. Into a ground category:
        /// the labels and the points become ground labels (<see cref="MapCategory.GroundLabels"/>; a point gives its
        /// <see cref="MapPoint.LabelText"/>), the labels made by the importer at the centre of the areas are left out, and
        /// the "ground labels" category takes no lines.
        /// </summary>
        public void AddAs(MapFile source, string category)
        {
            bool ground = MapCategory.IsGround(category);
            foreach (MapLine l in source.Lines)
            {
                if (ground && string.Equals(category, MapCategory.GroundLabels, StringComparison.OrdinalIgnoreCase)) continue;
                Lines.Add(new MapLine { Name = l.Name, Category = category, Points = l.Points.Select(p => new[] { p[0], p[1] }).ToList(), Extra = l.Extra });
            }
            foreach (MapLabel l in source.Labels)
            {
                if (ground && l.Auto) continue;
                Labels.Add(new MapLabel { Text = l.Text, Latitude = l.Latitude, Longitude = l.Longitude, Size = l.Size, Category = ground ? MapCategory.GroundLabels : category, Extra = l.Extra });
            }
            foreach (MapPoint p in source.Points)
            {
                if (ground)
                {
                    string text = (p.LabelText ?? p.Name).Trim();
                    if (text.Length > 0) Labels.Add(new MapLabel { Text = text, Latitude = p.Latitude, Longitude = p.Longitude, Category = MapCategory.GroundLabels });
                    continue;
                }
                Points.Add(new MapPoint { Name = p.Name, Description = p.Description, Latitude = p.Latitude, Longitude = p.Longitude, Symbol = p.Symbol, Category = category, Extra = p.Extra });
            }
        }

        /// <summary>Adds everything of another map of version 2 (items with their categories, and the sources).</summary>
        public void Merge(MapFile other)
        {
            Lines.AddRange(other.Lines);
            Labels.AddRange(other.Labels);
            Points.AddRange(other.Points);
            foreach (KeyValuePair<string, string> pair in other.Sources) Sources[pair.Key] = pair.Value;
        }

        /// <summary>
        /// Keeps one of the points that have the same name and category and are closer than the tolerance (the same point read
        /// from two sources); returns how many were dropped. Points without a name are never dropped.
        /// </summary>
        public int RemoveDuplicatePoints(double toleranceNM = 0.1)
        {
            Dictionary<string, List<MapPoint>> kept = new(StringComparer.Ordinal);
            List<MapPoint> result = [];
            foreach (MapPoint p in Points)
            {
                if (p.Name.Length == 0)
                {
                    result.Add(p);
                    continue;
                }
                string key = p.Category + "|" + p.Name.ToUpperInvariant();
                if (!kept.TryGetValue(key, out List<MapPoint>? same))
                {
                    same = [];
                    kept[key] = same;
                }
                bool duplicate = false;
                foreach (MapPoint q in same)
                {
                    double dLat = (p.Latitude - q.Latitude) * 60;
                    double dLon = (p.Longitude - q.Longitude) * 60 * Math.Cos(p.Latitude * Math.PI / 180);
                    if (dLat * dLat + dLon * dLon <= toleranceNM * toleranceNM)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;
                same.Add(p);
                result.Add(p);
            }
            int removed = Points.Count - result.Count;
            Points = result;
            return removed;
        }

        /// <summary>Takes away everything of the given categories (a new import of a category replaces the old one).</summary>
        public void RemoveCategories(IEnumerable<string> categories)
        {
            HashSet<string> set = new(categories, StringComparer.OrdinalIgnoreCase);
            Lines.RemoveAll(l => set.Contains(l.Category));
            Labels.RemoveAll(l => set.Contains(l.Category));
            Points.RemoveAll(p => set.Contains(p.Category));
            foreach (string c in set) Sources.Remove(c);
        }

        /// <summary>One line of numbers for the summaries: "12 lines (430 points), 9 labels, 0 points".</summary>
        [JsonIgnore]
        public string Summary =>
            $"{Lines.Count} line{(Lines.Count == 1 ? "" : "s")} ({Lines.Sum(l => l.Points.Count)} points), " +
            $"{Labels.Count} label{(Labels.Count == 1 ? "" : "s")}, {Points.Count} point{(Points.Count == 1 ? "" : "s")}";
    }

    /// <summary>A line (open or closed: closed when the last point repeats the first).</summary>
    public sealed class MapLine
    {
        /// <summary>Name of the line or of its group (e.g. the altitude of an area), may be empty.</summary>
        public string Name { get; set; } = "";
        /// <summary>Version 2: the category (see <see cref="MapCategory"/>).</summary>
        public string Category { get; set; } = "";
        /// <summary>The points, each [latitude, longitude].</summary>
        public List<double[]> Points { get; set; } = [];

        /// <summary>Fields of a newer version of the format, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>Never saved: the list of the block "Ground" the line was read from (<see cref="MapGround.AreasList"/> or <see cref="MapGround.LinesList"/>), null: not read from it.</summary>
        [JsonIgnore]
        public string? GroundList { get; set; }
    }

    /// <summary>A text on the map.</summary>
    public sealed class MapLabel
    {
        public const double DefaultSize = 8;
        public string Text { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        /// <summary>Version 2: the category (see <see cref="MapCategory"/>).</summary>
        public string Category { get; set; } = "";
        /// <summary>Size of the text (as in the Aurora map files: 8 is the usual size).</summary>
        public double Size { get; set; } = DefaultSize;

        /// <summary>Fields of a newer version of the format, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>Never saved: made by the importer (the name of an area at its centre), not a text of the file; left out in the ground.</summary>
        [JsonIgnore]
        public bool Auto { get; set; }

        /// <summary>Never saved: read from the block "Ground" (it goes back there).</summary>
        [JsonIgnore]
        public bool InGround { get; set; }
    }

    public enum MapSymbol
    {
        /// <summary>A point (fix, waypoint): a small triangle.</summary>
        Point,
        /// <summary>An airport: a small circle (in the Trainer with a vertical line through it, longer than the circle).</summary>
        Airport,
        /// <summary>A visual reporting point: a small square.</summary>
        Vrp,
        /// <summary>A VOR: a hexagon with a dot.</summary>
        Vor,
        /// <summary>A VOR with TACAN (VORTAC): a hexagon with a dot and a square.</summary>
        Vortac,
        /// <summary>A TACAN alone: a diamond with a dot.</summary>
        Tacan,
        /// <summary>An NDB: a circle with a dot.</summary>
        Ndb
    }

    /// <summary>A named point with its symbol.</summary>
    public sealed class MapPoint
    {
        /// <summary>The text shown beside the point (the label): ICAO of an airport, name of a FIX, code of a VRP...</summary>
        public string Name { get; set; } = "";
        /// <summary>Optional, version 2: the long name (name of an airport, of a VRP), not shown for now.</summary>
        public string Description { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        /// <summary>Version 2: the category (see <see cref="MapCategory"/>).</summary>
        public string Category { get; set; } = "";
        public MapSymbol Symbol { get; set; } = MapSymbol.Point;
        /// <summary>Only while importing (never saved): the block of the source file the point is in (an airport), to choose points by group.</summary>
        [JsonIgnore]
        public string Group { get; set; } = "";

        /// <summary>
        /// Only while importing (never saved): the text when the point goes into the ground labels, when it differs from the
        /// name (KML: "A_C" gives "A", what follows an underscore is not part of the name). Null: the name.
        /// </summary>
        [JsonIgnore]
        public string? LabelText { get; set; }

        /// <summary>Fields of a newer version of the format, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>
    /// The block "Ground" of a file (version 3): the ground layout of the aerodrome. <see cref="Areas"/>: filled areas
    /// (grass, taxiways, aprons, concrete, buildings, runways), drawn in this order; <see cref="Lines"/>: runway outlines,
    /// taxiway centrelines, stop bars; <see cref="Labels"/>: the names of taxiways and aprons. Each item has its category.
    /// </summary>
    public sealed class MapGround
    {
        internal const string AreasList = "Areas";
        internal const string LinesList = "Lines";

        public List<MapLine> Areas { get; set; } = [];
        public List<MapLine> Lines { get; set; } = [];
        public List<MapLabel> Labels { get; set; } = [];

        /// <summary>Fields of a newer version of the format, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
