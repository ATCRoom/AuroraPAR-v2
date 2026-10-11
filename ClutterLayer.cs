using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraPAR
{
    /// <summary>Rain intensity of the analog scope clutter.</summary>
    internal enum RainLevel
    {
        None,
        Light,
        Moderate,
        Heavy,
        Thunderstorm
    }

    /// <summary>Rain clutter: intensity from the METAR, and the filter set on the console.</summary>
    internal static class RainClutter
    {
        /// <summary>Present weather group of a METAR: -RA, +SHRA, TSRA, DZ, SN...</summary>
        private static readonly Regex Weather = new(@"^(\+|-)?(VC)?(MI|BC|PR|DR|BL|SH|TS|FZ)*(DZ|RA|SN|SG|PL|GR|GS|UP)+$");

        /// <summary>Intensity of the precipitation reported in a METAR (None when there is none).</summary>
        public static RainLevel FromMetar(string? metar)
        {
            if (string.IsNullOrWhiteSpace(metar)) return RainLevel.None;
            RainLevel level = RainLevel.None;
            foreach (string token in metar.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // The trend and remarks are not the present weather.
                if (token is "RMK" or "TEMPO" or "BECMG" or "NOSIG") break;
                if (!Weather.IsMatch(token) || token.StartsWith("VC")) continue;
                RainLevel found = token.Contains("TS") ? RainLevel.Thunderstorm
                    : token.StartsWith('+') ? RainLevel.Heavy
                    : token.StartsWith('-') || token.Contains("DZ") ? RainLevel.Light
                    : RainLevel.Moderate;
                if (found > level) level = found;
            }
            return level;
        }

        /// <summary>Wind in a METAR: 27015KT, 27015G25KT, 27008MPS (VRB has no direction).</summary>
        private static readonly Regex MetarWind = new(@"\b(\d{3})(\d{2,3})(?:G\d{2,3})?(KT|MPS|KMH)\b");

        /// <summary>Wind of a METAR: direction it blows from (degrees true) and speed in knots.</summary>
        public static bool TryWind(string? metar, out double from, out double knots)
        {
            from = knots = 0;
            if (string.IsNullOrEmpty(metar)) return false;
            Match match = MetarWind.Match(metar);
            if (!match.Success) return false;
            from = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double factor = match.Groups[3].Value switch { "MPS" => 1.94384, "KMH" => 0.539957, _ => 1 };
            knots = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * factor;
            return true;
        }

        /// <summary>Strength of the clutter, 0 to 1.</summary>
        public static double Strength(RainLevel level) => level switch
        {
            RainLevel.Light => 0.25,
            RainLevel.Moderate => 0.55,
            RainLevel.Heavy => 0.9,
            RainLevel.Thunderstorm => 1.0,
            _ => 0
        };

        public static string DisplayName(RainLevel level) => level switch
        {
            RainLevel.Light => "Light",
            RainLevel.Moderate => "Moderate",
            RainLevel.Heavy => "Heavy",
            RainLevel.Thunderstorm => "Thunderstorm",
            _ => "None"
        };
    }

    /// <summary>
    /// Rain clutter of the analog scope: irregular patches alternating with dots, only inside the antenna beam (with a
    /// short fade at its edges), brighter when the scan beam passes over them, like the echoes. The patches drift a
    /// little and the dots are renewed at every scan cycle. Graphic only: one bitmap per view at half resolution,
    /// updated every other frame.
    /// </summary>
    internal sealed class ClutterLayer
    {
        private const int Tile = 256;
        /// <summary>Screen pixels per bitmap pixel.</summary>
        private const int Pixel = 2;
        /// <summary>Width of the fade at the edges of the beam, as a fraction of the scan (a soft edge, not a cut).</summary>
        private const double EdgeFade = 0.015;
        private const int LutSize = 160;

        private static readonly float[] Coarse = MakeNoise(20, 11);
        private static readonly float[] Fine = MakeNoise(40, 23);
        private static readonly float[] Patch = MakeNoise(10, 5);

        private readonly Image image = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
        private WriteableBitmap? bitmap;
        private int[] pixels = [];
        private int frame;
        private readonly float[] light = new float[LutSize + 1];
        /// <summary>Beam limits of the last frame, and the previous ones after a tilt (kept a moment and faded out).</summary>
        private struct Wedge
        {
            public double LowY, HighY, Weight;
        }
        private readonly List<(double LowY, double HighY, double Since)> trail = [];
        private double lastLow = double.NaN, lastHigh, lastOriginX, lastOriginY, lastEndX, lastTrailTime = -1;
        private double offsetX, offsetY, lastTime = double.NaN;
        private const double TrailSpacing = 0.2;
        private const double TrailSeconds = 0.7;
        private const int TrailMax = 5;
        private Wedge[] wedges = new Wedge[TrailMax + 1];
        /// <summary>Compressed scale: linear-scale x and factor of each column of the bitmap (see Frame.Unwarp).</summary>
        private double[] columnX = [], columnFactor = [], rowY = [];

        public Image Element => image;

        /// <summary>Geometry and state of a frame (logical coordinates as in the views).</summary>
        public struct Frame
        {
            public double Width, Height, Time, SweepCycle;
            public bool Elevation, RunwayOnRight, FlipVertically;
            public double XShift, OriginX, OriginY, EndX, LowY, HighY, HorizonY;
            public double Strength, Filter;
            /// <summary>Drift of the patches with the wind, logical pixels per second.</summary>
            public double DriftX, DriftY;
            public ScanEffectSpeed Speed;
            public Color Colour;
            /// <summary>
            /// Compressed distance scale: for a logical x, the linear-scale x and the factor that brings a logical
            /// distance from <see cref="AxisY"/> back to the linear scale; null with the linear scale. The beam
            /// geometry above (origin, end, limits) is given in linear-scale coordinates.
            /// </summary>
            public Func<double, (double X, double Factor)>? Unwarp;
            /// <summary>Compressed vertical axis (profile, version B): logical y → linear-scale y, before the column factor; null otherwise.</summary>
            public Func<double, double>? UnwarpY;
            public double AxisY;
        }

        public void Hide() => image.Visibility = Visibility.Collapsed;

        public void Update(in Frame f)
        {
            if (f.Strength <= 0 || f.Width < 20 || f.Height < 20 || Math.Abs(f.HighY - f.LowY) < 1 || f.EndX - f.OriginX < 1)
            {
                Hide();
                return;
            }
            // Tilt: the picture stays a moment where the beam was and fades, it does not vanish at once.
            if (!double.IsNaN(lastLow))
            {
                if (Math.Abs(f.OriginX - lastOriginX) > 0.5 || Math.Abs(f.OriginY - lastOriginY) > 0.5 || Math.Abs(f.EndX - lastEndX) > 0.5) trail.Clear();
                else if ((Math.Abs(f.LowY - lastLow) > 0.5 || Math.Abs(f.HighY - lastHigh) > 0.5) && f.Time - lastTrailTime >= TrailSpacing)
                {
                    trail.Add((lastLow, lastHigh, f.Time));
                    lastTrailTime = f.Time;
                    if (trail.Count > TrailMax) trail.RemoveAt(0);
                }
            }
            lastLow = f.LowY; lastHigh = f.HighY; lastOriginX = f.OriginX; lastOriginY = f.OriginY; lastEndX = f.EndX;
            double now = f.Time;
            trail.RemoveAll(t => Math.Exp(-(now - t.Since) / TrailSeconds) < 0.03);
            int count = 0;
            wedges[count++] = new Wedge { LowY = f.LowY, HighY = f.HighY, Weight = 1 };
            foreach (var t in trail) wedges[count++] = new Wedge { LowY = t.LowY, HighY = t.HighY, Weight = Math.Exp(-(f.Time - t.Since) / TrailSeconds) };
            image.Visibility = Visibility.Visible;
            int w = (int)Math.Ceiling(f.Width / Pixel), h = (int)Math.Ceiling(f.Height / Pixel);
            if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
            {
                bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                pixels = new int[w * h];
                image.Source = bitmap;
                frame = 0;
            }
            image.Width = w * Pixel;
            image.Height = h * Pixel;
            if ((frame++ & 1) != 0) return;

            // Light of the beam by position across the scan (the same law as the echoes).
            for (int i = 0; i <= LutSize; i++)
            {
                double p = -0.1 + 1.2 * i / LutSize;
                light[i] = (float)ScanEffect.BeamLight(f.Time, f.Speed, f.Elevation, p, 0.6);
            }
            // The patches move with the wind (real speed, in scale with the range).
            double dt = double.IsNaN(lastTime) ? 0 : Math.Clamp(f.Time - lastTime, 0, 0.5);
            lastTime = f.Time;
            offsetX += f.DriftX * dt;
            offsetY += f.DriftY * dt;
            // Rain is static compared with an aircraft: patches and dots do not move (only their light changes with the beam).
            int cycle = 0;
            double ratio = Tile / f.Width;
            double thr = 0.80 - 0.24 * f.Strength;
            double dotBase = 0.05 * f.Strength;
            double gainFilter = 1 - 0.85 * f.Filter;
            double span = f.EndX - f.OriginX;
            double slope = (f.EndX - f.OriginX);
            if (f.Unwarp != null)
            {
                if (columnX.Length != w)
                {
                    columnX = new double[w];
                    columnFactor = new double[w];
                }
                for (int bx = 0; bx < w; bx++)
                {
                    double sx = (bx + 0.5) * Pixel;
                    (columnX[bx], columnFactor[bx]) = f.Unwarp((f.RunwayOnRight ? f.Width - sx : sx) - f.XShift);
                }
            }
            if (f.UnwarpY != null)
            {
                if (rowY.Length != h) rowY = new double[h];
                for (int by = 0; by < h; by++)
                {
                    double sy = (by + 0.5) * Pixel;
                    rowY[by] = f.UnwarpY(f.RunwayOnRight && f.FlipVertically ? f.Height - sy : sy);
                }
            }
            Array.Clear(pixels);
            for (int by = 0; by < h; by++)
            {
                double sy = (by + 0.5) * Pixel;
                double ly = f.RunwayOnRight && f.FlipVertically ? f.Height - sy : sy;
                if (ly > f.HorizonY) continue;
                double rowLinearY = f.UnwarpY != null ? rowY[by] : ly;
                for (int bx = 0; bx < w; bx++)
                {
                    double sx = (bx + 0.5) * Pixel;
                    double lx = (f.RunwayOnRight ? f.Width - sx : sx) - f.XShift;
                    // Direction from the antenna in linear-scale coordinates (the same as the logical ones with
                    // the linear scale); the noise below stays in logical coordinates (same look on the screen).
                    double linearX = lx, linearY = ly;
                    if (f.Unwarp != null)
                    {
                        linearX = columnX[bx];
                        linearY = f.AxisY + (rowLinearY - f.AxisY) * columnFactor[bx];
                    }
                    double dx = linearX - f.OriginX;
                    if (dx <= 1) continue;
                    double mask = 0, p = 0;
                    for (int g = 0; g < count; g++)
                    {
                        double y = f.OriginY + (linearY - f.OriginY) * slope / dx;
                        double pg = (y - wedges[g].LowY) / (wedges[g].HighY - wedges[g].LowY);
                        double m = Math.Clamp((Math.Min(pg, 1 - pg) + EdgeFade) / (2 * EdgeFade), 0, 1) * wedges[g].Weight;
                        if (m > mask) { mask = m; p = pg; }
                    }
                    if (mask <= 0) continue;
                    int tx = (int)Math.Floor((lx - offsetX) * ratio) & (Tile - 1);
                    int ty = (int)Math.Floor((ly - offsetY) * ratio) & (Tile - 1);
                    int ti = ty * Tile + tx;
                    double range = 0.4 + 0.6 * Math.Min(1, dx / span);
                    double n = 0.6 * Coarse[ti] + 0.4 * Fine[ti];
                    double mottle = 0.4 + 0.6 * Hash(bx, by, 0);
                    double blob = Math.Clamp((n - thr) / 0.12, 0, 1) * mottle * range;
                    double field = Math.Clamp((Patch[ti] - 0.35) * 1.8, 0, 1);
                    double dot = 0;
                    if (Hash(bx, by, cycle + 1) < dotBase * field * range * 3) dot = 0.3 + 0.7 * Hash(bx, by, cycle + 7);
                    double a = (blob * f.Strength * 1.1 + dot);
                    if (a <= 0.01) continue;
                    double lit = light[Math.Clamp((int)((p + 0.1) / 1.2 * LutSize), 0, LutSize)];
                    a = Math.Min(1, a * (0.6 + 0.6 * lit) * mask * gainFilter);
                    if (a <= 0.01) continue;
                    int alpha = (int)(a * 235);
                    pixels[by * w + bx] = (alpha << 24)
                        | ((f.Colour.R * alpha / 255) << 16)
                        | ((f.Colour.G * alpha / 255) << 8)
                        | (f.Colour.B * alpha / 255);
                }
            }
            bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        }

        private static double Hash(int x, int y, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h *= 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 65536.0;
        }

        /// <summary>Value noise 0..1 on a wrapping tile, with <paramref name="cells"/> cells across.</summary>
        private static float[] MakeNoise(int cells, int seed)
        {
            Random random = new(seed);
            float[] grid = new float[cells * cells];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)random.NextDouble();
            float[] tile = new float[Tile * Tile];
            double step = (double)cells / Tile;
            for (int y = 0; y < Tile; y++)
            {
                double gy = y * step;
                int y0 = (int)gy;
                double fy = gy - y0;
                fy = fy * fy * (3 - 2 * fy);
                for (int x = 0; x < Tile; x++)
                {
                    double gx = x * step;
                    int x0 = (int)gx;
                    double fx = gx - x0;
                    fx = fx * fx * (3 - 2 * fx);
                    int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                    double top = grid[y0 * cells + x0] * (1 - fx) + grid[y0 * cells + x1] * fx;
                    double bottom = grid[y1 * cells + x0] * (1 - fx) + grid[y1 * cells + x1] * fx;
                    tile[y * Tile + x] = (float)(top * (1 - fy) + bottom * fy);
                }
            }
            return tile;
        }
    }
}
