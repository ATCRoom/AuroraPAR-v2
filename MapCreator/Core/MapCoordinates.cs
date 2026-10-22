using System.Globalization;
using System.Text.RegularExpressions;

namespace ATCRoom.Maps
{
    /// <summary>Coordinates written in the formats of the map files, and the small geometry the importers need.</summary>
    public static partial class MapCoordinates
    {
        public static bool IsValid(double latitude, double longitude) =>
            double.IsFinite(latitude) && double.IsFinite(longitude) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;

        /// <summary>
        /// One coordinate as in the Aurora and EuroScope files: a hemisphere letter and degrees.minutes.seconds.decimals
        /// ("N047.10.47.653", "E013.06.38.300") or decimal degrees ("N013.32861"). False when it is not one.
        /// </summary>
        public static bool TryParseHemisphere(string? text, out double value)
        {
            value = double.NaN;
            string t = (text ?? "").Trim();
            if (t.Length < 2) return false;
            char hemisphere = char.ToUpperInvariant(t[0]);
            if (hemisphere is not ('N' or 'S' or 'E' or 'W')) return false;
            string[] parts = t[1..].Split('.');
            double degrees;
            if (parts.Length == 1 && parts[0].Length == 10 && parts[0].All(char.IsAsciiDigit))
            {
                // Without dots: N0434857348 = N 043 48 57.348 (3 digits of degrees, minutes, seconds, thousandths).
                int d = int.Parse(parts[0][..3], CultureInfo.InvariantCulture);
                int m = int.Parse(parts[0][3..5], CultureInfo.InvariantCulture);
                double s = int.Parse(parts[0][5..7], CultureInfo.InvariantCulture) + int.Parse(parts[0][7..], CultureInfo.InvariantCulture) / 1000.0;
                if (m >= 60 || s >= 60) return false;
                degrees = d + m / 60.0 + s / 3600.0;
            }
            else if (parts.Length >= 3)
            {
                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int d)) return false;
                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m) || m >= 60) return false;
                string secondsText = parts.Length > 3 ? parts[2] + "." + string.Join("", parts.Skip(3)) : parts[2];
                if (!double.TryParse(secondsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double s) || s >= 60) return false;
                degrees = d + m / 60.0 + s / 3600.0;
            }
            else
            {
                if (!double.TryParse(t[1..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out degrees)) return false;
            }
            if (hemisphere is 'S' or 'W') degrees = -degrees;
            double limit = hemisphere is 'N' or 'S' ? 90 : 180;
            if (!double.IsFinite(degrees) || Math.Abs(degrees) > limit) return false;
            value = degrees;
            return true;
        }

        /// <summary>A coordinate as written in the sector files: with a hemisphere letter (any of the forms above) or plain decimal degrees ("41.00850773", negative south / west).</summary>
        public static bool TryParseAny(string? text, out double value)
        {
            if (TryParseHemisphere(text, out value)) return true;
            string t = (text ?? "").Trim();
            if (t.Length > 0 && (char.IsAsciiDigit(t[0]) || t[0] == '-') && t.Contains('.')
                && double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                && Math.Abs(value) <= 180)
            {
                return true;
            }
            value = double.NaN;
            return false;
        }

        /// <summary>The coordinates written as in the AIP tables: 44°16'56" N 008°39'51" E (also with typographic quotes).</summary>
        [GeneratedRegex("(\\d{1,2})\\s*°\\s*(\\d{1,2})\\s*['’′]\\s*(\\d{1,2}(?:[.,]\\d+)?)\\s*[\"”″]\\s*([NS])\\s+(\\d{1,3})\\s*°\\s*(\\d{1,2})\\s*['’′]\\s*(\\d{1,2}(?:[.,]\\d+)?)\\s*[\"”″]\\s*([EW])")]
        private static partial Regex AipCoordinate();

        /// <summary>All the AIP-style coordinate pairs of a text, in order, with the place where each starts.</summary>
        public static List<(int Index, double Latitude, double Longitude)> FindAllAip(string text)
        {
            List<(int, double, double)> found = [];
            foreach (Match match in AipCoordinate().Matches(text ?? ""))
            {
                if (TryFindAip(match.Value, out double latitude, out double longitude)) found.Add((match.Index, latitude, longitude));
            }
            return found;
        }

        /// <summary>The first AIP-style coordinate pair in a text, if any.</summary>
        public static bool TryFindAip(string text, out double latitude, out double longitude)
        {
            latitude = longitude = double.NaN;
            Match m = AipCoordinate().Match(text ?? "");
            if (!m.Success) return false;
            static double Part(Match match, int first)
            {
                double d = double.Parse(match.Groups[first].Value, CultureInfo.InvariantCulture);
                double min = double.Parse(match.Groups[first + 1].Value, CultureInfo.InvariantCulture);
                double sec = double.Parse(match.Groups[first + 2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                return d + min / 60 + sec / 3600;
            }
            latitude = Part(m, 1) * (m.Groups[4].Value == "S" ? -1 : 1);
            longitude = Part(m, 5) * (m.Groups[8].Value == "W" ? -1 : 1);
            return IsValid(latitude, longitude);
        }

        /// <summary>Point at a distance (NM) and bearing (degrees true) from a point (sphere, enough for maps).</summary>
        public static double[] Destination(double latitude, double longitude, double bearing, double distanceNM)
        {
            const double EarthRadiusNM = 3440.065;
            double delta = distanceNM / EarthRadiusNM;
            double theta = bearing * Math.PI / 180;
            double phi1 = latitude * Math.PI / 180;
            double lambda1 = longitude * Math.PI / 180;
            double phi2 = Math.Asin(Math.Sin(phi1) * Math.Cos(delta) + Math.Cos(phi1) * Math.Sin(delta) * Math.Cos(theta));
            double lambda2 = lambda1 + Math.Atan2(Math.Sin(theta) * Math.Sin(delta) * Math.Cos(phi1), Math.Cos(delta) - Math.Sin(phi1) * Math.Sin(phi2));
            return [phi2 * 180 / Math.PI, (lambda2 * 180 / Math.PI + 540) % 360 - 180];
        }

        /// <summary>Initial bearing (degrees true, 0–360) from a point to another.</summary>
        public static double Bearing(double latitude1, double longitude1, double latitude2, double longitude2)
        {
            double phi1 = latitude1 * Math.PI / 180;
            double phi2 = latitude2 * Math.PI / 180;
            double deltaLambda = (longitude2 - longitude1) * Math.PI / 180;
            double y = Math.Sin(deltaLambda) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
            return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        }

        /// <summary>A closed circle around a centre (one point every 5°, the last repeats the first).</summary>
        public static List<double[]> Circle(double latitude, double longitude, double radiusNM)
        {
            List<double[]> points = [];
            for (int angle = 0; angle <= 360; angle += 5) points.Add(Destination(latitude, longitude, angle % 360, radiusNM));
            return points;
        }

        /// <summary>
        /// The points of an arc around a centre, from the bearing of <paramref name="from"/> to the bearing of
        /// <paramref name="to"/> (both excluded), clockwise or not, about one point every 2°.
        /// </summary>
        public static List<double[]> Arc(double[] centre, double radiusNM, double[] from, double[] to, bool clockwise)
        {
            double start = Bearing(centre[0], centre[1], from[0], from[1]);
            double end = Bearing(centre[0], centre[1], to[0], to[1]);
            double sweep = clockwise ? (end - start + 360) % 360 : -((start - end + 360) % 360);
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / 2));
            List<double[]> points = [];
            for (int i = 1; i < steps; i++) points.Add(Destination(centre[0], centre[1], start + sweep * i / steps, radiusNM));
            return points;
        }

        /// <summary>
        /// The arc like <see cref="Arc"/>, but an arc that the text sends the long way round (more than 300° between two end
        /// points less than 60° apart) is turned the other way: such an arc is an error of the AIP (a keyhole shape is
        /// described as a circle, never as a long arc), as in the minimum altitudes of Napoli. <paramref name="reversed"/> tells it.
        /// </summary>
        public static List<double[]> ArcChecked(double[] centre, double radiusNM, double[] from, double[] to, bool clockwise, out bool reversed)
        {
            double start = Bearing(centre[0], centre[1], from[0], from[1]);
            double end = Bearing(centre[0], centre[1], to[0], to[1]);
            double declared = clockwise ? (end - start + 360) % 360 : (start - end + 360) % 360;
            reversed = declared > 300 && declared < 355;
            return Arc(centre, radiusNM, from, to, reversed ? !clockwise : clockwise);
        }

        /// <summary>Centre of an area (centroid of the polygon; the mean of the points for a degenerate one).</summary>
        public static double[] Centroid(IReadOnlyList<double[]> points)
        {
            int n = points.Count;
            if (n == 0) return [double.NaN, double.NaN];
            double area = 0, cx = 0, cy = 0;
            double k = Math.Cos(points[0][0] * Math.PI / 180);
            for (int i = 0; i < n; i++)
            {
                double[] a = points[i];
                double[] b = points[(i + 1) % n];
                double ax = a[1] * k, ay = a[0], bx = b[1] * k, by = b[0];
                double cross = ax * by - bx * ay;
                area += cross;
                cx += (ax + bx) * cross;
                cy += (ay + by) * cross;
            }
            if (Math.Abs(area) < 1e-12) return [points.Average(p => p[0]), points.Average(p => p[1])];
            return [cy / (3 * area), cx / (3 * area) / k];
        }

        /// <summary>A minimum altitude as a short map label: "7000 FT" → "70", "FL90" → "90", other texts as they are.</summary>
        public static string ShortAltitude(string text)
        {
            string t = (text ?? "").Trim().ToUpperInvariant();
            Match ft = Regex.Match(t, "^(\\d{3,5})\\s*FT\\b");
            if (ft.Success) return (int.Parse(ft.Groups[1].Value, CultureInfo.InvariantCulture) / 100).ToString(CultureInfo.InvariantCulture);
            Match fl = Regex.Match(t, "^FL\\s*(\\d{2,3})$");
            if (fl.Success) return int.Parse(fl.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (Regex.IsMatch(t, "^\\d{3,5}$")) return (int.Parse(t, CultureInfo.InvariantCulture) / 100).ToString(CultureInfo.InvariantCulture);
            return t;
        }
    }
}
