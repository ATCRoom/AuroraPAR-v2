using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATCRoom.Maps
{
    /// <summary>
    /// A radar map in the open ATCRoom format (JSON, see docs/MAP-FORMAT.md): lines, labels and points, with the
    /// colours of its lines and labels. Written by ATCRoom Map Creator, read by AuroraPAR v2 Trainer. Coordinates are
    /// latitude and longitude in degrees (WGS 84, north and east positive).
    /// </summary>
    public sealed class MapFile
    {
        public const int CurrentFormatVersion = 1;
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
        /// <summary>Optional: where the data come from, who made the map, its licence (free text).</summary>
        public string Source { get; set; } = "";
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
            Source = (Source ?? "").Trim();
            LineColour = NormalColour(LineColour, DefaultColour);
            LabelColour = NormalColour(LabelColour, LineColour);
            Lines = (Lines ?? []).Where(l => l != null).ToList();
            foreach (MapLine line in Lines)
            {
                line.Name = (line.Name ?? "").Trim();
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
                label.Latitude = Math.Round(label.Latitude, 6);
                label.Longitude = Math.Round(label.Longitude, 6);
                if (!double.IsFinite(label.Size) || label.Size <= 0) label.Size = MapLabel.DefaultSize;
                label.Size = Math.Clamp(label.Size, 4, 40);
            }
            Points = (Points ?? []).Where(p => p != null && MapCoordinates.IsValid(p.Latitude, p.Longitude)).ToList();
            foreach (MapPoint point in Points)
            {
                point.Name = (point.Name ?? "").Trim();
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
        /// <summary>Size of the text (as in the Aurora map files: 8 is the usual size).</summary>
        public double Size { get; set; } = DefaultSize;
    }

    public enum MapSymbol
    {
        /// <summary>A point (fix, waypoint): a small triangle.</summary>
        Point,
        /// <summary>An airport: a small circle.</summary>
        Airport
    }

    /// <summary>A named point with its symbol.</summary>
    public sealed class MapPoint
    {
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public MapSymbol Symbol { get; set; } = MapSymbol.Point;
    }
}
