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

// --- IVAO files (.geo .fix .vor .ndb .ap .vfi .lartcc .str) ----------------------------------------------------------

{
    Check(MapCoordinates.TryParseAny("N0434857348", out double c1) && Near(c1, 43 + 48 / 60.0 + 57.348 / 3600, 1e-6), "Compact coordinate without dots", $"{c1:F6}");
    Check(MapCoordinates.TryParseAny("45.5", out double c2) && Near(c2, 45.5, 1e-9), "Plain decimal degrees");

    // .geo: segments that continue are joined; the tag COAST is suggested as boundaries
    FileImport geo = MapImport.Read("itgeo.geo",
    [
        "// Coast A",
        "N0440000000;E0080000000;N0441000000;E0080000000;COAST;",
        "N0441000000;E0080000000;N0441000000;E0081000000;COAST;",
        "",
        "N0450000000;E0090000000;N0451000000;E0090000000;LAKE;",
        "garbage"
    ], null);
    ImportEntry? coast = geo.Entries.FirstOrDefault(e => e.Title.StartsWith("Coast"));
    Check(coast != null && coast.Content.Lines.Count == 1 && coast.Content.Lines[0].Points.Count == 3 && coast.Suggested == MapCategory.Boundaries,
        ".geo: segments joined, COAST suggested as boundaries", coast == null ? "missing" : coast.Content.Summary);
    Check(geo.Entries.Count == 2 && geo.Entries.Any(e => e.Title == "LAKE" && e.Suggested == null), ".geo: other tag separate and not suggested");
    Check(geo.Notes.Any(n => n.Contains("not understood")), ".geo: bad row reported", string.Join(" | ", geo.Notes));

    // .fix: terminal and both are FIX, en-route apart, hidden (type 3) a third part, comments name the groups
    FileImport fix = MapImport.Read("itfix.fix",
    [
        "//LIBC;",
        "ABC;N045.00.00.000;E012.00.00.000;1;0;",
        "DEF;N045.10.00.000;E012.00.00.000;2;0;HLD-DEF;",
        "GHI;N045.20.00.000;E012.00.00.000;0;0;",
        "//<!--LIPX-->",
        "JKL;N045.30.00.000;E012.00.00.000;3;",
        "MNO;N045.40.00.000;E012.00.00.000;1;0; // comment",
        "PQR;N045.50.00.000;E012.00.00.000;3:",
        "STU;N0455100000;E0120000000;3",
        "TAC-06R;45.52;12.0;3;"
    ], null);
    Check(fix.Entries.Count == 3 && fix.Entries[0].Content.Points.Count == 3 && fix.Entries[0].Suggested == MapCategory.Fix
        && fix.Entries[1].Content.Points.Count == 1 && fix.Entries[1].Suggested == null
        && fix.Entries[2].Content.Points.Count == 4 && fix.Entries[2].Suggested == MapCategory.Fix, ".fix: terminal + both, en-route apart, hidden (type 3) a third part, typo 3: and no ; accepted",
        string.Join(",", fix.Entries.Select(e => e.Content.Points.Count)));
    Check(fix.Entries[0].Content.Points[0].Group == "LIBC" && fix.Entries[2].Content.Points[0].Group == "LIPX", ".fix: the comment before a block is the group of its points",
        string.Join(",", fix.Entries.SelectMany(e => e.Content.Points).Select(p => p.Name + ":" + p.Group)));
    ImportEntry chosen = fix.Entries[2];
    chosen.Excluded.Add(chosen.Content.Points[0]);
    Check(chosen.Selected().Points.Count == 3 && chosen.Content.Points.Count == 4, "Choose points: a point switched off is left out of the selection");
    MapPoint renamed = chosen.Content.Points[1];
    string oldName = renamed.Name;
    chosen.OriginalNames[renamed] = oldName;
    renamed.Name = "NEWNAME";
    MapFile asMap = new();
    asMap.AddAs(chosen.Selected(), MapCategory.Fix);
    Check(asMap.Points.Any(p => p.Name == "NEWNAME") && chosen.OriginalNames[renamed] == oldName, "Choose points: a renamed point goes into the map with its new name and keeps the old one");

    // .vor and .ndb: symbols, hidden skipped, impossible seconds skipped and named
    FileImport vor = MapImport.Read("itvor.vor",
    [
        "AAA;112.100;N045.00.00.000;E012.00.00.000;0;",
        "BBB;113.200;N045.10.00.000;E012.00.00.000;0;2;45X;",
        "CCC;114.300;N045.20.00.000;E012.00.00.000;0;3;50Y;",
        "DDD;115.400;N045.30.00.000;E012.00.00.000;1;",
        "KPT;116.500;N045.40.75.000;E012.00.00.000;0;"
    ], null);
    List<MapPoint> vp = vor.Entries[0].Content.Points;
    Check(vp.Count == 3 && vp[0].Symbol == MapSymbol.Vor && vp[1].Symbol == MapSymbol.Vortac && vp[2].Symbol == MapSymbol.Tacan && vor.Entries[0].Suggested == MapCategory.Navaids,
        ".vor: VOR, VORTAC, TACAN symbols; hidden and bad skipped", string.Join(",", vp.Select(p => p.Name + ":" + p.Symbol)));
    Check(vor.Notes.Any(n => n.Contains("KPT")), ".vor: bad coordinates named in the notes", string.Join(" | ", vor.Notes));
    FileImport ndb = MapImport.Read("itndb.ndb", ["NDB1;350;N045.00.00.000;E012.00.00.000;", "NDB2;360;N045.10.00.000;E012.00.00.000;1;"], null);
    Check(ndb.Entries[0].Content.Points.Count == 1 && ndb.Entries[0].Content.Points[0].Symbol == MapSymbol.Ndb, ".ndb: one NDB, hidden skipped");

    // .ap
    FileImport ap = MapImport.Read("itap.ap",
    [
        "LIPX;100;5000;N045.00.00.000;E012.00.00.000;VERONA;",
        "LIZZ;0;5000;N045.10.00.000;E012.00.00.000;AIR DEFENCE;",
        "LIPY;10;5000;N045.20.00.000;E012.00.00.000;ANCONA;1;"
    ], null);
    Check(ap.Entries[0].Content.Points.Count == 1 && ap.Entries[0].Content.Points[0].Name == "LIPX" && ap.Entries[0].Content.Points[0].Description == "VERONA"
        && ap.Entries[0].Suggested == MapCategory.Airports, ".ap: ICAO label, name as description, ZZ and hidden skipped", ap.Entries[0].Content.Summary);

    // .vfi (airport from the file name)
    FileImport vrp = MapImport.Read("libc.vfi", ["PONTE ROSSO;ROS;N045.00.00.000;E012.00.00.000;", "LAGO;1500;N045.10.00.000;E012.00.00.000;"], null);
    Check(vrp.Entries[0].Content.Points.Count == 2 && vrp.Entries[0].Content.Points[0].Name == "ROS" && vrp.Entries[0].Content.Points[0].Description == "PONTE ROSSO"
        && vrp.Entries[0].Content.Points[1].Name == "LAGO" && vrp.Airport == "LIBC", ".vfi: code as label, altitude form, airport from the name");

    // .vfi with the same prefix on every row and a row with one name (lipi.vfi): both blocks, nothing skipped
    FileImport lipi = MapImport.Read("lipi.vfi",
    [
        "SIERRA;PI;N045.47.20.000;E012.47.20.000;",
        "PI-NOVEMBER;N046.05.42.000;E013.03.33.000;",
        "A;PI;N045.57.17.000;E012.59.22.000;",
        "B;PI;N045.59.44.620;E013.05.18.200;"
    ], null);
    ImportEntry lipiEntry = lipi.Entries[0];
    string LipiNames() => string.Join(",", lipiEntry.Content.Points.Select(p => p.Name));
    Check(lipiEntry.Content.Points.Count == 4 && lipiEntry.LabelFrom == LabelFrom.Both && LipiNames() == "PI-SIERRA,PI-NOVEMBER,PI-A,PI-B" && lipi.Notes.Count == 0,
        ".vfi: a repeated second block gives both (PI-SIERRA), a row with one name is kept", LipiNames());
    lipiEntry.SetLabelFrom(LabelFrom.First);
    Check(LipiNames() == "SIERRA,PI-NOVEMBER,A,B" && lipiEntry.Content.Points[0].Description == "PI", ".vfi: label from the first block", LipiNames());
    lipiEntry.SetLabelFrom(LabelFrom.Second);
    Check(LipiNames() == "PI,PI-NOVEMBER,PI,PI" && lipiEntry.Content.Points[0].Description == "SIERRA", ".vfi: label from the second block", LipiNames());

    // AIP lists (PDF): VRP of ENR 6.3.1 (name first in the first list, code first in the second), significant points of ENR 4.4
    FileImport aipVrp = MapImport.ImportAipVrp("LI-ENR_6.3.1.pdf",
    [
        "          Geographical Name         Code              Coordinates                        Radio AID or next visual REP",
        "    1NM NORTH SALERNO TOWN                      RINW1             40°41'47''N       014°47'06''E   FROM SOR VOR: 069°/22NM",
        "                TODI                       RZSW1       42°46'56''N       012°24'57''E   FROM PRU VOR: 191°/20NM",
        "                                                                                                   TO Orvieto Scalo: 027°/7NM",
        "       Code         Geographical Name          Coordinates                        Radio AID or next visual REP",
        "      RZSW1                    TODI      42°46'56''N       012°24'57''E   FROM PRU VOR: 191°/20NM",
        "      RZNE                 VALFABBRICA      43°09'33''N       012°36'02''E   FROM PRU VOR: 046°/5NM"
    ]);
    List<MapPoint> vrpPoints = aipVrp.Entries.Count == 1 ? aipVrp.Entries[0].Content.Points : [];
    Check(vrpPoints.Count == 3 && vrpPoints[0].Name == "RINW1" && vrpPoints[0].Description == "1NM NORTH SALERNO TOWN" && vrpPoints[0].Symbol == MapSymbol.Vrp
        && vrpPoints[2].Name == "RZNE" && vrpPoints[2].Description == "VALFABBRICA" && Near(vrpPoints[0].Latitude, 40.6963889, 1e-6) && Near(vrpPoints[0].Longitude, 14.785, 1e-6)
        && aipVrp.Entries[0].Suggested == MapCategory.Vrp, "AIP ENR 6.3.1: both lists, a point listed twice kept once", string.Join(",", vrpPoints.Select(p => p.Name)));
    FileImport aipFix = MapImport.ImportAipFixes("LI-ENR_4.4.pdf",
    [
        "    CODICE ICAO               COORDINATE                     ROTTE INTERESSATE",
        "          ABADI        40°45'19''N       018°38'30''E   P92                           BR star, BR sid                          FRA(I)",
        "          ABESI        46°09'35''N       009°02'34''E   N851                          MF sid, MZ sid                          FRA(X):",
        "                                                                                                                           FRA(D): LIMC,",
        "          ABADI        40°45'19''N       018°38'30''E   P92"
    ]);
    List<MapPoint> fixPoints = aipFix.Entries.Count == 1 ? aipFix.Entries[0].Content.Points : [];
    Check(fixPoints.Count == 2 && fixPoints[1].Name == "ABESI" && Near(fixPoints[1].Longitude, 9.0427778, 1e-6) && aipFix.Entries[0].Suggested == MapCategory.Fix,
        "AIP ENR 4.4: a point per code, as FIX", string.Join(",", fixPoints.Select(p => p.Name)));

    // AIP radio aids (ENR 4.1.1): the cells are read by the place of the words (a table made by hand: header ID, column numbers, two aids)
    PdfWord W(double x, double y, string text) => new(1, x, y, text, x + text.Length * 5);
    List<PdfWord> navWords =
    [
        W(60, 40, "ENR"), W(90, 40, "4.1.1"), W(120, 160, "ID"), W(120, 247, "9"),
        W(224, 340, "VOR"), W(142, 352, "113.80"), W(211, 352, "40°37'41''N"), W(65, 358, "ALGHERO"), W(209, 364, "008°14'38''E"),
        W(68, 370, "VORTAC"), W(119, 370, "ALG"), W(58, 382, "(1°"), W(220, 376, "TACAN"), W(211, 388, "40°37'42''N"), W(209, 400, "008°14'38''E"),
        W(211, 516, "43°35'11''N"), W(67, 522, "GIOIA"), W(96, 522, "DEL"), W(209, 528, "013°28'16''E"), W(65, 534, "COLLE"), W(119, 534, "ANC"),
        W(65, 546, "VOR/DME"), W(211, 552, "43°35'11''N"), W(209, 564, "013°28'17''E"),
        W(211, 700, "45°55'28''N"), W(67, 700, "AVIANO"), W(209, 712, "012°25'43''E"), W(65, 712, "NDB"), W(119, 712, "AVI"), W(146, 712, "KHZ")
    ];
    FileImport aipNav = MapImport.Read("LI-ENR_4.1.1.pdf", null, navWords);
    List<MapPoint> navPoints = aipNav.Entries.Count == 1 ? aipNav.Entries[0].Content.Points : [];
    Check(navPoints.Count == 3 && navPoints[0].Name == "ALG" && navPoints[0].Symbol == MapSymbol.Vortac && navPoints[0].Description == "ALGHERO VORTAC"
        && Near(navPoints[0].Latitude, 40.6280556, 1e-6) && Near(navPoints[0].Longitude, 8.2438889, 1e-6)
        && navPoints[1].Name == "ANC" && navPoints[1].Symbol == MapSymbol.Vor && navPoints[1].Description == "GIOIA DEL COLLE VOR/DME"
        && navPoints[2].Name == "AVI" && navPoints[2].Symbol == MapSymbol.Ndb && aipNav.Entries[0].Suggested == MapCategory.Navaids,
        "AIP ENR 4.1.1: aids by the place of the words (ID, name over several lines, type, first coordinate pair)", string.Join(",", navPoints.Select(p => p.Name + ":" + p.Symbol)));

    // Arc of the AIP sent the long way round (Napoli, area 6000 SOUTH-WEST): 326° anti-clockwise from the first point to the second is turned
    double[] napoli = [40.8841667, 14.2944444], napoliP1 = [40.4244444, 13.9383333], napoliP2 = [40.6533333, 13.6613889];
    List<double[]> longArc = MapCoordinates.ArcChecked(napoli, 32, napoliP1, napoliP2, clockwise: false, out bool turned1);
    List<double[]> shortArc = MapCoordinates.ArcChecked(napoli, 32, napoliP2, napoliP1, clockwise: false, out bool turned2);
    double MiddleBearing(List<double[]> arc) => MapCoordinates.Bearing(napoli[0], napoli[1], arc[arc.Count / 2][0], arc[arc.Count / 2][1]);
    Check(turned1 && !turned2 && longArc.Count > 10 && longArc.Count < 40 && Near(MiddleBearing(longArc), 227, 3) && Near(MiddleBearing(shortArc), 227, 3),
        "AIP arc: more than 300° between two close points is turned the other way; the right arc is left as it is", $"{longArc.Count} points, middle {MiddleBearing(longArc):F0}°");
    MapCoordinates.ArcChecked(napoli, 32, [40.8841667, 14.8], [40.8841667, 13.8], clockwise: true, out bool turned3);
    Check(!turned3, "AIP arc: a half circle is not touched");

    // lartcc: one part per ID, the name suggests the category
    FileImport art = MapImport.Read("libb_cs0_app.lartcc",
    [
        "T;LIBB CTR;N045.00.00.000;E012.00.00.000;",
        "T;LIBB CTR;N045.10.00.000;E012.00.00.000;",
        "T;LIBB CTR;N045.10.00.000;E012.10.00.000;",
        "T;LIBB CTR;N045.00.00.000;E012.00.00.000;",
        "",
        "T;SECTOR 1;N046.00.00.000;E013.00.00.000;",
        "T;SECTOR 1;N046.10.00.000;E013.00.00.000;",
        "T;SECTOR 1;N046.10.00.000;E013.10.00.000;"
    ], null);
    Check(art.Entries.Count == 2 && art.Entries[0].Suggested == MapCategory.Ctr && art.Entries[1].Suggested == null
        && art.Entries[0].Content.Labels.Count == 1, "lartcc: one part per ID, CTR suggested by name, label at the centre");

    // .str: only MAPS groups, CTR + ATZ by name, procedures skipped, inline comments ignored
    FileImport str = MapImport.Read("libf.str",
    [
        "LIBF;MAPS;CTR LIBF; ; ;1;",
        "N045.00.00.000;E012.00.00.000; // start",
        "N045.10.00.000;E012.00.00.000;",
        "N045.10.00.000;E012.10.00.000;",
        "N045.00.00.000;E012.00.00.000;",
        "N046.00.00.000;E013.00.00.000;<br>",
        "N046.10.00.000;E013.00.00.000;",
        "N046.10.00.000;E013.10.00.000;",
        "N046.00.00.000;E013.00.00.000;",
        "LIBF;MAPS;ATZ LIBF; ; ;5;",
        "45.0;12.0;",
        "45.05;12.0;",
        "45.05;12.05;",
        "45.0;12.0;",
        "LIBF;MAPS;STAR LOW 29R; ; ;3;",
        "ABC;ABC;",
        "DEF;DEF;",
        "LIBF;STAR;SOMETHING; ; ;3;",
        "N045.00.00.000;E012.00.00.000;"
    ], null);
    ImportEntry? ctr = str.Entries.FirstOrDefault(e => e.Title == "CTR LIBF");
    Check(ctr != null && ctr.Content.Lines.Count == 2 && ctr.Suggested == MapCategory.Ctr, ".str: CTR group, two zones from <br>", ctr == null ? "missing" : ctr.Content.Summary);
    ImportEntry? atz = str.Entries.FirstOrDefault(e => e.Title == "ATZ LIBF");
    Check(atz != null && atz.Content.Lines.Count == 1 && atz.Suggested == MapCategory.Atz, ".str: ATZ group with decimal coordinates");
    Check(str.Entries.Count == 2 && str.Airport == "LIBF", ".str: STAR group (names of points) and non-MAPS groups skipped, airport from the header",
        string.Join(",", str.Entries.Select(e => e.Title)));

    // unknown
    Check(MapImport.Read("x.txt", ["hello"], null).Entries.Count == 0, "Unknown file gives no part");
}

