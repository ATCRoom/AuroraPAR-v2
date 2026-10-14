using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraPAR
{
    internal enum RunwaySide
    {
        Left,
        Right
    }

    /// <summary>
    /// Display range used when the program starts.
    /// </summary>
    internal enum StartupRange
    {
        /// <summary>The range in use when the program was last closed.</summary>
        LastUsed,
        /// <summary>The default range of the runway, from runways.par.</summary>
        RunwayDefault,
        /// <summary>Always <see cref="Profile.PreferredRange"/>.</summary>
        Fixed
    }

    /// <summary>
    /// Display range used when another runway is selected.
    /// </summary>
    internal enum RunwayChangeRange
    {
        /// <summary>The default range of the new runway, from runways.par.</summary>
        RunwayDefault,
        /// <summary>The range currently in use is kept.</summary>
        KeepCurrent,
        /// <summary>Always <see cref="Profile.PreferredRange"/>.</summary>
        Fixed
    }

    internal enum PressureReference
    {
        /// <summary>Altitudes (above mean sea level).</summary>
        QNH,
        /// <summary>Heights above the runway threshold.</summary>
        QFE
    }

    internal enum PressureUnit
    {
        HectoPascal,
        InchesOfMercury
    }

    /// <summary>
    /// Name used for the minimum: the altitude form with QNH, the height form with QFE.
    /// </summary>
    internal enum MinimaLabel
    {
        DaDh,
        OcaOch,
        MdaMdh
    }

    internal enum LengthUnit
    {
        Feet,
        Metres
    }

    /// <summary>
    /// Pressure conversions and formatting.
    /// </summary>
    internal static class Pressure
    {
        public const double HectoPascalPerInch = 33.8639;

        /// <summary>
        /// QFE at the given elevation from the QNH (standard atmosphere), in hPa.
        /// </summary>
        public static double QfeFromQnh(double qnhHectoPascal, double elevationFt)
        {
            double metres = elevationFt * 0.3048;
            return qnhHectoPascal * Math.Pow(1 - 0.0065 * metres / 288.15, 5.25588);
        }

        public static string Format(double hectoPascal, PressureUnit unit)
        {
            return unit == PressureUnit.InchesOfMercury
                ? (hectoPascal / HectoPascalPerInch).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : Math.Round(hectoPascal).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The two names of a minimum: (altitude, height), e.g. ("OCA", "OCH").
        /// </summary>
        public static (string Altitude, string Height) Names(MinimaLabel label)
        {
            return label switch
            {
                MinimaLabel.OcaOch => ("OCA", "OCH"),
                MinimaLabel.MdaMdh => ("MDA", "MDH"),
                _ => ("DA", "DH")
            };
        }
    }

    /// <summary>A control group of the analog console: a knob or keys.</summary>
    internal enum AnalogControl
    {
        Knob,
        Keys
    }

    /// <summary>Readout windows of the analog console: 14-segment displays or mechanical drum counters.</summary>
    internal enum ReadoutStyle
    {
        Segments,
        Drums
    }

    /// <summary>Look of the keys of the analog console: grey console keys, or backlit keys as on the FIAR console.</summary>
    internal enum AnalogKeyStyle
    {
        Console,
        Fiar
    }

    /// <summary>Range of the analog console: a knob, one key per range, or keys down / preferred / up.</summary>
    internal enum AnalogRangeControl
    {
        Knob,
        RangeKeys,
        StepKeys,
        /// <summary>One key per range, as the range panel of the FIAR console: two columns of large square keys
        /// (number and NM), framed, with a key back to the preferred range under them.</summary>
        PanelKeys
    }

    /// <summary>Antenna tilt of the analog console: two knobs (EL, AZ), keys, or a small 4-way joystick.</summary>
    internal enum AnalogTiltControl
    {
        Knobs,
        Keys,
        Joystick
    }

    /// <summary>
    /// Display ranges, in NM: all those offered, and those in use (ticked in the active profile).
    /// </summary>
    internal static class Ranges
    {
        /// <summary>All the ranges offered; each profile ticks the ones it uses (<see cref="Profile.DisplayRanges"/>).</summary>
        public static readonly double[] All = [1, 2.5, 5, 10, 15, 20, 30, 40];
        /// <summary>Ranges of a profile that never chose (those of the older versions).</summary>
        public static readonly double[] Default = [1, 2.5, 5, 10, 15, 20];

        /// <summary>Ranges in use (ticked in the active profile), smallest first; never empty.</summary>
        public static double[] Values { get; set; } = Default;

        /// <summary>Ranges ticked in a profile: only offered values, smallest first, at least one.</summary>
        public static double[] Of(IEnumerable<double>? ranges)
        {
            double[] values = All.Where(a => ranges?.Any(r => Math.Abs(r - a) < 0.01) == true).ToArray();
            return values.Length > 0 ? values : Default;
        }

        /// <summary>
        /// Index of the range in use closest to the given one.
        /// </summary>
        public static int IndexOfClosest(double range) => IndexOfClosest(Values, range);

        /// <summary>Index of the value of the list closest to the given range.</summary>
        public static int IndexOfClosest(IReadOnlyList<double> values, double range)
        {
            int best = 0;
            for (int i = 1; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - range) < Math.Abs(values[best] - range))
                {
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The range in use closest to the given one.</summary>
        public static double Closest(double range) => Values[IndexOfClosest(range)];
    }

    /// <summary>
    /// A named set of user preferences. New preferences are added here as properties with a default value:
    /// profiles saved by older versions simply get the default for the missing ones.
    /// </summary>
    /// <summary>Where the horizontal DH line goes from the point where the glide path reaches the DH (side of the screen).</summary>
    internal enum DhHorizontalSide { Left, Right, Both, None }

    /// <summary>
    /// Last part of the glide path, approach limits and centreline (drawn solid): all solid, or dashed from the point
    /// where the glide path reaches the DH, or from the threshold, down to the touchdown point.
    /// </summary>
    internal enum FinalSegment { Solid, FromDh, FromThreshold }

    /// <summary>In which distance scale the horizontal altitude lines are drawn.</summary>
    /// <summary>Where a label starts (modern display): next to the track, or at 45° with a leader line.</summary>
    internal enum LabelPlacement
    {
        UpRight,
        UpLeft,
        DownRight,
        DownLeft,
        NextToTrack
    }

    internal enum AltitudeLinesScale { LogOnly, LinOnly, Both }

    /// <summary>Where the vertical DH line goes from that point.</summary>
    internal enum DhVerticalSide { Down, Up, Both, None }

    internal class Profile
    {
        public string Name { get; set; } = AppSettings.DefaultProfileName;
        /// <summary>
        /// Side of the screen where the runway is shown. With the runway on the right the profile view is
        /// mirrored horizontally and the azimuth view is rotated by 180°, so left/right of the approach stay correct.
        /// </summary>
        public RunwaySide RunwaySide { get; set; } = RunwaySide.Left;
        /// <summary>
        /// Display range used when the program starts.
        /// </summary>
        public StartupRange StartupRange { get; set; } = StartupRange.LastUsed;
        /// <summary>
        /// Display range used when another runway is selected.
        /// </summary>
        public RunwayChangeRange RunwayChangeRange { get; set; } = RunwayChangeRange.RunwayDefault;
        /// <summary>
        /// Preferred range in NM, used at start and/or on runway change when set to Fixed.
        /// </summary>
        public double PreferredRange { get; set; } = 15;
        /// <summary>
        /// Display ranges offered by the range controls (knob, keys, Page up / Page down, mouse wheel), from
        /// <see cref="Ranges.All"/>; at least one. Older profiles: the ranges up to 20 NM.
        /// </summary>
        public List<double> DisplayRanges { get; set; } = [.. Ranges.Default];

        // Radar equipment (see Radar): angles in degrees.
        /// <summary>Approach limits from the ideal glide path and centreline (green inside, red outside).</summary>
        public double ApproachAbove { get; set; } = 0.5;
        /// <summary>
        /// Upper approach limit in use. Off (some PARs have only the lower one): no upper line, and above the glide
        /// path the track keeps the "inside" colour, however high, until it goes below the lower limit.
        /// </summary>
        public bool UpperApproachLimit { get; set; } = true;
        /// <summary>Where the labels start: at 45° (up right by default) with a leader line, or next to the track.</summary>
        public LabelPlacement LabelPlacement { get; set; } = LabelPlacement.UpRight;
        /// <summary>Distance (px) from the track to the nearest corner of the label, along the 45° line.</summary>
        public double LabelOffsetDistance { get; set; } = DefaultLabelOffset;
        public const double DefaultLabelOffset = 40;
        public const double MinLabelOffset = 15;
        public const double MaxLabelOffset = 120;
        public double ApproachBelow { get; set; } = 0.5;
        public double ApproachLeft { get; set; } = 1.5;
        public double ApproachRight { get; set; } = 1.5;
        /// <summary>
        /// Safety limits (optional, off by default): lines outside the approach limits, from the touchdown point,
        /// in each view; only drawn (the track colour follows the approach limits), cut at the scan limits.
        /// </summary>
        public bool SafetyLimitsElevation { get; set; }
        public bool SafetyLimitsAzimuth { get; set; }
        public double SafetyAbove { get; set; } = 2;
        public double SafetyBelow { get; set; } = 2;
        public double SafetyLeft { get; set; } = 5;
        public double SafetyRight { get; set; } = 5;
        /// <summary>
        /// Scan limits from the antenna. Since <see cref="ScanModel"/> 1: the physical limits of the antenna (fixed).
        /// In older profiles (model 0): the limits of the old beam in neutral position, moved by the tilt up to
        /// <see cref="TiltMax"/> (converted by <see cref="MigrateScanModel"/>). The defaults are the old ones, so a
        /// new profile is converted in the same way.
        /// </summary>
        public double ScanUp { get; set; } = 8;
        public double ScanDown { get; set; } = -1;
        public double ScanLeft { get; set; } = 10;
        public double ScanRight { get; set; } = 10;
        /// <summary>Antenna tilt step.</summary>
        public double TiltStep { get; set; } = 2;
        /// <summary>Old maximum tilt: only read to convert the profiles of model 0 (the tilt range now follows from the beam).</summary>
        public double TiltMax { get; set; } = 10;
        /// <summary>
        /// 0: scan limits of the old model (beam = scan limits, moved by the tilt); 1: scan limits + antenna beam,
        /// converted with the scan limits widened by the old maximum tilt and the beam on (builds of October 2026);
        /// 2: scan limits + antenna beam, beam off by default. See <see cref="MigrateScanModel"/>.
        /// </summary>
        public int ScanModel { get; set; }
        public const int CurrentScanModel = 2;
        /// <summary>Antenna beam: width (degrees) and centre in neutral position (EL up from the horizon, AZ right of the centreline).</summary>
        public double BeamElevation { get; set; } = 9;
        public double BeamAzimuth { get; set; } = 20;
        public double BeamElevationNeutral { get; set; } = 3.5;
        public double BeamAzimuthNeutral { get; set; }
        /// <summary>Draw the edges of the antenna beam (off: the beam is shown only by the thicker range marks).</summary>
        public bool ShowBeamEdges { get; set; }
        /// <summary>
        /// Narrow antenna beam moved by the tilt (an advanced function, off by default). Off: the beam is the scan
        /// limits, no tilt.
        /// </summary>
        public bool BeamEnabled { get; set; }
        /// <summary>
        /// Elevation centre of the beam in neutral = the glide path angle in use (instead of
        /// <see cref="BeamElevationNeutral"/>). On by default.
        /// </summary>
        public bool BeamElevationNeutralAuto { get; set; } = true;
        /// <summary>
        /// Display mode locked in this profile: no button and no key A to switch it (to change it, change profile or
        /// untick this in the settings).
        /// </summary>
        public bool LockDisplayMode { get; set; }
        /// <summary>Decision height selector on the right panel (field, knob or keys): an advanced function, off by default (Shift+arrows always work).</summary>
        public bool ShowDhSelector { get; set; }
        /// <summary>
        /// Free CRS selector (optional, off by default): the final course can be changed on the fly for the session,
        /// e.g. on a ship. Modern: box with − / +; analog: keys − RWY +.
        /// </summary>
        public bool ShowCourseSelector { get; set; }
        /// <summary>Analog scope: rain clutter (patches and dots in the beam, from the METAR) and the CLUTTER knob. Off by default.</summary>
        public bool RainClutter { get; set; }
        /// <summary>Analog console: the controls of each group as a knob or as keys (the tilt also as a small joystick).</summary>
        public AnalogRangeControl RangeControl { get; set; } = AnalogRangeControl.Knob;
        /// <summary>Text of the key back to the preferred range (<see cref="AnalogRangeControl.StepKeys"/>), at most 5 characters.</summary>
        public string RangeDefaultKey { get; set; } = "DEF";
        /// <summary>The key/button back to the preferred range shows that range (e.g. 15NM) instead of <see cref="RangeDefaultKey"/>.</summary>
        public bool RangeDefaultMiles { get; set; }

        /// <summary>Text of the key/button back to the preferred range: the range in NM or the text chosen.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string PreferredRangeText => RangeDefaultMiles
            ? PreferredRange.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "NM"
            : RangeDefaultKey;
        public AnalogTiltControl TiltControl { get; set; } = AnalogTiltControl.Knobs;
        public AnalogControl DhControl { get; set; } = AnalogControl.Knob;
        public AnalogControl BrightnessControl { get; set; } = AnalogControl.Knob;
        /// <summary>Readout windows of the analog console (airport, runway, course, ...): segments or drums.</summary>
        public ReadoutStyle Readouts { get; set; } = ReadoutStyle.Segments;
        /// <summary>Look of all the keys of the analog console (groups, runway, approach, system keys).</summary>
        public AnalogKeyStyle KeyStyle { get; set; } = AnalogKeyStyle.Console;
        /// <summary>
        /// Coasting tracks (modern display): seconds a track out of the beam is still shown at its estimated
        /// position (0 = hidden at once).
        /// </summary>
        public double CoastSeconds { get; set; } = 8;
        /// <summary>Track filter of the modern display: smooth tracks (see TrackFilter).</summary>
        public TrackSmoothing TrackSmoothing { get; set; } = TrackSmoothing.Light;
        public const double MaxCoastSeconds = 30;
        /// <summary>Largest scan limit angle (a limit near 90° would be drawn far away).</summary>
        public const double MaxScanAngle = 80;
        /// <summary>
        /// Azimuth tilt left/right swapped: false (default) = left/right as seen by the pilot flying the approach;
        /// true = as seen from the runway looking at the approach (controller's view). Applies to the AZ TILT knob,
        /// the arrow keys, the buttons and the L/R readouts; the antenna itself is the same.
        /// </summary>
        public bool AzimuthTiltSwapped { get; set; }

        // Units and references.
        public PressureReference PressureReference { get; set; } = PressureReference.QNH;
        public PressureUnit PressureUnit { get; set; } = PressureUnit.HectoPascal;
        public MinimaLabel MinimaLabel { get; set; } = MinimaLabel.DaDh;
        public bool ShowAltitudeScale { get; set; } = true;
        /// <summary>Horizontal lines every 1000 (in the unit of the scale) in the vertical view: on by default (see <see cref="AltitudeLinesIn"/>).</summary>
        public bool ShowAltitudeLines { get; set; } = true;
        /// <summary>The altitude lines are drawn in the log scale only (default), in the linear one only, or in both.</summary>
        public AltitudeLinesScale AltitudeLinesIn { get; set; } = AltitudeLinesScale.LogOnly;

        /// <summary>Altitude lines to draw now: the option, in the distance scale in use.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool AltitudeLinesNow => ShowAltitudeLines && AltitudeLinesIn switch
        {
            AltitudeLinesScale.LogOnly => CompressedScale,
            AltitudeLinesScale.LinOnly => !CompressedScale,
            _ => true
        };
        /// <summary>
        /// Compressed distance scale: the last miles before the runway get more room, the far ones less, so the
        /// traffic stays in view without changing the range. Every line starting at the touchdown point (glide path,
        /// approach limits, centreline) stays straight; heights and lateral offsets shrink with the distance.
        /// </summary>
        public bool CompressedScale { get; set; }
        /// <summary>Compressed scale: distance from the touchdown (NM) within which the scale stays almost linear.</summary>
        public double CompressionKnee { get; set; } = DefaultCompressionKnee;
        public const double DefaultCompressionKnee = 3;
        public const double MinCompressionKnee = 0.5;
        public const double MaxCompressionKnee = 20;
        /// <summary>
        /// Unit of heights: altitude scale and labels (altitude, deviations in ft or m, vertical speed in ft/min or m/s).
        /// </summary>
        public LengthUnit AltitudeScaleUnit { get; set; } = LengthUnit.Feet;

        // Tracks and labels.
        public LabelLayout ElevationLabel { get; set; } = LabelLayout.DefaultElevation();
        public LabelLayout AzimuthLabel { get; set; } = LabelLayout.DefaultAzimuth();
        /// <summary>Track ID label field: a random two-digit ID for each new track (an assigned ID is shown anyway).</summary>
        public bool RandomTrackIds { get; set; } = true;
        /// <summary>History tails: previous positions of each track.</summary>
        public bool HistoryEnabled { get; set; } = true;
        public int HistoryDots { get; set; } = 50;
        /// <summary>
        /// Seconds between two history dots. Aurora sends a position every 0.5 s: one dot per update made the
        /// tail look like a continuous line.
        /// </summary>
        public double HistoryInterval { get; set; } = 2;
        public SymbolSetting TrackSymbol { get; set; } = new(SymbolShape.CrossCircle, 12);
        public SymbolSetting ThresholdSymbol { get; set; } = new(SymbolShape.Line, 10);
        public SymbolSetting TouchdownSymbol { get; set; } = new(SymbolShape.Line, 12);
        public SymbolSetting AntennaSymbol { get; set; } = new(SymbolShape.Square, 8);
        /// <summary>Symbol on the point where the glide path reaches the decision height (none by default).</summary>
        public SymbolSetting DhSymbol { get; set; } = new(SymbolShape.None, 10);
        /// <summary>Horizontal DH line, drawn from the point where the glide path reaches the DH: side on the screen and length (NM) on each side.</summary>
        public DhHorizontalSide DhLineSide { get; set; } = DhHorizontalSide.Both;
        public double DhLineLength { get; set; } = 1;
        /// <summary>Last part of the solid glide path, limits and centreline: solid (default), or dashed / dotted from the DH or from the threshold.</summary>
        public FinalSegment FinalSegment { get; set; } = FinalSegment.Solid;
        public LineDash FinalSegmentDash { get; set; } = LineDash.Dashed;
        /// <summary>Vertical DH line from the same point: direction and length in ft (0 = as long as the DH, down to the ground).</summary>
        public DhVerticalSide DhDropSide { get; set; } = DhVerticalSide.Down;
        public double DhDropLength { get; set; }
        /// <summary>DH line of the azimuth view: length on each side of the centreline in metres (0 = up to the scan limits), and a symbol on the centreline.</summary>
        public double DhAzimuthLength { get; set; }
        public SymbolSetting DhAzimuthSymbol { get; set; } = new(SymbolShape.None, 10);
        public SymbolSetting HistorySymbol { get; set; } = new(SymbolShape.FilledCircle, 3);
        /// <summary>Symbol of a coasting track (estimated position, out of the beam).</summary>
        public SymbolSetting CoastSymbol { get; set; } = new(SymbolShape.Diamond, 12);

        /// <summary>Antenna scan effect: a sweeping beam drawn over the views, graphic only (no effect on the data).</summary>
        public bool ScanEffect { get; set; } = true;
        /// <summary>Modern display, or analog scope (round phosphor screen, echoes lit by the beam, no labels, knobs).</summary>
        public DisplayMode DisplayMode { get; set; } = DisplayMode.Modern;
        /// <summary>Range marks drawn at each display range.</summary>
        public RangeMarkSettings RangeMarks { get; set; } = RangeMarkSettings.Default();
        /// <summary>Colours, dash styles and widths of the modern display.</summary>
        public DisplayStyleSettings Style { get; set; } = DisplayStyleSettings.CreateDefault();
        /// <summary>Phosphor colour of the analog scope (#RRGGBB).</summary>
        public string AnalogColor { get; set; } = "#A8FF60";
        /// <summary>
        /// Analog scope: length of the echo at the far end of the range, compared with the one at the touchdown
        /// (1 = always the same; the beam widens with the distance on real scopes).
        /// </summary>
        public double EchoGrowth { get; set; } = 1;
        public const double MaxEchoGrowth = 4;
        /// <summary>
        /// Brightness of the radar picture, percent (100 = normal), modern display and analog scope. Above 100 the
        /// colours are boosted (for dim monitors).
        /// </summary>
        public int BrightnessModern { get; set; } = 100;
        public int BrightnessAnalog { get; set; } = 100;
        public const int MinBrightness = 10;
        public const int BrightnessStep = 10;
        public const int MaxBrightness = 150;
        /// <summary>Distance reminders for all runways (each runway can have its own too, see AppSettings).</summary>
        public List<DistanceReminder> Reminders { get; set; } = [];
        /// <summary>
        /// Elevation view: distance text below the horizon line and reminder markers above it; false: text above, markers below.
        /// </summary>
        public bool RangeTextBelowHorizon { get; set; }
        /// <summary>
        /// Default magnetic variation (degrees, East positive) for the final course of runways without their own
        /// value in runways.par (the headings in the file are true).
        /// </summary>
        public double MagneticVariation { get; set; }
        public ScanEffectSpeed ScanEffectSpeed { get; set; } = ScanEffectSpeed.Normal;

        public const int MinHistoryDots = 3;
        public const int MaxHistoryDots = 100;
        public const double MinHistoryInterval = 0.5;
        public const double MaxHistoryInterval = 10;

        /// <summary>
        /// Repairs values missing or out of range (profiles from older versions or edited by hand).
        /// </summary>
        public void Normalize()
        {
            ElevationLabel ??= LabelLayout.DefaultElevation();
            AzimuthLabel ??= LabelLayout.DefaultAzimuth();
            ElevationLabel.Normalize();
            AzimuthLabel.Normalize();
            HistoryDots = Math.Clamp(HistoryDots, MinHistoryDots, MaxHistoryDots);
            HistoryInterval = double.IsNaN(HistoryInterval) ? 2 : Math.Clamp(HistoryInterval, MinHistoryInterval, MaxHistoryInterval);
            if (!Enum.IsDefined(ScanEffectSpeed)) ScanEffectSpeed = ScanEffectSpeed.Normal;
            if (!Enum.IsDefined(TrackSmoothing)) TrackSmoothing = TrackSmoothing.Light;
            if (!Enum.IsDefined(DisplayMode)) DisplayMode = DisplayMode.Modern;
            if (!Enum.IsDefined(RangeControl)) RangeControl = AnalogRangeControl.Knob;
            DisplayRanges = [.. Ranges.Of(DisplayRanges)];
            if (!Enum.IsDefined(TiltControl)) TiltControl = AnalogTiltControl.Knobs;
            if (!Enum.IsDefined(DhControl)) DhControl = AnalogControl.Knob;
            if (!Enum.IsDefined(LabelPlacement)) LabelPlacement = LabelPlacement.UpRight;
            LabelOffsetDistance = double.IsNaN(LabelOffsetDistance) ? DefaultLabelOffset : Math.Clamp(Math.Round(LabelOffsetDistance), MinLabelOffset, MaxLabelOffset);
            // Safety limits: always wider than the approach limits (also after these are widened), within sane angles.
            static double Wider(double safety, double approach, double max) =>
                Math.Clamp(double.IsNaN(safety) ? max : safety, Math.Min(approach + 0.1, max), max);
            SafetyAbove = Wider(SafetyAbove, ApproachAbove, 15);
            SafetyBelow = Wider(SafetyBelow, ApproachBelow, 15);
            SafetyLeft = Wider(SafetyLeft, ApproachLeft, 30);
            SafetyRight = Wider(SafetyRight, ApproachRight, 30);
            if (!Enum.IsDefined(BrightnessControl)) BrightnessControl = AnalogControl.Knob;
            if (!Enum.IsDefined(Readouts)) Readouts = ReadoutStyle.Segments;
            if (!Enum.IsDefined(KeyStyle)) KeyStyle = AnalogKeyStyle.Console;
            if (!Enum.IsDefined(AltitudeLinesIn)) AltitudeLinesIn = AltitudeLinesScale.LogOnly;
            RangeDefaultKey = (RangeDefaultKey ?? "").Trim().ToUpperInvariant();
            if (RangeDefaultKey.Length > MaxRangeKeyText) RangeDefaultKey = RangeDefaultKey[..MaxRangeKeyText];
            if (RangeDefaultKey.Length == 0) RangeDefaultKey = "DEF";
            RangeMarks ??= RangeMarkSettings.Default();
            RangeMarks.Normalize();
            Style ??= DisplayStyleSettings.CreateDefault();
            Style.Normalize();
            if (!ColorText.TryParse(AnalogColor, out _)) AnalogColor = "#A8FF60";
            EchoGrowth = double.IsFinite(EchoGrowth) ? Math.Clamp(EchoGrowth, 1, MaxEchoGrowth) : 1;
            CompressionKnee = double.IsFinite(CompressionKnee) && CompressionKnee > 0 ? Math.Clamp(CompressionKnee, MinCompressionKnee, MaxCompressionKnee) : DefaultCompressionKnee;
            BrightnessModern = Math.Clamp(BrightnessModern, MinBrightness, MaxBrightness);
            BrightnessAnalog = Math.Clamp(BrightnessAnalog, MinBrightness, MaxBrightness);
            Reminders ??= [];
            DistanceReminder.Normalize(Reminders);
            if (double.IsNaN(MagneticVariation) || Math.Abs(MagneticVariation) > 90) MagneticVariation = 0;
            TrackSymbol = NormalizeSymbol(TrackSymbol, new(SymbolShape.CrossCircle, 12));
            ThresholdSymbol = NormalizeSymbol(ThresholdSymbol, new(SymbolShape.Line, 10));
            TouchdownSymbol = NormalizeSymbol(TouchdownSymbol, new(SymbolShape.Line, 12));
            AntennaSymbol = NormalizeSymbol(AntennaSymbol, new(SymbolShape.Square, 8));
            DhSymbol = NormalizeSymbol(DhSymbol, new(SymbolShape.None, 10));
            if (!Enum.IsDefined(DhLineSide)) DhLineSide = DhHorizontalSide.Both;
            if (!Enum.IsDefined(DhDropSide)) DhDropSide = DhVerticalSide.Down;
            if (!Enum.IsDefined(FinalSegment)) FinalSegment = FinalSegment.Solid;
            if (FinalSegmentDash is not (LineDash.Dashed or LineDash.Dotted)) FinalSegmentDash = LineDash.Dashed;
            // 0 = up to the edge of the view.
            DhLineLength = double.IsNaN(DhLineLength) || DhLineLength < 0 ? 1 : DhLineLength == 0 ? 0 : Math.Clamp(DhLineLength, 0.25, 5);
            DhDropLength = double.IsNaN(DhDropLength) || DhDropLength < 0 ? 0 : Math.Clamp(DhDropLength, 0, 1000);
            DhAzimuthLength = double.IsNaN(DhAzimuthLength) || DhAzimuthLength < 0 ? 0 : Math.Clamp(DhAzimuthLength, 0, 2000);
            DhAzimuthSymbol = NormalizeSymbol(DhAzimuthSymbol, new(SymbolShape.None, 10));
            HistorySymbol = NormalizeSymbol(HistorySymbol, new(SymbolShape.FilledCircle, 3));
            CoastSymbol = NormalizeSymbol(CoastSymbol, new(SymbolShape.Diamond, 12));
            CoastSeconds = double.IsNaN(CoastSeconds) ? 8 : Math.Clamp(CoastSeconds, 0, MaxCoastSeconds);
            MigrateScanModel();
            if (double.IsNaN(TiltStep) || TiltStep <= 0) TiltStep = 2;
            if (double.IsNaN(ScanUp) || double.IsNaN(ScanDown) || ScanUp <= ScanDown) { ScanUp = 18; ScanDown = -11; }
            ScanUp = Math.Min(ScanUp, MaxScanAngle);
            ScanDown = Math.Max(ScanDown, -MaxScanAngle);
            if (ScanUp <= ScanDown) { ScanUp = 18; ScanDown = -11; }
            ScanLeft = double.IsNaN(ScanLeft) || ScanLeft <= 0 ? 20 : Math.Min(ScanLeft, MaxScanAngle);
            ScanRight = double.IsNaN(ScanRight) || ScanRight <= 0 ? 20 : Math.Min(ScanRight, MaxScanAngle);
            BeamElevation = double.IsNaN(BeamElevation) ? 9 : Math.Clamp(BeamElevation, 0.1, 90);
            BeamAzimuth = double.IsNaN(BeamAzimuth) ? 20 : Math.Clamp(BeamAzimuth, 0.1, 180);
            if (double.IsNaN(BeamElevationNeutral)) BeamElevationNeutral = (ScanUp + ScanDown) / 2;
            if (double.IsNaN(BeamAzimuthNeutral)) BeamAzimuthNeutral = 0;
        }

        /// <summary>
        /// Profile created when there is none yet (first start): wide scan limits that work at once, no antenna beam
        /// and no tilt (advanced functions, turned on in the settings).
        /// </summary>
        public static Profile CreateNew()
        {
            Profile profile = new()
            {
                ScanModel = CurrentScanModel,
                ScanUp = 10,
                ScanDown = -1,
                ScanLeft = 15,
                ScanRight = 15,
                BeamEnabled = false
            };
            profile.NarrowBeam();
            profile.Normalize();
            return profile;
        }

        /// <summary>
        /// Beam a few degrees narrower than the scan limits, centred: set when the antenna beam is turned on, so the
        /// tilt has some room at once and the user sees how it works.
        /// </summary>
        /// <param name="onlyWhereFull">Only an axis whose beam is as wide as the scan limits (a beam already set is kept).</param>
        public void NarrowBeam(bool onlyWhereFull = false)
        {
            if (!onlyWhereFull || BeamElevation >= ScanUp - ScanDown - 0.01)
            {
                BeamElevation = Math.Max(1, ScanUp - ScanDown - 4);
                BeamElevationNeutral = (ScanUp + ScanDown) / 2;
            }
            if (!onlyWhereFull || BeamAzimuth >= ScanLeft + ScanRight - 0.01)
            {
                BeamAzimuth = Math.Max(1, ScanLeft + ScanRight - 6);
                BeamAzimuthNeutral = (ScanRight - ScanLeft) / 2;
            }
        }

        /// <summary>
        /// Brings a profile of an earlier scan model to the current one, once. The antenna beam (an advanced function)
        /// is then off, and the scan limits are those the profile showed before the antenna beam existed:
        /// - model 0 (version 1): the old scan limits stay as they are (the old tilt is no longer there);
        /// - model 1 (converted by the first builds with the beam: scan limits widened by the old maximum tilt, beam =
        ///   the old sector): the scan limits go back to the old sector, when the values show it (all four widened by
        ///   the same tilt); otherwise the defaults of a new profile.
        /// The beam values are set a few degrees narrower than the scan limits, ready for when it is turned on.
        /// </summary>
        public void MigrateScanModel()
        {
            if (ScanModel >= CurrentScanModel) return;
            if (ScanModel <= 0)
            {
                // Old values not valid: the old defaults.
                if (double.IsNaN(ScanUp) || double.IsNaN(ScanDown) || ScanUp <= ScanDown) { ScanUp = 8; ScanDown = -1; }
                if (double.IsNaN(ScanLeft) || ScanLeft <= 0) ScanLeft = 10;
                if (double.IsNaN(ScanRight) || ScanRight <= 0) ScanRight = 10;
            }
            else
            {
                // The old sector is the beam in neutral position; the scan limits around it all widened by the same tilt.
                double up = BeamElevationNeutral + BeamElevation / 2;
                double down = BeamElevationNeutral - BeamElevation / 2;
                double right = BeamAzimuthNeutral + BeamAzimuth / 2;
                double left = BeamAzimuth / 2 - BeamAzimuthNeutral;
                double tilt = ScanUp - up;
                bool oldSector = !double.IsNaN(tilt) && tilt >= -0.01
                    && Math.Abs(down - ScanDown - tilt) < 0.01
                    && Math.Abs(ScanLeft - left - tilt) < 0.01
                    && Math.Abs(ScanRight - right - tilt) < 0.01
                    && up > down && left > 0 && right > 0;
                if (oldSector)
                {
                    ScanUp = up;
                    ScanDown = down;
                    ScanLeft = left;
                    ScanRight = right;
                }
                else
                {
                    ScanUp = 10;
                    ScanDown = -1;
                    ScanLeft = 15;
                    ScanRight = 15;
                }
            }
            BeamEnabled = false;
            NarrowBeam();
            ScanModel = CurrentScanModel;
        }

        private static SymbolSetting NormalizeSymbol(SymbolSetting? symbol, SymbolSetting fallback)
        {
            if (symbol == null || !Enum.IsDefined(symbol.Shape)) return fallback;
            symbol.Size = Math.Clamp(symbol.Size, 2, 60);
            if (symbol.Cells != null && symbol.Cells.Length != Symbols.CustomBox * Symbols.CustomBox) symbol.Cells = null;
            // A custom shape without a valid path: the default one.
            if (symbol.Shape == SymbolShape.Custom && Symbols.CreateCustom(symbol.Path, symbol.Size) == null)
            {
                symbol.Shape = fallback.Shape;
            }
            return symbol;
        }

        public const int MaxRangeKeyText = 5;

        /// <summary>
        /// Deep copy (through JSON, so it stays correct when nested settings are added).
        /// </summary>
        public Profile Clone()
        {
            return JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
        }
    }

    /// <summary>
    /// Main window bounds in its normal (not maximized) state, plus whether it was maximized.
    /// </summary>
    internal class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    /// <summary>
    /// Everything saved in the settings file: the profiles, the active one and the last session state.
    /// </summary>
    internal class AppSettings
    {
        public const string DefaultProfileName = "Default";
        public int Version { get; set; } = 1;
        public string ActiveProfile { get; set; } = DefaultProfileName;
        public List<Profile> Profiles { get; set; } = [];
        /// <summary>
        /// Runway selected when the program was closed ("ICAO DESIGNATOR"), restored at start.
        /// </summary>
        public string? LastRunway { get; set; }
        /// <summary>
        /// Display range (NM) used when the program was closed, restored together with the runway.
        /// </summary>
        public double? LastRange { get; set; }
        /// <summary>
        /// Glide path angle of the approach in use when the program was closed (runways with several approaches in
        /// runways.par); only a published one is restored.
        /// </summary>
        public double? LastGlideSlope { get; set; }
        /// <summary>
        /// Size and position of the main window when the program was closed, restored at start.
        /// </summary>
        public WindowPlacement? Window { get; set; }
        /// <summary>
        /// Distance reminders of single runways, by "ICAO DESIGNATOR" (in addition to those of the profile).
        /// </summary>
        public Dictionary<string, List<DistanceReminder>> RunwayReminders { get; set; } = [];
        /// <summary>Coordination light panel with the tower.</summary>
        public CoordinationSettings Coordination { get; set; } = new();
        /// <summary>Settings window: the advanced options shown too (off: only the essentials).</summary>
        public bool ShowAdvancedSettings { get; set; }
        /// <summary>Joystick or gamepad: test aircraft and antenna tilt.</summary>
        public JoystickSettings Joystick { get; set; } = new();

        /// <summary>Reminders of a runway (created empty if needed).</summary>
        public List<DistanceReminder> RemindersOf(string runway)
        {
            if (!RunwayReminders.TryGetValue(runway, out List<DistanceReminder>? list) || list == null)
            {
                list = [];
                RunwayReminders[runway] = list;
            }
            return list;
        }

        /// <summary>Reminders drawn for a runway: those of the active profile, then those of the runway.</summary>
        public IEnumerable<DistanceReminder> RemindersFor(Runway runway)
        {
            IEnumerable<DistanceReminder> own = RunwayReminders.TryGetValue(runway.ToString(), out List<DistanceReminder>? list) && list != null ? list : [];
            return Active.Reminders.Concat(own);
        }

        /// <summary>
        /// Reminders drawn for a runway with several approaches (lines of the file with different names, e.g.
        /// "LIPC 11 2.8" and "LIPC 11 2.5"): those of the profile, then those saved for any of its lines.
        /// </summary>
        public IEnumerable<DistanceReminder> RemindersFor(IEnumerable<string> runwayNames)
        {
            IEnumerable<DistanceReminder> own = runwayNames.Distinct()
                .SelectMany(name => RunwayReminders.TryGetValue(name, out List<DistanceReminder>? list) && list != null ? list : []);
            return Active.Reminders.Concat(own);
        }

        [JsonIgnore]
        public Profile Active => Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];

        /// <summary>
        /// Repairs a file edited by hand or written by another version: at least one profile,
        /// unique non-empty names, a valid active profile.
        /// </summary>
        public void Normalize()
        {
            Profiles ??= [];
            RunwayReminders ??= [];
            Coordination ??= new();
            Coordination.Normalize();
            Joystick ??= new();
            Joystick.Normalize();
            foreach (string key in RunwayReminders.Keys.ToList())
            {
                if (RunwayReminders[key] == null || RunwayReminders[key].Count == 0) RunwayReminders.Remove(key);
                else DistanceReminder.Normalize(RunwayReminders[key]);
            }
            List<Profile> valid = Profiles.Where(p => p != null).ToList();
            Profiles = [];
            foreach (Profile p in valid)
            {
                p.Normalize();
                p.Name = UniqueName(string.IsNullOrWhiteSpace(p.Name) ? "Profile" : p.Name.Trim());
                Profiles.Add(p);
            }
            if (Profiles.Count == 0)
            {
                Profiles.Add(Profile.CreateNew());
            }
            if (!Profiles.Any(p => p.Name == ActiveProfile))
            {
                ActiveProfile = Profiles[0].Name;
            }
        }

        /// <summary>
        /// The given name, or the name followed by " (2)", " (3)"... if already used.
        /// </summary>
        public string UniqueName(string name)
        {
            if (!Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
            for (int i = 2; ; i++)
            {
                string candidate = $"{name} ({i})";
                if (!Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Reads and writes the settings file.
    /// Location: the user's settings folder (Windows %AppData%\AuroraPAR, Linux ~/.config/AuroraPAR, macOS the
    /// equivalent), so settings survive program updates and work on every platform.
    /// Portable mode: if a file named <see cref="FileName"/> exists next to the program (even empty), that one is used.
    /// </summary>
    internal static class SettingsStore
    {
        public const string FileName = "AuroraPAR.settings.json";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string PortablePath => Path.Combine(AppContext.BaseDirectory, FileName);
        public static string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPAR", FileName);
        public static bool IsPortable => File.Exists(PortablePath);
        public static string FilePath => IsPortable ? PortablePath : UserPath;

        /// <summary>
        /// Loads the settings. A missing or empty file gives the defaults. A damaged file is kept as
        /// "...json.bad" (so nothing is lost) and the defaults are used.
        /// </summary>
        public static AppSettings Load()
        {
            string path = FilePath;
            AppSettings? settings = null;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                }
            }
            catch (Exception)
            {
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (Exception) { }
                settings = null;
            }
            settings ??= NewSettings();
            settings.Normalize();
            return settings;
        }

        /// <summary>Folder of the profiles that come with the program (first start: they are the profiles).</summary>
        public static string BundledProfilesPath => Path.Combine(AppContext.BaseDirectory, "Profiles");

        /// <summary>
        /// Settings of a first start (no settings file yet): the profiles that come with the program (Profiles folder,
        /// in name order, the first one active), or the default profile when there are none.
        /// </summary>
        private static AppSettings NewSettings()
        {
            AppSettings settings = new();
            try
            {
                if (Directory.Exists(BundledProfilesPath))
                {
                    foreach (string file in Directory.GetFiles(BundledProfilesPath, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Profile? profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), JsonOptions);
                            if (profile != null) settings.Profiles.Add(profile);
                        }
                        catch (Exception)
                        {
                            // A damaged file: skipped.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Folder not readable: the default profile.
            }
            if (settings.Profiles.Count > 0) settings.ActiveProfile = settings.Profiles[0].Name;
            return settings;
        }

        /// <summary>
        /// Saves the settings. The file is written to a temporary file first and then moved,
        /// so a crash while saving cannot leave a half-written settings file.
        /// Returns the error message, or null on success.
        /// </summary>
        public static string? Save(AppSettings settings)
        {
            string path = FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temp, path, overwrite: true);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static void ExportProfile(Profile profile, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
        }

        /// <summary>
        /// Reads a profile exported with <see cref="ExportProfile"/>. Throws if the file is not a valid profile.
        /// </summary>
        public static Profile ImportProfile(string path)
        {
            Profile? profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), JsonOptions);
            if (profile == null)
            {
                throw new InvalidDataException("The file does not contain an AuroraPAR profile.");
            }
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = Path.GetFileNameWithoutExtension(path);
            }
            profile.Normalize();
            return profile;
        }
    }
}
