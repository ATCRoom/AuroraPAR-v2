using System.Globalization;
using ATCRoom.Maps;

// Self-test of the map format and of the importers (ATCRoom.Maps). Run by the GitHub build: any failure gives exit
// code 1. The samples are small invented files.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int failures = 0;
int checks = 0;

void Check(bool ok, string name, string detail = "")
{
    checks++;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
}

bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

double DistanceNM(double lat1, double lon1, double lat2, double lon2)
{
    double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
    double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
    double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
    return 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 3440.065;
}

bool Closed(MapLine line) => line.Points.Count > 2 && line.Points[0][0] == line.Points[^1][0] && line.Points[0][1] == line.Points[^1][1];

// --- Coordinates -------------------------------------------------------------------------------------------------

{
    Check(MapCoordinates.TryParseHemisphere("N047.10.47.653", out double a) && Near(a, 47.1799036, 1e-6), "D.M.S latitude", $"{a:F7}");
    Check(MapCoordinates.TryParseHemisphere("E013.06.38.300", out double b) && Near(b, 13.1106389, 1e-6), "D.M.S longitude", $"{b:F7}");
    Check(MapCoordinates.TryParseHemisphere("N013.32861", out double c) && Near(c, 13.32861, 1e-9), "Decimal degrees", $"{c}");
    Check(MapCoordinates.TryParseHemisphere("S033.30.00.000", out double d) && Near(d, -33.5, 1e-9), "South is negative", $"{d}");
    Check(MapCoordinates.TryParseHemisphere("W000.30.00.000", out double e) && Near(e, -0.5, 1e-9), "West is negative", $"{e}");
    Check(!MapCoordinates.TryParseHemisphere("X12", out _), "Not a coordinate");
    Check(!MapCoordinates.TryParseHemisphere("N091.00.00.000", out _), "Latitude above 90 refused");
    Check(!MapCoordinates.TryParseHemisphere("N045.61.00.000", out _), "Minutes above 59 refused");
    Check(MapCoordinates.TryFindAip("44°16'56\" N 008°39'51\" E", out double lat, out double lon) && Near(lat, 44.2822222, 1e-6) && Near(lon, 8.6641667, 1e-6),
        "AIP coordinates", $"{lat:F6} {lon:F6}");
    Check(MapCoordinates.TryFindAip("44°16’56” N 008°39’51” E", out double lat2, out _) && Near(lat2, 44.2822222, 1e-6), "AIP coordinates with typographic quotes");
    Check(MapCoordinates.ShortAltitude("7000 FT") == "70" && MapCoordinates.ShortAltitude("6000 FT EAST") == "60"
        && MapCoordinates.ShortAltitude("FL90") == "90" && MapCoordinates.ShortAltitude("TRL") == "TRL", "Short altitude labels");
    List<double[]> circle = MapCoordinates.Circle(45, 12, 10);
    Check(circle.Count == 73 && circle.All(p => Near(DistanceNM(45, 12, p[0], p[1]), 10, 0.01)), "Circle of 10 NM", $"{circle.Count} points");
}

// --- Aurora --------------------------------------------------------------------------------------------------------

string[] aurora =
[
    "//TEST",
    "T;A;N045.00.00.000;E012.00.00.000;",
    "T;A;N045.10.00.000;E012.00.00.000;",
    "T;A;N045.10.00.000;E012.10.00.000;",
    "",
    "T;B;N046.00.00.000;E013.00.00.000;",
    "T;B;N046.00.00.000;E013.10.00.000;",
    "T;C;N046.10.00.000;E013.10.00.000;",
    "T;C;N046.20.00.000;E013.10.00.000;",
    "L;FL150;N046.00.00.000;E013.00.00.000;150;8;",
    "something else"
];
{
    Check(MapImport.Detect(aurora) == MapSourceFormat.Aurora, "Aurora file recognised");
    ImportResult r = MapImport.ImportAurora(aurora);
    MapFile m = r.Map;
    Check(m.Lines.Count == 3 && m.Lines[0].Points.Count == 3 && m.Lines[1].Points.Count == 2 && m.Lines[2].Points.Count == 2,
        "Aurora: one line per block and name", string.Join(",", m.Lines.Select(l => $"{l.Name}:{l.Points.Count}")));
    Check(m.Lines[0].Name == "A" && Near(m.Lines[0].Points[1][0], 45 + 10 / 60.0, 1e-6), "Aurora: names and points");
    Check(m.Labels.Count == 1 && m.Labels[0].Text == "150" && m.Labels[0].Size == 8, "Aurora: label text and size");
    Check(r.Notes.Any(n => n.Contains("1 line not understood")), "Aurora: a line not understood is reported", string.Join(" | ", r.Notes));
}

