using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATCRoom.Maps
{
    /// <summary>
    /// A radar map in the open ATCRoom format (JSON, see docs/MAP-FORMAT.md): lines, labels and points, each with its
    /// category (version 2: one file per airport, see <see cref="MapCategory"/>), or a whole map with the colours of its
    /// lines and labels (version 1). Written by ATCRoom Map Creator, read by AuroraPAR v2 Trainer. Coordinates are
    /// latitude and longitude in degrees (WGS 84, north and east positive).
    /// </summary>
    public sealed class MapFile
    {
        public const int CurrentFormatVersion = 2;
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

        /// <summary>Fields of a newer version of the format, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>Reads a map file. Throws (with a readable message) when it is not a map.</summary>
        public static MapFile Load(string path)
        {
            MapFile? map;
            try
            {
                map = JsonSerializer.Deserialize<MapFile>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Not a map file ({ex.Message}).", ex);
            }
            if (map == null) throw new InvalidDataException("Empty map file.");
            map.Normalize();
            if (map.Name.Length == 0) map.Name = Path.GetFileNameWithoutExtension(path);
            return map;
        }

        public void Save(string path)
        {
            Normalize();
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }

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
                line.Category = MapCategory.Normal(line.Category) ?? "";
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
                label.Category = MapCategory.Normal(label.Category) ?? "";
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
                point.Category = MapCategory.Normal(point.Category) ?? "";
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

        /// <summary>Adds the items of another map (read from a file, without categories) as one category.</summary>
        public void AddAs(MapFile source, string category)
        {
            foreach (MapLine l in source.Lines) Lines.Add(new MapLine { Name = l.Name, Category = category, Points = l.Points.Select(p => new[] { p[0], p[1] }).ToList() });
            foreach (MapLabel l in source.Labels) Labels.Add(new MapLabel { Text = l.Text, Latitude = l.Latitude, Longitude = l.Longitude, Size = l.Size, Category = category });
            foreach (MapPoint p in source.Points) Points.Add(new MapPoint { Name = p.Name, Description = p.Description, Latitude = p.Latitude, Longitude = p.Longitude, Symbol = p.Symbol, Category = category });
        }

        /// <summary>Adds everything of another map of version 2 (items with their categories, and the sources).</summary>
        public void Merge(MapFile other)
        {
            Lines.AddRange(other.Lines);
            Labels.AddRange(other.Labels);
            Points.AddRange(other.Points);
            foreach (KeyValuePair<string, string> pair in other.Sources) Sources[pair.Key] = pair.Value;
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
    }

    public enum MapSymbol
    {
        /// <summary>A point (fix, waypoint): a small triangle.</summary>
        Point,
        /// <summary>An airport: a small circle.</summary>
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
    }
}
