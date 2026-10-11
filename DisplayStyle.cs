using System.Globalization;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>Types of range mark, by their interval.</summary>
    internal enum MarkInterval
    {
        Five,
        Two,
        One,
        Half,
        Quarter
    }

    internal static class MarkIntervals
    {
        public static readonly MarkInterval[] All = [MarkInterval.Five, MarkInterval.Two, MarkInterval.One, MarkInterval.Half, MarkInterval.Quarter];

        public static double Nm(MarkInterval interval) => interval switch
        {
            MarkInterval.Five => 5,
            MarkInterval.Two => 2,
            MarkInterval.One => 1,
            MarkInterval.Half => 0.5,
            _ => 0.25
        };

        public static string Name(MarkInterval interval) => interval switch
        {
            MarkInterval.Five => "5 NM",
            MarkInterval.Two => "2 NM",
            MarkInterval.One => "1 NM",
            MarkInterval.Half => "½ NM",
            _ => "¼ NM"
        };

        /// <summary>Style element of a type of range mark.</summary>
        public static StyleElement Element(MarkInterval interval) => interval switch
        {
            MarkInterval.Five => StyleElement.MarkFive,
            MarkInterval.Two => StyleElement.MarkTwo,
            MarkInterval.One => StyleElement.MarkOne,
            MarkInterval.Half => StyleElement.MarkHalf,
            _ => StyleElement.MarkQuarter
        };
    }

    /// <summary>
    /// Range marks of one display range: which types are drawn, and on which ones the distance is written.
    /// </summary>
    internal class RangeMarkRow
    {
        public double Range { get; set; }
        public List<MarkInterval> Lines { get; set; } = [];
        /// <summary>The distance is written on the marks at multiples of this interval; null: no text.</summary>
        public MarkInterval? Text { get; set; }
        /// <summary>Write the unit (NM) after the number; off: only the number.</summary>
        public bool ShowUnit { get; set; } = true;
    }

    /// <summary>
    /// Range marks for every display range (see <see cref="Ranges.All"/>).
    /// </summary>
    /// <summary>How the distance is written under the range marks.</summary>
    internal enum DistanceTextFormat
    {
        /// <summary>1.25NM</summary>
        Decimal,
        /// <summary>1 1/4NM</summary>
        Fractions
    }

    /// <summary>How the range marks are drawn.</summary>
    internal enum RangeMarkDrawing
    {
        /// <summary>Lines across the view (the default).</summary>
        Lines,
        /// <summary>Only short ticks on the distance axis (horizon line / centreline), as on some real scopes.</summary>
        Ticks,
        /// <summary>Lines and ticks.</summary>
        Both
    }

    /// <summary>Side of the distance axis the ticks go to.</summary>
    internal enum TickSide
    {
        Both,
        Above,
        Below
    }

    internal class RangeMarkSettings
    {
        public RangeMarkDrawing Drawing { get; set; } = RangeMarkDrawing.Lines;
        /// <summary>Length of the ticks of the marks with text, in pixels (the others a little shorter).</summary>
        public double TickLength { get; set; } = 10;
        public TickSide TickSide { get; set; } = TickSide.Both;
        public const double MinTickLength = 4;
        public const double MaxTickLength = 30;

        public bool DrawLines => Drawing != RangeMarkDrawing.Ticks;
        public bool DrawTicks => Drawing != RangeMarkDrawing.Lines;
        /// <summary>Tick length of a mark on each side of the axis (above = up on the screen / logical y smaller), 0 if no ticks.</summary>
        public double TickAbove(bool withText) => DrawTicks && TickSide != TickSide.Below ? TickLength * (withText ? 1 : 0.6) : 0;
        public double TickBelow(bool withText) => DrawTicks && TickSide != TickSide.Above ? TickLength * (withText ? 1 : 0.6) : 0;
        public List<RangeMarkRow> Rows { get; set; } = [];
        public DistanceTextFormat TextFormat { get; set; } = DistanceTextFormat.Decimal;

        /// <summary>Size of the distance text, in pixels.</summary>
        public double TextSize { get; set; } = DefaultTextSize;
        public const double DefaultTextSize = 12;
        public const int MinTextSize = 10;
        public const int MaxTextSize = 18;

        /// <summary>Default marks: every 5 NM at 30 and 40 NM, every 2 NM at 20 NM, every NM at 15 and 10, NM and half miles at 5, quarter miles at 2.5 and 1.</summary>
        public static RangeMarkSettings Default()
        {
            RangeMarkSettings settings = new();
            foreach (double range in Ranges.All)
            {
                settings.Rows.Add(DefaultRow(range));
            }
            return settings;
        }

        public static RangeMarkRow DefaultRow(double range)
        {
            return range switch
            {
                >= 30 => new() { Range = range, Lines = [MarkInterval.Five], Text = MarkInterval.Five },
                >= 20 => new() { Range = range, Lines = [MarkInterval.Two], Text = MarkInterval.Two },
                >= 10 => new() { Range = range, Lines = [MarkInterval.One], Text = MarkInterval.One },
                >= 5 => new() { Range = range, Lines = [MarkInterval.One, MarkInterval.Half], Text = MarkInterval.One },
                _ => new() { Range = range, Lines = [MarkInterval.One, MarkInterval.Half, MarkInterval.Quarter], Text = MarkInterval.Quarter }
            };
        }

        /// <summary>Row of the given range (the default one if missing).</summary>
        public RangeMarkRow For(double range)
        {
            return Rows.FirstOrDefault(r => Math.Abs(r.Range - range) < 0.01) ?? DefaultRow(range);
        }

        /// <summary>One row per display range, without duplicates or unknown values.</summary>
        public void Normalize()
        {
            Rows ??= [];
            if (!Enum.IsDefined(TextFormat)) TextFormat = DistanceTextFormat.Decimal;
            if (!Enum.IsDefined(Drawing)) Drawing = RangeMarkDrawing.Lines;
            if (!Enum.IsDefined(TickSide)) TickSide = TickSide.Both;
            TickLength = TickLength <= 0 ? 10 : Math.Clamp(Math.Round(TickLength), MinTickLength, MaxTickLength);
            TextSize = TextSize <= 0 ? DefaultTextSize : Math.Clamp(Math.Round(TextSize), MinTextSize, MaxTextSize);
            List<RangeMarkRow> rows = [];
            foreach (double range in Ranges.All)
            {
                RangeMarkRow? row = Rows.FirstOrDefault(r => r != null && Math.Abs(r.Range - range) < 0.01);
                if (row == null)
                {
                    rows.Add(DefaultRow(range));
                    continue;
                }
                row.Range = range;
                row.Lines = (row.Lines ?? []).Where(i => Enum.IsDefined(i)).Distinct().OrderBy(i => i).ToList();
                if (row.Text is MarkInterval text && !Enum.IsDefined(text)) row.Text = null;
                rows.Add(row);
            }
            Rows = rows;
        }

        /// <summary>
        /// Marks to draw at a range: distance from touchdown, style element and whether the distance is written.
        /// Each ticked type draws its lines (every multiple of its interval) with its own style; where ticked types
        /// overlap (e.g. 1 NM is also a ½ and a ¼ mile) one line is drawn, with the style of the largest one.
        /// </summary>
        public IEnumerable<(double Distance, StyleElement Element, bool Text)> Marks(double range)
        {
            RangeMarkRow row = For(range);
            List<MarkInterval> selected = row.Lines.ToList();
            if (selected.Count == 0) yield break;
            // Only the ticked types count: a line takes the style of the largest ticked type it belongs to.
            List<MarkInterval> types = selected.OrderByDescending(MarkIntervals.Nm).ToList();
            const double finest = 0.25;
            int count = (int)Math.Floor(range / finest + 1e-6);
            for (int k = 1; k <= count; k++)
            {
                double distance = Math.Round(k * finest, 3);
                if (!selected.Any(i => IsMultiple(distance, MarkIntervals.Nm(i)))) continue;
                MarkInterval type = types.FirstOrDefault(i => IsMultiple(distance, MarkIntervals.Nm(i)), MarkInterval.Quarter);
                bool text = row.Text is MarkInterval t && IsMultiple(distance, MarkIntervals.Nm(t));
                yield return (distance, MarkIntervals.Element(type), text);
            }
        }

        private static bool IsMultiple(double value, double interval)
        {
            double q = value / interval;
            return Math.Abs(q - Math.Round(q)) < 1e-6;
        }

        /// <summary>Distance written under a range mark, in the chosen format (1.25NM or 1 1/4NM).</summary>
        public string Label(double distance, double range)
        {
            string unit = For(range).ShowUnit ? "NM" : "";
            // Quarters only: any other value (not possible with the marks offered) is written as a decimal.
            if (TextFormat == DistanceTextFormat.Fractions && Math.Abs(distance * 4 - Math.Round(distance * 4)) < 1e-6)
            {
                double whole = Math.Floor(distance + 1e-6);
                // Normal digits (1/4, 1/2, 3/4): the ¼ ½ ¾ characters are too small to read on the scope.
                string fraction = Math.Round((distance - whole) * 4) switch
                {
                    1 => "1/4",
                    2 => "1/2",
                    3 => "3/4",
                    _ => ""
                };
                string number = whole.ToString("0", CultureInfo.InvariantCulture);
                string text = fraction.Length == 0 ? number : whole > 0 ? number + " " + fraction : fraction;
                return text + unit;
            }
            return distance.ToString("0.##", CultureInfo.InvariantCulture) + unit;
        }
    }

    internal enum LineDash
    {
        Solid,
        Dashed,
        DashDot,
        Dotted
    }

    /// <summary>Elements of the display that have their own colour (and, for lines, style and width).</summary>
    internal enum StyleElement
    {
        MarkFive,
        MarkTwo,
        MarkOne,
        MarkHalf,
        MarkQuarter,
        RangeText,
        GlidePath,
        Centerline,
        ApproachLimits,
        ScanLimits,
        /// <summary>Edges of the antenna beam (moved by the tilt inside the scan limits).</summary>
        AntennaBeam,
        /// <summary>Antenna symbol (added later: older profiles get the colour of the scan limits).</summary>
        Antenna,
        DecisionHeight,
        Runway,
        Ground,
        Touchdown,
        AltitudeScale,
        /// <summary>Horizontal lines every 1000 (optional).</summary>
        AltitudeLines,
        /// <summary>Beam line of the antenna scan effect (added later: older profiles get the default, 1 px).</summary>
        ScanEffect,
        Background,
        PlotInside,
        PlotOutside,
        TrackInside,
        TrackOutside,
        /// <summary>Coasting track: estimated position of a track out of the beam (modern display).</summary>
        TrackCoasting,
        LabelText,
        /// <summary>Vertical line from the intercept of the glide path with the DH down to the ground (added later: older profiles get the style of the DH line, dashed).</summary>
        DecisionHeightDrop,
        /// <summary>Colour of the symbol on the DH point (added later: older profiles get the colour of the DH line).</summary>
        DecisionHeightMark,
        /// <summary>DH line of the azimuth view (added later: older profiles get the style of the DH line).</summary>
        DecisionHeightAzimuth,
        /// <summary>Line joining a dragged label to its track (added later: older profiles get the default pale yellow, not the colour of the track).</summary>
        LabelLeader,
        /// <summary>Frame of the track label (sides chosen in the label editor; invisible by default).</summary>
        LabelFrame,
        /// <summary>Text colour of the field kept outside the label frame.</summary>
        LabelTextOutside
    }

    /// <summary>Colour (as #RRGGBB), dash style and width of an element.</summary>
    internal class LineStyle
    {
        public string Color { get; set; } = "#00FF00";
        public LineDash Dash { get; set; } = LineDash.Solid;
        public double Width { get; set; } = 1;
        /// <summary>Line not drawn in the modern display / on the analog scope (see <see cref="DisplayStyleSettings.CanHide"/>).</summary>
        public bool Hidden { get; set; }
        public bool HiddenAnalog { get; set; }

        public LineStyle Copy() => new() { Color = Color, Dash = Dash, Width = Width, Hidden = Hidden, HiddenAnalog = HiddenAnalog };
    }

    /// <summary>
    /// Colours, dash styles and widths of the elements. The analog scope uses the dash styles, widths and its own
    /// Show choice, with the phosphor colour instead of the colours.
    /// </summary>
    internal class DisplayStyleSettings
    {
        public Dictionary<StyleElement, LineStyle> Elements { get; set; } = [];

        /// <summary>True for elements drawn as lines (with dash style and width); the others have only a colour.</summary>
        public static bool IsLine(StyleElement element) => element switch
        {
            StyleElement.RangeText or StyleElement.Background or StyleElement.PlotInside or StyleElement.PlotOutside
                or StyleElement.Antenna or StyleElement.TrackInside or StyleElement.TrackOutside or StyleElement.TrackCoasting
                or StyleElement.LabelText or StyleElement.LabelTextOutside or StyleElement.DecisionHeightMark => false,
            _ => true
        };

        /// <summary>
        /// Lines that can be hidden (in each mode): all the lines but the range marks, which have their own table.
        /// </summary>
        public static bool CanHide(StyleElement element) => IsLine(element)
            && element is not (StyleElement.MarkFive or StyleElement.MarkTwo or StyleElement.MarkOne or StyleElement.MarkHalf or StyleElement.MarkQuarter);

        public static string Name(StyleElement element) => element switch
        {
            StyleElement.MarkFive => "Range marks 5 NM",
            StyleElement.MarkTwo => "Range marks 2 NM",
            StyleElement.MarkOne => "Range marks 1 NM",
            StyleElement.MarkHalf => "Range marks ½ NM",
            StyleElement.MarkQuarter => "Range marks ¼ NM",
            StyleElement.RangeText => "Range mark text",
            StyleElement.GlidePath => "Glide path",
            StyleElement.Centerline => "Centreline",
            StyleElement.ApproachLimits => "Approach limits",
            StyleElement.ScanLimits => "Scan limits",
            StyleElement.AntennaBeam => "Antenna beam",
            StyleElement.Antenna => "Antenna",
            StyleElement.DecisionHeight => "Decision height line (horizontal)",
            StyleElement.DecisionHeightDrop => "Decision height drop line (vertical)",
            StyleElement.DecisionHeightMark => "Decision height mark (symbol)",
            StyleElement.DecisionHeightAzimuth => "Decision height line (azimuth view)",
            StyleElement.LabelLeader => "Label leader line",
            StyleElement.LabelFrame => "Label frame",
            StyleElement.LabelTextOutside => "Label text (field outside the frame)",
            StyleElement.Runway => "Runway and threshold",
            StyleElement.Ground => "Ground",
            StyleElement.Touchdown => "Touchdown point",
            StyleElement.AltitudeScale => "Altitude scale",
            StyleElement.AltitudeLines => "Altitude lines (every 1000)",
            StyleElement.ScanEffect => "Antenna scan effect (beam line)",
            StyleElement.Background => "Background",
            StyleElement.PlotInside => "Plots inside limits (history)",
            StyleElement.PlotOutside => "Plots outside limits (history)",
            StyleElement.TrackInside => "Tracks inside limits",
            StyleElement.TrackOutside => "Tracks outside limits",
            StyleElement.TrackCoasting => "Coasting tracks (estimated position)",
            StyleElement.LabelText => "Label text",
            _ => element.ToString()
        };

        public static LineStyle Default(StyleElement element) => element switch
        {
            StyleElement.MarkHalf => new() { Color = "#008000", Dash = LineDash.Dashed, Width = 1 },
            StyleElement.MarkFive or StyleElement.MarkTwo or StyleElement.MarkOne or StyleElement.MarkQuarter => new() { Color = "#008000", Width = 1 },
            StyleElement.RangeText => new() { Color = "#FFFF00" },
            StyleElement.GlidePath => new() { Color = "#FFFF00", Width = 2 },
            StyleElement.Centerline => new() { Color = "#FFFF00", Width = 2 },
            StyleElement.ApproachLimits => new() { Color = "#FF0000", Width = 1 },
            StyleElement.ScanLimits => new() { Color = "#5F9EA0", Width = 3 },
            StyleElement.AntennaBeam => new() { Color = "#40E0D0", Width = 2 },
            StyleElement.Antenna => new() { Color = "#5F9EA0" },
            StyleElement.DecisionHeight => new() { Color = "#FF0000", Width = 2 },
            StyleElement.DecisionHeightDrop => new() { Color = "#FF0000", Width = 2, Dash = LineDash.Dashed },
            StyleElement.DecisionHeightMark => new() { Color = "#FF0000" },
            StyleElement.DecisionHeightAzimuth => new() { Color = "#FF0000", Width = 2 },
            StyleElement.LabelLeader => new() { Color = "#F0E39E", Width = 1 },
            StyleElement.LabelFrame => new() { Color = "#C87832", Width = 1 },
            StyleElement.LabelTextOutside => new() { Color = "#FFFF00" },
            StyleElement.Runway => new() { Color = "#008000", Width = 3 },
            StyleElement.Ground => new() { Color = "#008000", Width = 2 },
            StyleElement.Touchdown => new() { Color = "#FFFF00", Width = 2 },
            StyleElement.AltitudeScale => new() { Color = "#808080", Width = 1 },
            StyleElement.AltitudeLines => new() { Color = "#008000", Dash = LineDash.Dotted, Width = 1 },
            StyleElement.ScanEffect => new() { Color = "#70F0E0", Width = 1 },
            StyleElement.Background => new() { Color = "#000000" },
            StyleElement.PlotInside => new() { Color = "#008000" },
            StyleElement.PlotOutside => new() { Color = "#FF0000" },
            StyleElement.TrackInside => new() { Color = "#008000" },
            StyleElement.TrackOutside => new() { Color = "#FF0000" },
            StyleElement.TrackCoasting => new() { Color = "#008000" },
            StyleElement.LabelText => new() { Color = "#FFFFFF" },
            _ => new()
        };

        public static DisplayStyleSettings CreateDefault()
        {
            DisplayStyleSettings settings = new();
            settings.Normalize();
            return settings;
        }

        public LineStyle Get(StyleElement element)
        {
            return Elements.TryGetValue(element, out LineStyle? style) ? style : Default(element);
        }

        /// <summary>Every element present, with a valid colour, dash style and width.</summary>
        public void Normalize()
        {
            Elements ??= [];
            // Profiles saved before the antenna had its own colour: same colour as the scan limits, as it was.
            if ((!Elements.TryGetValue(StyleElement.Antenna, out LineStyle? antenna) || antenna == null)
                && Elements.TryGetValue(StyleElement.ScanLimits, out LineStyle? scan) && scan != null
                && ColorText.TryParse(scan.Color, out _))
            {
                Elements[StyleElement.Antenna] = new LineStyle { Color = scan.Color };
            }
            // Profiles saved before the DH had a vertical line and a mark of their own: the style of the DH line, as it was
            // (the vertical line was always dashed).
            if (Elements.TryGetValue(StyleElement.DecisionHeight, out LineStyle? dh) && dh != null && ColorText.TryParse(dh.Color, out _))
            {
                if (!Elements.TryGetValue(StyleElement.DecisionHeightDrop, out LineStyle? drop) || drop == null)
                {
                    LineStyle copy = dh.Copy();
                    copy.Dash = LineDash.Dashed;
                    Elements[StyleElement.DecisionHeightDrop] = copy;
                }
                if (!Elements.TryGetValue(StyleElement.DecisionHeightAzimuth, out LineStyle? azimuth) || azimuth == null)
                {
                    Elements[StyleElement.DecisionHeightAzimuth] = dh.Copy();
                }
                if (!Elements.TryGetValue(StyleElement.DecisionHeightMark, out LineStyle? mark) || mark == null)
                {
                    Elements[StyleElement.DecisionHeightMark] = new LineStyle { Color = dh.Color };
                }
            }
            // Profiles saved before the antenna beam: the old scan limits were the beam; with a colour or line of
            // their own they keep it on the beam (same picture). With the default style the beam gets its new one.
            if ((!Elements.TryGetValue(StyleElement.AntennaBeam, out LineStyle? beam) || beam == null)
                && Elements.TryGetValue(StyleElement.ScanLimits, out LineStyle? oldScan) && oldScan != null
                && ColorText.TryParse(oldScan.Color, out _))
            {
                LineStyle defaults = Default(StyleElement.ScanLimits);
                bool customised = !string.Equals(oldScan.Color, defaults.Color, StringComparison.OrdinalIgnoreCase)
                    || oldScan.Width != defaults.Width || oldScan.Dash != defaults.Dash;
                if (customised) Elements[StyleElement.AntennaBeam] = oldScan.Copy();
            }
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                if (!Elements.TryGetValue(element, out LineStyle? style) || style == null)
                {
                    Elements[element] = Default(element);
                    continue;
                }
                if (!ColorText.TryParse(style.Color, out _)) style.Color = Default(element).Color;
                if (!Enum.IsDefined(style.Dash)) style.Dash = LineDash.Solid;
                style.Width = double.IsNaN(style.Width) ? 1 : Math.Clamp(style.Width, 1, 6);
            }
            foreach (StyleElement unknown in Elements.Keys.Where(k => !Enum.IsDefined(k)).ToList())
            {
                Elements.Remove(unknown);
            }
        }
    }
}