// --- EuroScope .ese ------------------------------------------------------------------------------------------------

string[] ese =
[
    "[INFO]",
    "whatever",
    "[FREETEXT]",
    "N045.00.00.000:E012.00.00.000:Group A:Hello",
    "N045.01.00.000:E012.01.00.000:Plain",
    "; a comment",
    "[AIRSPACE]",
    "SECTORLINE:L1",
    "DISPLAY:X:Y:Z",
    "COORD:N045.00.00.000:E012.00.00.000",
    "COORD:N045.30.00.000:E012.00.00.000",
    "SECTOR:S1:0:9999",
    "OWNER:S1:AA",
    "COORD:N040.00.00.000:E010.00.00.000",
    "CIRCLE_SECTORLINE:C1:N045.00.00.000:E012.00.00.000:10",
    "CIRCLE_SECTORLINE:C2:LIPI:10",
    "MSAW:M1:3000",
    "COORD:N045.00.00.000:E012.00.00.000",
    "COORD:N045.10.00.000:E012.00.00.000",
    "COORD:N045.10.00.000:E012.10.00.000",
    "OFFSET:0.5:0",
    "SECTORLINE:L2",
    "COORD:N045.00.00.000:E012.00.00.000 ; trailing comment",
    "COORD:N045.10.00.000:E012.00.00.000",
    "OFFSET:0:0",
    "[GROUND]",
    "TAXI:A:30",
    "COORD:N045.00.00.000:E012.00.00.000",
    "COORD:N045.00.10.000:E012.00.00.000"
];
{
    Check(MapImport.Detect(ese) == MapSourceFormat.EuroScope, "EuroScope file recognised");
    ImportResult r = MapImport.ImportEuroScope(ese);
    MapFile m = r.Map;
    Check(m.Lines.Count == 4, "EuroScope: sector line, circle, MSAW area, line with offset", string.Join(",", m.Lines.Select(l => $"{l.Name}:{l.Points.Count}")));
    MapLine? l1 = m.Lines.FirstOrDefault(l => l.Name == "L1");
    Check(l1 != null && l1.Points.Count == 2, "EuroScope: DISPLAY does not end the line, SECTOR does", l1 == null ? "missing" : $"{l1.Points.Count} points");
    MapLine? c1 = m.Lines.FirstOrDefault(l => l.Name == "C1");
    Check(c1 != null && Closed(c1) && Near(DistanceNM(45, 12, c1.Points[10][0], c1.Points[10][1]), 10, 0.01), "EuroScope: circle around a coordinate");
    MapLine? m1 = m.Lines.FirstOrDefault(l => l.Name == "M1");
    Check(m1 != null && m1.Points.Count == 4 && Closed(m1), "EuroScope: MSAW area closed", m1 == null ? "missing" : $"{m1.Points.Count} points");
    MapLabel? msaw = m.Labels.FirstOrDefault(l => l.Text == "30");
    Check(msaw != null && Near(msaw.Latitude, 45 + 2 * (10 / 60.0) / 3, 0.01) && Near(msaw.Longitude, 12 + (10 / 60.0) / 3, 0.01), "EuroScope: MSAW altitude label in the area",
        msaw == null ? "missing" : $"{msaw.Latitude:F4} {msaw.Longitude:F4}");
    MapLine? l2 = m.Lines.FirstOrDefault(l => l.Name == "L2");
    Check(l2 != null && Near(l2.Points[0][0], 45.5, 1e-6) && l2.Points.Count == 2, "EuroScope: OFFSET and trailing comment", l2 == null ? "missing" : $"{l2.Points[0][0]}");
    Check(m.Labels.Any(l => l.Text == "Hello") && m.Labels.Any(l => l.Text == "Plain") && m.Labels.Count == 3, "EuroScope: free texts with and without group",
        string.Join(",", m.Labels.Select(l => l.Text)));
    Check(r.Notes.Any(n => n.Contains("1 circle")), "EuroScope: circle around a named point reported", string.Join(" | ", r.Notes));
    ImportResult only = MapImport.ImportEuroScope(ese, new EseOptions { SectorLines = false, FreeText = false, MsawAreas = true });
    Check(only.Map.Lines.Count == 1 && only.Map.Labels.Count == 1, "EuroScope: only the MSAW areas when asked");
}

// --- AIP table -----------------------------------------------------------------------------------------------------

