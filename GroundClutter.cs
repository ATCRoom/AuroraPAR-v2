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
    /// Ground clutter: fixed echoes of the ground and buildings close to the radar, as left on a real radar after the
    /// MTI filter, invented once from the runway name (always the same for that runway). Eye candy: not real data.
    /// </summary>
    internal static class GroundClutter
    {
        /// <summary>Specks of a runway for a level (empty when off).</summary>
        public static List<ClutterSpeck> Build(Runway runway, GroundClutterLevel level)
        {
            List<ClutterSpeck> specks = [];
            if (level == GroundClutterLevel.Off) return specks;
            double scale = level switch { GroundClutterLevel.Light => 0.5, GroundClutterLevel.Strong => 1.8, _ => 1 };
            Random random = new(Seed(TerrainFile.Key(runway.ICAO, runway.Designator)));
            // Invented: patches of echoes close to the radar, as left after the MTI on a real PAR: most within the first
            // mile from the threshold, none beyond 2 NM, low on the ground and always well under the glide path.
            double antennaAlong = -runway.LengthNM / 2;
            const double MaxAlongNM = 2;
            int patches = 22;
            for (int p = 0; p < patches; p++)
            {
                double along = antennaAlong + 0.1 + (MaxAlongNM - antennaAlong - 0.1) * Math.Pow(random.NextDouble(), 2.5);
                double lateral = (random.NextDouble() * 2 - 1) * (0.15 + 0.6 * random.NextDouble());
                double radius = 0.03 + 0.08 * random.NextDouble();
                double top = 20 + 70 * random.NextDouble();
                double weight = Math.Clamp(1.1 - Math.Max(0, along - antennaAlong) / 2.5, 0.3, 1);
                int count = (int)Math.Clamp(radius * radius * 40000 * weight * scale, 10, 500);
                for (int i = 0; i < count; i++)
                {
                    // Gaussian around the centre of the patch, a little longer than wide along the approach.
                    (double gx, double gy) = Gaussian(random);
                    double a = Math.Min(MaxAlongNM, along + gx * radius * 0.6);
                    double l = lateral + gy * radius * 0.45;
                    // Never near the glide path: at most 40 % of its height there (and the top of the patch).
                    double gpHeight = Math.Max(0, runway.GlidePathHeight(a + runway.TouchdownNM));
                    // Behind the touchdown point there is no glide path (the runway): only the top of the patch.
                    double limit = a + runway.TouchdownNM <= 0 ? top : Math.Min(top, Math.Max(10, 0.4 * gpHeight));
                    double height = limit * Math.Pow(random.NextDouble(), 2);
                    specks.Add(new ClutterSpeck(a, l, height, weight * (0.4 + 0.6 * random.NextDouble())));
                }
            }
            return Capped(specks, random);
        }

        /// <summary>At most <see cref="MaxSpecks"/> (a random part of them), so it stays light to draw.</summary>
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
    }
}