// --- Categories, cut, file version 2 ---------------------------------------------------------------------------------

{
    MapFile map = new() { Airport = "lipx" };
    FileImport fixes = MapImport.Read("a.fix", ["NEAR;N045.00.00.000;E012.00.00.000;1;0;", "FAR;N047.00.00.000;E012.00.00.000;1;0;"], null);
    map.AddAs(fixes.Entries[0].Content, MapCategory.Fix);
    map.AddAs(new MapFile { Lines = [new MapLine { Name = "L", Points = [[44.9, 12.0], [45.1, 12.0], [47.0, 12.0]] }], Labels = [new MapLabel { Text = "T", Latitude = 45.0, Longitude = 12.01 }] }, MapCategory.Mva);
    map.Sources[MapCategory.Fix] = "test";
    map.Normalize();
    Check(map.UsedCategories().SequenceEqual([MapCategory.Mva, MapCategory.Fix]) || map.UsedCategories().ToHashSet().SetEquals([MapCategory.Mva, MapCategory.Fix]),
        "Categories used", string.Join(",", map.UsedCategories()));
    MapFile cut = MapCut.CutCircle(map, 45.0, 12.0, 10);
    Check(cut.Points.Count == 1 && cut.Points[0].Name == "NEAR" && cut.Labels.Count == 1, "Cut: points and labels outside dropped");
    Check(cut.Lines.Count == 1 && cut.Lines[0].Points.Count >= 2 && cut.Lines[0].Points.All(p => DistanceNM(45.0, 12.0, p[0], p[1]) <= 10.05) && cut.Lines[0].Category == MapCategory.Mva,
        "Cut: line clipped at the circle, category kept", cut.Summary);
    Check(cut.Airport == "LIPX", "Airport in capitals");

    // A map made before is loaded as parts (one per category) and can get a detail added
    MapFile before = new() { Airport = "LIPX", Source = "old" };
    before.AddAs(new MapFile { Points = [new MapPoint { Name = "AAA", Latitude = 45, Longitude = 12 }] }, MapCategory.Fix);
    before.AddAs(new MapFile { Lines = [new MapLine { Points = [[45, 12], [45.1, 12]] }] }, MapCategory.Mva);
    before.Sources[MapCategory.Fix] = "IVAO";
    FileImport again = MapImport.Read("LIPX.json", System.Text.Json.JsonSerializer.Serialize(before, MapFile.JsonOptions).Split('\n'), null);
    Check(again.Entries.Count == 2 && again.IsExistingMap && again.Airport == "LIPX" && again.Entries.All(e => e.Category == e.Suggested && e.Category != null), "Existing map: one part per category, category and airport kept",
        string.Join(",", again.Entries.Select(e => e.Title + ":" + e.Category)));
    Check(again.Entries.First(e => e.Category == MapCategory.Fix).Source == "IVAO" && again.Entries.First(e => e.Category == MapCategory.Mva).Source == "old", "Existing map: the sources are kept (the one of the category, else the general one)");
    MapFile sum = new();
    foreach (ImportEntry e in again.Entries) sum.AddAs(e.Content, e.Category!);
    sum.AddAs(new MapFile { Points = [new MapPoint { Name = "BBB", Latitude = 45.2, Longitude = 12 }, new MapPoint { Name = "aaa", Latitude = 45.0001, Longitude = 12 }, new MapPoint { Name = "AAA", Latitude = 45.5, Longitude = 12 }] }, MapCategory.Fix);
    int dropped = sum.RemoveDuplicatePoints();
    Check(dropped == 1 && sum.Points.Count == 3 && sum.Points.Any(p => p.Name == "BBB"), "Duplicates: the same name and category closer than 0.1 NM counts once, a far one stays", $"{dropped} dropped, {sum.Points.Count} left");
    Check(MapImport.Read("bad.json", ["not json"], null).Entries.Count == 0, "Existing map: a file that is not a map gives no part");
    FileImport old1 = MapImport.Read("v1.json", ["{ \"Name\": \"x\", \"Lines\": [ { \"Points\": [[45,12],[45.1,12]] } ] }"], null);
    Check(old1.Entries.Count == 1 && old1.Entries[0].Category == null, "Existing map of version 1: one part without a category");

    string folder = Path.Combine(Path.GetTempPath(), "atcroom-maps-selftest2-" + Guid.NewGuid().ToString("N"));
    string path = Path.Combine(folder, "LIPX.json");
    cut.Sources[MapCategory.Fix] = "test";
    cut.Save(path);
    MapFile back = MapFile.Load(path);
    Check(back.FormatVersion == 2 && back.Airport == "LIPX" && back.Points.Count == 1 && back.Sources.ContainsKey(MapCategory.Fix), "File v2: airport, categories and sources kept");
    MapFile other = new() { Airport = "LIPX" };
    other.AddAs(new MapFile { Points = [new MapPoint { Name = "ZZZ", Latitude = 45, Longitude = 12, Symbol = MapSymbol.Vrp }] }, MapCategory.Vrp);
    back.RemoveCategories([MapCategory.Fix]);
    back.Merge(other);
    Check(back.Points.Count == 1 && back.Points[0].Category == MapCategory.Vrp && !back.Sources.ContainsKey(MapCategory.Fix) && back.OfCategory(MapCategory.Mva).Lines.Count == 1,
        "Merge: a category replaced, the others kept");
    try { Directory.Delete(folder, true); } catch (Exception) { }
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
