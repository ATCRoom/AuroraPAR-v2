using System.Globalization;

namespace AuroraPAR
{
    /// <summary>Strength of the ground clutter in a profile (see <see cref="GroundClutter"/>).</summary>
    internal enum GroundClutterLevel
    {
        Off,
        Light,
        Medium,
        Strong
    }

    /// <summary>
    /// One speck of ground clutter in world coordinates: distance from the threshold along the approach (NM),
    /// lateral offset (NM, + right as seen landing), height above the threshold (ft) and strength (0..1).
    /// </summary>
    internal readonly record struct ClutterSpeck(double AlongNM, double LateralNM, double HeightFt, double Weight);

    /// <summary>
    /// A zone of clutter written for a runway in clutter.par: from-to distance from the threshold (NM), from-to
    /// lateral offset (m, + right as seen landing), strength 1 to 3 and the top of the echoes (ft above the threshold).
    /// </summary>
    internal readonly record struct ClutterZone(double FromNM, double ToNM, double LeftM, double RightM, int Strength, double TopFt);

    /// <summary>
    /// Ground clutter: fixed echoes of the ground, buildings and hills, strongest near the antenna, as left on a real
    /// radar after the MTI filter. A runway with zones in clutter.par gets those (phase 2); the others get clutter
    /// invented once from the runway name, always the same for that runway (phase 1). Simulation only: not real data.
    /// </summary>
    internal static class GroundClutter
    {
        public const string FileName = "clutter.par";
        public const string FormatComment = "# ICAO;RUNWAY;FROM-TO(NM from threshold):LATERAL FROM-TO(m, L or R):STRENGTH 1-3[:TOP ft];... e.g. LIRN;24;1-1.5:R200-R500:3;2.2-3:L800-L300:2:600";

        /// <summary>Specks of a runway for a level (empty when off): the zones of clutter.par, else the invented clutter.</summary>
        public static List<ClutterSpeck> Build(Runway runway, GroundClutterLevel level, IReadOnlyList<ClutterZone>? zones)
        {
            List<ClutterSpeck> specks = [];
            if (level == GroundClutterLevel.Off) return specks;
            double scale = level switch { GroundClutterLevel.Light => 0.5, GroundClutterLevel.Strong => 1.8, _ => 1 };
            Random random = new(Seed(TerrainFile.Key(runway.ICAO, runway.Designator)));
            if (zones is { Count: > 0 })
            {
                foreach (ClutterZone zone in zones)
                {
                    double lengthNM = zone.ToNM - zone.FromNM;
                    double widthNM = Math.Abs(zone.RightM - zone.LeftM) / 1852;
                    double strength = zone.Strength / 3.0;
                    int count = (int)Math.Clamp(lengthNM * Math.Max(widthNM, 0.05) * 9000 * strength * scale, 20, 3000);
                    for (int i = 0; i < count; i++)
                    {
                        double along = zone.FromNM + random.NextDouble() * lengthNM;
                        double lateral = (zone.LeftM + random.NextDouble() * (zone.RightM - zone.LeftM)) / 1852;
                        double height = zone.TopFt * Math.Pow(random.NextDouble(), 2);
                        specks.Add(new ClutterSpeck(along, lateral, height, strength * (0.5 + 0.5 * random.NextDouble())));
                    }
                }
                return Capped(specks, random);
            }
            // Invented: patches of echoes, many near the antenna (middle of the runway), fewer and weaker farther out.
            double antennaAlong = -runway.LengthNM / 2;
            int patches = 28;
            for (int p = 0; p < patches; p++)
            {
                bool hill = random.NextDouble() < 0.12;
                double along = antennaAlong + 0.2 + 5.2 * Math.Pow(random.NextDouble(), 2.2);
                if (hill) along = 2 + 6 * random.NextDouble();
                double lateral = (random.NextDouble() * 2 - 1) * (0.3 + 1.2 * random.NextDouble());
                double radius = hill ? 0.3 + 0.3 * random.NextDouble() : 0.04 + 0.12 * random.NextDouble() + 0.03 * Math.Max(0, along);
                double top = hill ? 300 + 500 * random.NextDouble() : 40 + 200 * random.NextDouble();
                double weight = Math.Clamp(1.1 - Math.Max(0, along - antennaAlong) / 6, 0.25, 1) * (hill ? 0.6 : 1);
                int count = (int)Math.Clamp(radius * radius * 30000 * weight * scale, 15, 900);
                for (int i = 0; i < count; i++)
                {
                    // Gaussian around the centre of the patch, a little longer than wide along the approach.
                    (double gx, double gy) = Gaussian(random);
                    double a = along + gx * radius * 0.6;
                    double l = lateral + gy * radius * 0.45;
                    double height = top * Math.Pow(random.NextDouble(), 2);
                    specks.Add(new ClutterSpeck(a, l, height, weight * (0.4 + 0.6 * random.NextDouble())));
                }
            }
            return Capped(specks, random);
        }