{
    List<PdfWord> words = [];
    void Row(double y, double x, params string[] texts)
    {
        foreach (string t in texts)
        {
            words.Add(new PdfWord(1, x, y, t));
            x += 8 * t.Length + 4;
        }
    }
    Row(60, 100, "MINIMUM", "ALTITUDES/LEVELS");
    Row(100, 300, "Line", "joining", "following", "points:");
    Row(110, 300, "44°00'00\"", "N", "008°00'00\"", "E");
    Row(120, 300, "44°10'00\"", "N", "008°00'00\"", "E");
    Row(121, 100, "2000", "FT");
    Row(130, 300, "44°10'00\"", "N", "008°10'00\"", "E");
    Row(140, 300, "44°00'00\"", "N", "008°00'00\"", "E");
    Row(150, 300, "Line", "joining", "following", "points:");
    Row(160, 300, "44°20'00\"", "N", "008°00'00\"", "E");
    Row(170, 300, "then", "arc", "of", "circle", "in", "clockwise", "direction", "radius", "10", "NM", "centred", "on:");
    Row(180, 300, "44°20'00\"", "N", "008°10'00\"", "E");
    Row(185, 100, "FL90");
    Row(190, 300, "till", "point");
    Row(200, 300, "44°30'00\"", "N", "008°10'00\"", "E");
    Row(210, 300, "44°20'00\"", "N", "008°00'00\"", "E");
    ImportResult r = MapImport.ImportAipTable(words);
    MapFile m = r.Map;
    Check(m.Lines.Count == 2 && m.Lines.All(Closed), "AIP: two closed areas", string.Join(",", m.Lines.Select(l => $"{l.Name}:{l.Points.Count}")));
    Check(m.Lines.Count == 2 && m.Lines[0].Name == "2000 FT" && m.Lines[1].Name == "FL90", "AIP: altitude of the left column to its area");
    Check(m.Labels.Select(l => l.Text).SequenceEqual(["20", "90"]), "AIP: short altitude labels", string.Join(",", m.Labels.Select(l => l.Text)));
    if (m.Lines.Count == 2)
    {
        MapLine area = m.Lines[1];
        List<double[]> onArc = area.Points.Skip(1).Take(area.Points.Count - 3).ToList();
        double centreLat = 44 + 20 / 60.0, centreLon = 8 + 10 / 60.0;
        Check(area.Points.Count > 30 && onArc.All(p => Near(DistanceNM(centreLat, centreLon, p[0], p[1]), 10, 0.01)), "AIP: arc points at the radius from the centre",
            $"{area.Points.Count} points");
        double[] firstOnArc = onArc[0];
        Check(firstOnArc[0] > centreLat, "AIP: clockwise arc from west to north goes through the north-west", $"{firstOnArc[0]:F4}");
    }
    Check(MapImport.ImportAipTable([new PdfWord(1, 10, 10, "Chart")]).Map.Lines.Count == 0, "AIP: a PDF without the table gives no area");
}

// --- File ----------------------------------------------------------------------------------------------------------

{
    string folder = Path.Combine(Path.GetTempPath(), "atcroom-maps-selftest-" + Guid.NewGuid().ToString("N"));
    string path = Path.Combine(folder, "test.json");
    MapFile map = MapImport.ImportAurora(aurora).Map;
    map.Name = "Test map";
    map.LineColour = "#abcdef";
    map.LabelColour = "nonsense";
    map.Points.Add(new MapPoint { Name = "RIV", Latitude = 46, Longitude = 13, Symbol = MapSymbol.Point });
    map.Save(path);
    MapFile back = MapFile.Load(path);
    Check(back.Name == "Test map" && back.Lines.Count == 3 && back.Labels.Count == 1 && back.Points.Count == 1, "File: saved and read back", back.Summary);
    Check(back.LineColour == "#ABCDEF" && back.LabelColour == "#ABCDEF", "File: colours normalised (label colour falls back to the line colour)",
        $"{back.LineColour} {back.LabelColour}");
    Check(File.ReadAllText(path).Contains("\"Symbol\": \"Point\""), "File: symbols written as text");
    File.WriteAllText(path, "{ \"Lines\": [ { \"Name\": \"x\", \"Points\": [ [45, 12], [95, 12], [45.5, 12] ] } ], // comment\n }");
    MapFile hand = MapFile.Load(path);
    Check(hand.Lines.Count == 1 && hand.Lines[0].Points.Count == 2 && hand.Name == "test", "File: hand-written file repaired (bad point dropped, name from the file)");
    File.WriteAllText(path, "not json");
    bool refused = false;
    try { MapFile.Load(path); } catch (InvalidDataException) { refused = true; }
    Check(refused, "File: not a map, refused with a message");
    try { Directory.Delete(folder, true); } catch (Exception) { }
}

Console.WriteLine();
Console.WriteLine($"{checks - failures} of {checks} checks passed.");
return failures == 0 ? 0 : 1;
