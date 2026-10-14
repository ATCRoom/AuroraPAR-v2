using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>Display mode: the modern synthetic display or the old analog scope.</summary>
    internal enum DisplayMode
    {
        Modern,
        Analog
    }

    /// <summary>
    /// Brush, width and dash style of every element of the views. <see cref="Modern"/> comes from the user's
    /// settings (<see cref="DisplayStyleSettings"/>); <see cref="Analog"/> is the fixed theme of an old radar
    /// scope: one phosphor colour, only the brightness changes from element to element.
    /// </summary>
    internal sealed class Theme
    {
        /// <summary>Default phosphor of the analog scope (yellow-green, as on the European PAR screens).</summary>
        public static readonly Color DefaultPhosphor = Color.FromRgb(0xA8, 0xFF, 0x60);

        private readonly Dictionary<StyleElement, (Brush Brush, double Width, LineDash Dash)> elements = [];

        public bool IsAnalog { get; private init; }
        /// <summary>Beam of the antenna scan effect.</summary>
        public Brush Sweep { get; private set; } = Brushes.White;
        /// <summary>Colour of the glow of the analog scope.</summary>
        public Color Glow { get; private init; } = DefaultPhosphor;

        public Brush Brush(StyleElement element) => elements[element].Brush;
        public double Width(StyleElement element) => elements[element].Width;
        public LineDash Dash(StyleElement element) => elements[element].Dash;
        /// <summary>Lines hidden in this mode (Display style → Colours &amp; lines, Show).</summary>
        private readonly HashSet<StyleElement> hidden = [];
        public bool IsHidden(StyleElement element) => hidden.Contains(element);

        /// <summary>
        /// <paramref name="boost"/> (0 to 1) is the brightness above 100%: the colours are made brighter and
        /// lighter, for dim monitors.
        /// </summary>
        public static Theme Modern(DisplayStyleSettings settings, double boost = 0)
        {
            Theme theme = new() { Sweep = Frozen(Color.FromRgb(0x70, 0xF0, 0xE0)) };
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                LineStyle style = settings.Get(element);
                Color color = ColorText.Parse(style.Color, ColorText.Parse(DisplayStyleSettings.Default(element).Color, System.Windows.Media.Colors.White));
                // The background stays as chosen: only what is drawn on it gets brighter.
                if (element != StyleElement.Background) color = Boost(color, boost);
                theme.elements[element] = (Frozen(color), style.Width, style.Dash);
                if (style.Hidden && DisplayStyleSettings.CanHide(element)) theme.hidden.Add(element);
            }
            // The beam of the scan effect has its own colour (Display style); on the analog scope it is the phosphor.
            theme.Sweep = theme.elements[StyleElement.ScanEffect].Brush;
            return theme;
        }

        /// <param name="style">Width, dash style and Show of the lines as set in Colours &amp; lines (the colours are
        /// the phosphor).</param>
        public static Theme Analog(Color phosphor, double boost = 0, DisplayStyleSettings? style = null)
        {
            boost = Math.Clamp(boost, 0, 1);
            Theme theme = new()
            {
                IsAnalog = true,
                Glow = phosphor,
                Sweep = Frozen(Mix(phosphor, System.Windows.Media.Colors.White, 0.55))
            };
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                LineStyle defaults = DisplayStyleSettings.Default(element);
                Brush brush = element == StyleElement.Background
                    ? Brushes.Transparent
                    : Frozen(Phosphor(phosphor, Brightness(element), boost));
                // Width and dash style as set by the user (same as the modern display); colour: the phosphor.
                LineStyle line = style?.Get(element) ?? defaults;
                theme.elements[element] = (brush, line.Width, line.Dash);
                if (style != null && style.Get(element).HiddenAnalog && DisplayStyleSettings.CanHide(element)) theme.hidden.Add(element);
            }
            return theme;
        }

        /// <summary>
        /// Phosphor colour of an element: its brightness is the opacity. The boost brings the faint elements
        /// towards full intensity (keeping a little of the difference) and lightens the colour.
        /// </summary>
        private static Color Phosphor(Color phosphor, double brightness, double boost)
        {
            double alpha = brightness + (1 - brightness) * 0.8 * boost;
            Color color = Boost(phosphor, boost);
            return Color.FromArgb((byte)Math.Round(255 * alpha), color.R, color.G, color.B);
        }

        /// <summary>
        /// Brighter colour for a boost from 0 (unchanged) to 1: its strongest channel raised to full, then mixed
        /// with a quarter of white.
        /// </summary>
        public static Color Boost(Color color, double boost)
        {
            boost = Math.Clamp(boost, 0, 1);
            if (boost <= 0) return color;
            int strongest = Math.Max(color.R, Math.Max(color.G, color.B));
            double scale = strongest == 0 ? 1 : 1 + (255.0 / strongest - 1) * boost;
            Color scaled = Color.FromArgb(color.A,
                (byte)Math.Min(255, Math.Round(color.R * scale)),
                (byte)Math.Min(255, Math.Round(color.G * scale)),
                (byte)Math.Min(255, Math.Round(color.B * scale)));
            // Black stays black (a colour chosen to hide an element), the others get lighter.
            Color mixed = strongest == 0 ? scaled : Mix(scaled, System.Windows.Media.Colors.White, 0.25 * boost);
            return Color.FromArgb(color.A, mixed.R, mixed.G, mixed.B);
        }

        /// <summary>Brightness of each element on the analog scope.</summary>
        private static double Brightness(StyleElement element) => element switch
        {
            StyleElement.Runway => 0.95,
            StyleElement.GlidePath or StyleElement.Centerline or StyleElement.Touchdown => 0.9,
            StyleElement.Ground or StyleElement.DecisionHeight or StyleElement.DecisionHeightDrop or StyleElement.DecisionHeightMark or StyleElement.DecisionHeightAzimuth => 0.6,
            StyleElement.AntennaBeam => 0.75,
            StyleElement.ScanLimits or StyleElement.Antenna or StyleElement.RangeText => 0.55,
            StyleElement.MarkFive or StyleElement.MarkTwo or StyleElement.MarkOne or StyleElement.AltitudeScale => 0.5,
            StyleElement.ApproachLimits or StyleElement.SafetyLimits or StyleElement.MarkQuarter => 0.45,
            StyleElement.MarkHalf or StyleElement.AltitudeLines => 0.3,
            _ => 1
        };

        /// <summary>WPF dash array of a dash style (in units of the line width).</summary>
        public static DoubleCollection? DashArray(LineDash dash) => dash switch
        {
            LineDash.Dashed => new DoubleCollection { 4, 3 },
            LineDash.DashDot => new DoubleCollection { 6, 3, 1, 3 },
            LineDash.Dotted => new DoubleCollection { 1, 2 },
            _ => null
        };

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }
    }
}