        /// <summary>At most <see cref="MaxSpecks"/> (a random part of them), so a big clutter.par stays light to draw.</summary>
        private const int MaxSpecks = 8000;

        private static List<ClutterSpeck> Capped(List<ClutterSpeck> specks, Random random)
        {
            if (specks.Count <= MaxSpecks) return specks;
            double keep = (double)MaxSpecks / specks.Count;
            return specks.Where(_ => random.NextDouble() < keep).ToList();
        }

        private static (double, double) Gaussian(Random random)
        {
            double u1 = 1 - random.NextDouble(), u2 = random.NextDouble();
            double r = Math.Sqrt(-2 * Math.Log(u1));
            return (r * Math.Cos(2 * Math.PI * u2), r * Math.Sin(2 * Math.PI * u2));
        }

        /// <summary>Seed from the runway name, the same at every start (string.GetHashCode changes from run to run).</summary>
        private static int Seed(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text) hash = (hash ^ c) * 16777619;
                return (int)hash;
            }
        }

        public static string PathFor(string runwaysPath) =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(runwaysPath) ?? "", FileName);

        /// <summary>Zones by runway (<see cref="TerrainFile.Key"/>); empty if the file is missing or cannot be read.</summary>
        public static Dictionary<string, List<ClutterZone>> Read(string path)
        {
            Dictionary<string, List<ClutterZone>> zones = [];
            try
            {
                if (!System.IO.File.Exists(path)) return zones;
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text.StartsWith('#')) continue;
                    string[] fields = text.Split(';', 3);
                    if (fields.Length < 3) continue;
                    if (TryParseZones(fields[2], out List<ClutterZone> list, out _)) zones[TerrainFile.Key(fields[0], fields[1])] = list;
                }
            }
            catch (Exception)
            {
                // Not readable: invented clutter for every runway.
            }
            return zones;
        }

        /// <summary>
        /// Zones as written in the file: <c>1-1.5:R200-R500:3; 2.2-3:L800-L300:2:600</c> (distances NM from the
        /// threshold; lateral m, L = left, R = right as seen landing; strength 1 light to 3 strong; top ft, default
        /// 150 / 300 / 500 by strength).
        /// </summary>
        public static bool TryParseZones(string text, out List<ClutterZone> zones, out string? error)
        {
            zones = [];
            error = null;
            foreach (string raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = raw.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length is < 3 or > 4) { error = $"\"{raw}\": write FROM-TO:LATERAL:STRENGTH, e.g. 1-1.5:R200-R500:3"; return false; }
                // FROM may be negative (towards the antenna): the separator is the first '-' after the first character.
                int dash = parts[0].IndexOf('-', 1);
                string[] along = dash < 0 ? [parts[0]] : [parts[0][..dash].Trim(), parts[0][(dash + 1)..].Trim()];
                string[] lateral = parts[1].Split('-', StringSplitOptions.TrimEntries);
                if (along.Length != 2 || !Number(along[0], out double from) || !Number(along[1], out double to) || from < -2 || to <= from || to > 40)
                {
                    error = $"\"{raw}\": distances FROM-TO in NM from the threshold, going up";
                    return false;
                }
                if (lateral.Length != 2 || !Lateral(lateral[0], out double a) || !Lateral(lateral[1], out double b) || a == b)
                {
                    error = $"\"{raw}\": lateral FROM-TO in m with L or R, e.g. R200-R500 or L300-R300";
                    return false;
                }
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int strength) || strength is < 1 or > 3)
                {
                    error = $"\"{raw}\": strength 1, 2 or 3";
                    return false;
                }
                double top = strength switch { 1 => 150, 2 => 300, _ => 500 };
                if (parts.Length == 4 && (!Number(parts[3], out top) || top <= 0 || top > 5000))
                {
                    error = $"\"{raw}\": top of the echoes in ft, 1 to 5000";
                    return false;
                }
                zones.Add(new ClutterZone(from, to, Math.Min(a, b), Math.Max(a, b), strength, top));
            }
            return true;
        }

        private static bool Lateral(string text, out double metres)
        {
            metres = 0;
            if (text.Length < 2) return false;
            char side = char.ToUpperInvariant(text[0]);
            if (side != 'L' && side != 'R') return false;
            if (!Number(text[1..], out double value) || value < 0 || value > 20000) return false;
            metres = side == 'R' ? value : -value;
            return true;
        }

        private static bool Number(string text, out double value) =>
            double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
