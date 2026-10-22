using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ATCRoom.Maps
{
    public static partial class MapImport
    {
        private static IEnumerable<XElement> KmlChildren(XElement parent, string name) => parent.Elements().Where(e => e.Name.LocalName == name);

        /// <summary>The "lon,lat[,alt]" list of a coordinates element as points [lat, lon] (an impossible one is dropped).</summary>
        private static List<double[]> KmlCoordinates(XElement? coordinates)
        {
            List<double[]> points = [];
            if (coordinates == null) return points;
            foreach (string token in coordinates.Value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = token.Split(',');
                if (f.Length < 2 || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
                    || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) || !MapCoordinates.IsValid(lat, lon)) continue;
                points.Add([lat, lon]);
            }
            return points;
        }

        /// <summary>
        /// KML (Google Earth; a KMZ is a zip of it, opened by the program). Polygons → closed lines with their name as a label at the
        /// centre (of a 3D one - ceiling, floor and walls, as made by the airspace converters - only the outline in plan is taken);
        /// LineString → line; Point → point (not when an area has the same name: it is the label position of that area). One part for
        /// the CTR (suggested: CTR), one for the ATZ (suggested: ATZ), one for each folder of the others.
        /// </summary>
        public static FileImport ImportKml(string fileName, IReadOnlyList<string> lines)
        {
            FileImport result = new() { FileName = fileName, FormatName = "KML (Google Earth)" };
            XDocument document;
            try
            {
                document = XDocument.Parse(string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                result.Notes.Add($"The KML file is not valid ({ex.Message}).");
                return result;
            }
            string source = "KML " + Path.GetFileName(fileName);
            Dictionary<string, MapFile> parts = new(StringComparer.Ordinal);
            List<string> order = [];
            HashSet<string> areaNames = new(StringComparer.OrdinalIgnoreCase);
            List<(string Part, string Name, XElement Element)> pointPlacemarks = [];
            int areas = 0, linesCount = 0, strays = 0, skippedPoints = 0;

            MapFile PartOf(string key)
            {
                if (!parts.TryGetValue(key, out MapFile? file))
                {
                    file = new MapFile();
                    parts[key] = file;
                    order.Add(key);
                }
                return file;
            }

            foreach (XElement placemark in document.Descendants().Where(e => e.Name.LocalName == "Placemark"))
            {
                string name = KmlChildren(placemark, "name").Select(e => e.Value.Trim()).FirstOrDefault() ?? "";
                string folder = placemark.Ancestors().Where(e => e.Name.LocalName == "Folder")
                    .Select(f => KmlChildren(f, "name").Select(n => n.Value.Trim()).FirstOrDefault() ?? "").FirstOrDefault() ?? "";
                string key = Regex.IsMatch(name, "\\bCTR\\b", RegexOptions.IgnoreCase) ? "CTR"
                    : Regex.IsMatch(name, "\\bATZ\\b", RegexOptions.IgnoreCase) ? "ATZ"
                    : folder.Length > 0 ? folder : "KML";

                // The outlines in plan: every polygon once (the floor repeats the ceiling), not the walls (only two places in plan).
                List<List<double[]>> outlines = [];
                foreach (XElement polygon in placemark.Descendants().Where(e => e.Name.LocalName == "Polygon"))
                {
                    List<double[]> ring = KmlCoordinates(polygon.Descendants().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs")?.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "coordinates"));
                    if (ring.Select(p => (p[0], p[1])).Distinct().Count() < 3) continue;
                    if (outlines.Any(o => o.Count == ring.Count && o.Zip(ring).All(z => z.First[0] == z.Second[0] && z.First[1] == z.Second[1]))) continue;
                    if (ring[0][0] != ring[^1][0] || ring[0][1] != ring[^1][1]) ring.Add(ring[0]);
                    outlines.Add(ring);
                }
                foreach (XElement lineString in placemark.Descendants().Where(e => e.Name.LocalName == "LineString"))
                {
                    List<double[]> path = KmlCoordinates(lineString.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates"));
                    if (path.Count < 2) continue;
                    PartOf(key).Lines.Add(new MapLine { Name = name, Points = path });
                    linesCount++;
                }
                foreach (List<double[]> ring in outlines)
                {
                    MapFile file = PartOf(key);
                    file.Lines.Add(new MapLine { Name = name, Points = ring });
                    if (name.Length > 0)
                    {
                        double[] centre = MapCoordinates.Centroid(ring);
                        file.Labels.Add(new MapLabel { Text = name, Latitude = centre[0], Longitude = centre[1] });
                    }
                    areas++;
                }
                if (outlines.Count > 0 && name.Length > 0) areaNames.Add(name);
                if (placemark.Descendants().Any(e => e.Name.LocalName == "Point")) pointPlacemarks.Add((key, name, placemark));
            }

            foreach ((string part, string name, XElement placemark) in pointPlacemarks)
            {
                if (areaNames.Contains(name)) { skippedPoints++; continue; }
                List<double[]> p = KmlCoordinates(placemark.Descendants().Where(e => e.Name.LocalName == "Point")
                    .Select(e => e.Descendants().FirstOrDefault(c => c.Name.LocalName == "coordinates")).FirstOrDefault(c => c != null));
                if (p.Count == 0) continue;
                PartOf(part).Points.Add(new MapPoint { Name = name, Latitude = p[0][0], Longitude = p[0][1], Symbol = MapSymbol.Point });
                strays++;
            }

            foreach (string key in order.OrderBy(k => k == "CTR" ? 0 : k == "ATZ" ? 1 : 2))
            {
                string? suggested = key == "CTR" ? MapCategory.Ctr : key == "ATZ" ? MapCategory.Atz : null;
                result.Entries.Add(NewEntry(fileName, key is "CTR" or "ATZ" ? key + " (KML)" : key, suggested, parts[key], source));
            }
            result.Notes.Add($"{Plural(areas, "area", "areas")}, {Plural(linesCount, "line", "lines")}, {Plural(strays, "point", "points")} read.");
            if (skippedPoints > 0) result.Notes.Add($"{Plural(skippedPoints, "point", "points")} with the name of an area skipped (they only mark the label of the area).");
            if (areas + linesCount + strays == 0) result.Notes.Add("No polygon, line or point found in this KML file.");
            return result;
        }
    }
}
