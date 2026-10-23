using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>
    /// A shape of a custom symbol, in pixels from the point (x to the right, y down; about -5 to 5): a "line" (a list of points),
    /// a "polygon" (closed; "fill": true fills it) or a "circle" ([x, y, radius]; "fill" too).
    /// </summary>
    internal sealed class CustomSymbolShape
    {
        [JsonPropertyName("line")] public double[][]? Line { get; set; }
        [JsonPropertyName("polygon")] public double[][]? Polygon { get; set; }
        [JsonPropertyName("circle")] public double[]? Circle { get; set; }
        [JsonPropertyName("fill")] public bool Fill { get; set; }
    }

    internal sealed class CustomSymbol
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("shapes")] public List<CustomSymbolShape> Shapes { get; set; } = [];
    }

    internal sealed class SymbolsFile
    {
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("symbols")] public List<CustomSymbol> Symbols { get; set; } = [];
    }

    /// <summary>
    /// The symbols the user can add to the built-in ones: the file Symbols.json in the Maps folder (made with a few examples when it does
    /// not exist, read at start and by Reload). A symbol is a name and shapes; a new symbol is a new entry of the file.
    /// </summary>
    internal static class CustomSymbols
    {
        public static string FilePath => System.IO.Path.Combine(MapLibrary.Folder, "Symbols.json");

        public static List<CustomSymbol> All { get; private set; } = [];

        /// <summary>The error of the last reading (empty: none).</summary>
        public static string Error { get; private set; } = "";

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        public static CustomSymbol? Find(string? name) =>
            string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public static void Load()
        {
            Error = "";
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(MapLibrary.Folder);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(Defaults(), Options));
                }
                SymbolsFile? file = JsonSerializer.Deserialize<SymbolsFile>(File.ReadAllText(FilePath), Options);
                // A name that is one of the built-in symbols would hide it: not accepted.
                All = (file?.Symbols ?? []).Where(s => s.Name.Trim().Length > 0 && !Enum.TryParse<MapSymbol>(s.Name.Trim(), true, out _)).ToList();
            }
            catch (Exception ex)
            {
                All = [];
                Error = $"Symbols.json: {ex.Message}";
            }
        }

        public static void Draw(DrawingContext dc, CustomSymbol symbol, Point p, Pen pen)
        {
            foreach (CustomSymbolShape shape in symbol.Shapes)
            {
                if (shape.Circle is { Length: >= 3 } c)
                {
                    dc.DrawEllipse(shape.Fill ? pen.Brush : null, pen, new Point(p.X + c[0], p.Y + c[1]), c[2], c[2]);
                    continue;
                }
                double[][]? points = shape.Polygon ?? shape.Line;
                if (points == null || points.Length < 2 || points.Any(q => q.Length < 2)) continue;
                StreamGeometry geometry = new();
                using (StreamGeometryContext g = geometry.Open())
                {
                    g.BeginFigure(new Point(p.X + points[0][0], p.Y + points[0][1]), shape.Polygon != null && shape.Fill, shape.Polygon != null);
                    g.PolyLineTo(points.Skip(1).Select(q => new Point(p.X + q[0], p.Y + q[1])).ToList(), true, false);
                }
                geometry.Freeze();
                dc.DrawGeometry(shape.Polygon != null && shape.Fill ? pen.Brush : null, pen, geometry);
            }
        }

        private static SymbolsFile Defaults()
        {
            static double[][] P(params double[][] points) => points;
            return new SymbolsFile
            {
                Description = "Symbols for the points of the maps. Each symbol: a name and shapes drawn in pixels from the point (x right, y down, about -5 to 5). Shapes: \"line\": [[x,y],...], \"polygon\": [[x,y],...] (closed), \"circle\": [x,y,radius]; \"fill\": true fills a polygon or a circle. Add a symbol = add an entry, then Reload in RDR > Maps.",
                Symbols =
                [
                    new CustomSymbol { Name = "Circle", Shapes = [new CustomSymbolShape { Circle = [0, 0, 4] }] },
                    new CustomSymbol { Name = "Plus", Shapes = [new CustomSymbolShape { Line = P([-4, 0], [4, 0]) }, new CustomSymbolShape { Line = P([0, -4], [0, 4]) }] },
                    new CustomSymbol { Name = "Cross", Shapes = [new CustomSymbolShape { Line = P([-3.5, -3.5], [3.5, 3.5]) }, new CustomSymbolShape { Line = P([-3.5, 3.5], [3.5, -3.5]) }] },
                    new CustomSymbol { Name = "Triangle filled", Shapes = [new CustomSymbolShape { Polygon = P([0, -4.5], [4, 3], [-4, 3]), Fill = true }] },
                    new CustomSymbol { Name = "Square filled", Shapes = [new CustomSymbolShape { Polygon = P([-3.5, -3.5], [3.5, -3.5], [3.5, 3.5], [-3.5, 3.5]), Fill = true }] },
                    new CustomSymbol { Name = "Triangle down", Shapes = [new CustomSymbolShape { Polygon = P([0, 4.5], [4, -3], [-4, -3]) }] },
                    new CustomSymbol { Name = "Triangle down filled", Shapes = [new CustomSymbolShape { Polygon = P([0, 4.5], [4, -3], [-4, -3]), Fill = true }] }
                ]
            };
        }
    }
}
