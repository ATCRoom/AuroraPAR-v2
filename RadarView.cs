using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Display options of the views, taken from the active profile (plus the labels on/off switch of the session).
    /// </summary>
    internal class ViewOptions
    {
        /// <summary>Incremented when the options change, so tracks rebuild their symbol and label.</summary>
        public int Version { get; set; }
        /// <summary>True: heights above the threshold (QFE); false: altitudes (QNH).</summary>
        public bool Qfe { get; set; }
        public bool ShowAltitudeScale { get; set; } = true;
        /// <summary>Horizontal lines every 1000 in the vertical view.</summary>
        public bool ShowAltitudeLines { get; set; }
        /// <summary>Heights, deviations and vertical speed in metres (true) or feet (false).</summary>
        public bool ScaleInMetres { get; set; }
        /// <summary>Labels shown (switch of the session, not saved).</summary>
        public bool ShowLabels { get; set; } = true;
        /// <summary>Fictitious track IDs of the session, shared by the two views.</summary>
        public TrackIdentities Identities { get; } = new();
        public bool HistoryEnabled { get; set; } = true;
        public int HistoryDots { get; set; } = 50;
        /// <summary>Seconds between two history dots.</summary>
        public double HistoryInterval { get; set; } = 2;
        public SymbolSetting TrackSymbol { get; set; } = new(SymbolShape.CrossCircle, 12);
        public SymbolSetting ThresholdSymbol { get; set; } = new(SymbolShape.Line, 10);
        public SymbolSetting TouchdownSymbol { get; set; } = new(SymbolShape.Line, 12);
        public SymbolSetting AntennaSymbol { get; set; } = new(SymbolShape.Square, 8);
        public SymbolSetting DhSymbol { get; set; } = new(SymbolShape.None, 10);
        public DhHorizontalSide DhLineSide { get; set; } = DhHorizontalSide.Both;
        public double DhLineLength { get; set; } = 1;
        public DhVerticalSide DhDropSide { get; set; } = DhVerticalSide.Down;
        public double DhDropLength { get; set; }
        public double DhAzimuthLength { get; set; }
        public SymbolSetting DhAzimuthSymbol { get; set; } = new(SymbolShape.None, 10);
        public SymbolSetting HistorySymbol { get; set; } = new(SymbolShape.FilledCircle, 3);
        /// <summary>Coasting tracks (modern display): symbol, and seconds shown out of the beam (0 = none).</summary>
        public SymbolSetting CoastSymbol { get; set; } = new(SymbolShape.Diamond, 12);
        public double CoastSeconds { get; set; } = 8;
        /// <summary>Edges of the antenna beam drawn (otherwise only the thicker range marks show it).</summary>
        public bool ShowBeamEdges { get; set; }
        public LabelLayout ElevationLabel { get; set; } = LabelLayout.DefaultElevation();
        public LabelLayout AzimuthLabel { get; set; } = LabelLayout.DefaultAzimuth();
        /// <summary>Analog scope: monochrome phosphor, echoes lit by the beam, no labels.</summary>
        public bool Analog { get; set; }
        /// <summary>Analog scope: length of the echo at the far end of the range compared with the touchdown (1 = same).</summary>
        public double EchoGrowth { get; set; } = 1;
        /// <summary>Analog scope: rain clutter strength 0 to 1 (0 = none) and CLUTTER filter 0 to 1 (session values).</summary>
        public double ClutterStrength { get; set; }
        public double ClutterFilter { get; set; }
        /// <summary>Wind that moves the clutter patches, in knots: along the final course towards the runway (tailwind) and to the right of the flight direction.</summary>
        public double ClutterWindAlongKt { get; set; }
        public double ClutterWindRightKt { get; set; }
        /// <summary>Colours, widths and dash styles of the elements.</summary>
        public Theme Theme { get; set; } = Theme.Modern(DisplayStyleSettings.CreateDefault());
        /// <summary>Range marks drawn at each range.</summary>
        public RangeMarkSettings RangeMarks { get; set; } = RangeMarkSettings.Default();
        /// <summary>Distance reminders of a runway (those of the profile and those of the runway).</summary>
        public Func<Runway, IEnumerable<DistanceReminder>> Reminders { get; set; } = _ => [];
        /// <summary>Distance text below the horizon line and reminder markers above it (otherwise the opposite).</summary>
        public bool RangeTextBelowHorizon { get; set; }
        /// <summary>
        /// Compressed distance scale: knee in NM from the touchdown (see <see cref="RadarView.CompressNM"/>);
        /// 0 = linear scale.
        /// </summary>
        public double DistanceKnee { get; set; }
    }

    /// <summary>
    /// Common part of the profile (elevation) and azimuth views.
    ///
    /// Static elements (runway, scan and approach limits, range marks...) are built only when something they
    /// depend on changes (runway, range, window size, orientation, settings): see <see cref="Invalidate"/>.
    /// Aircraft tracks (symbol, history dots, label, leader line) are created once per callsign and then only
    /// moved and updated.
    ///
    /// Labels: by default 45° up-right of the track; they can be dragged with the mouse (a leader line then joins
    /// them to the track), double click puts them back, right click hides one, right click on the track shows it again.
    ///
    /// The drawing code of the views works in "logical" coordinates, in pixels, as if the runway were on the left
    /// (x grows from the runway towards the approach). <see cref="ToScreenX"/> and <see cref="ToScreenY"/>
    /// mirror them when the runway is shown on the right.
    /// </summary>
    internal abstract class RadarView
    {
        private const int StaticZIndex = 0;
        private const int ClutterZIndex = 3;
        private const int SweepZIndex = 5;
        private const int HistoryZIndex = 9;
        private const int TrackZIndex = 11;
        private const int LabelZIndex = 12;
        /// <summary>Default label position: this many pixels right of and above the track (45°).</summary>
        private const double LabelDistance = 14;

        protected readonly Canvas Canvas;
        protected Runway Runway;
        /// <summary>
        /// Approach limits, scan limits and antenna tilt (shared by both views).
        /// </summary>
        protected readonly Radar Radar;
        protected readonly ViewOptions Options;
        protected double xscale = 1;
        protected double yscale = 1;
        protected bool RunwayOnRight { get; private set; }

        private bool staticValid = false;
        private readonly List<UIElement> staticElements = [];
        private readonly Dictionary<string, Track> tracks = [];
        /// <summary>Label being dragged with the mouse, and where the drag started.</summary>
        private Track? dragging;
        /// <summary>Shared geometry of the history dots, rebuilt when the options change.</summary>
        private Geometry historyGeometry = Geometry.Empty;
        private int historyGeometryVersion = -1;
        private Point dragStart;
        /// <summary>Clock of the history dots.</summary>
        private static readonly System.Diagnostics.Stopwatch HistoryClock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>Lines of the antenna scan effect (beam and glow), created when first needed.</summary>
        private readonly List<Polyline> sweepLines = [];
        private readonly ClutterLayer clutter = new();
        private bool clutterAdded;
        /// <summary>Afterglow of the echo: seconds between two copies, and strength of each copy (newest first).</summary>
        private const double GhostSpacing = 0.8;
        private static readonly double[] GhostStrengths = [0.4, 0.24, 0.12];
        /// <summary>Echo of an aircraft on the analog scope: a small blob, longer along the range.</summary>
        /// <summary>Echo of the analog scope, sized by the track symbol size (see <see cref="CreateEchoGeometry"/>).</summary>
        private Geometry echoGeometry = CreateEchoGeometry(12);
        private int echoVersion = -1;

        private Geometry EchoGeometry
        {
            get
            {
                if (echoVersion != Options.Version)
                {
                    echoGeometry = CreateEchoGeometry(Options.TrackSymbol.Size);
                    echoVersion = Options.Version;
                }
                return echoGeometry;
            }
        }
        private Vector dragStartOffset;

        /// <summary>
        /// Screen elements and state of one aircraft, reused at every refresh.
        /// </summary>
        protected sealed class Track
        {
            public readonly Path Symbol = new() { StrokeThickness = 2 };
            public readonly Border Label = new() { Background = Brushes.Transparent, Padding = new Thickness(1) };
            public TextBlock[] Cells = [];
            public int LayoutVersion = -1;
            public int SymbolVersion = -1;
            public readonly Line Leader = new() { StrokeThickness = 1 };
            public readonly List<Path> Dots = [];
            /// <summary>Opacity of each history dot by its age, before the glow of the beam (analog scope).</summary>
            public readonly List<double> DotBase = [];
            public int DotsVersion = -1;
            /// <summary>
            /// Analog scope: faint copies of the echo where it was a moment ago (afterglow of the phosphor), and their
            /// strength (0 = hidden).
            /// </summary>
            public readonly Path[] Ghosts = new Path[GhostStrengths.Length];
            public readonly double[] GhostStrength = new double[GhostStrengths.Length];
            /// <summary>Positions of the last seconds (world coordinates), for the afterglow.</summary>
            public readonly List<(double Time, double Along, double Value, bool Inside)> Recent = [];
            /// <summary>Previous positions, in world coordinates (see <see cref="ToWorld"/>), oldest first.</summary>
            /// <summary>Inside: the aircraft was inside the scan limits there (only those dots are drawn).</summary>
            public readonly List<(double Along, double Value, bool Inside)> History = [];
            public (double Along, double Value, bool Inside) LastWorld;
            public double LastLatitude = double.NaN;
            public double LastLongitude = double.NaN;
            public double LastAltitude = double.NaN;
            /// <summary>Time of the last history dot (see <see cref="HistoryClock"/>).</summary>
            public double LastHistoryTime = double.NegativeInfinity;
            /// <summary>Screen position of the track.</summary>
            public Point Position;
            /// <summary>Logical position of the track (see the class description).</summary>
            public Point Logical;
            /// <summary>True when the label was dragged: <see cref="Offset"/> is then its top-left corner from the track.</summary>
            public bool Moved;
            public Vector Offset;
            /// <summary>Label hidden with a right click.</summary>
            public bool LabelHidden;
            public Brush Color = Brushes.Green;
            /// <summary>Colour of the history dots (plots), inside or outside the approach limits like the track.</summary>
            public Brush PlotColor = Brushes.Green;
            public string Callsign = "";
            /// <summary>Last state seen inside the antenna beam, and when (history clock), for the coasting track.</summary>
            public Aircraft? BeamFix;
            public double BeamFixTime;
            /// <summary>The symbol is the one of a coasting track.</summary>
            public bool SymbolCoasting;
            /// <summary>Analog scope: brightness factor of an echo near the edge of the beam (1 well inside, towards 0 at the edge).</summary>
            public double EdgeFactor = 1;
            /// <summary>Analog scope: when the echo left the beam (history clock), NaN while inside.</summary>
            public double OutOfBeamSince = double.NaN;

            public IEnumerable<UIElement> Elements()
            {
                yield return Symbol;
                yield return Label;
                yield return Leader;
                foreach (Path ghost in Ghosts) if (ghost != null) yield return ghost;
                foreach (Path dot in Dots) yield return dot;
            }
        }

        protected RadarView(Canvas canvas, Runway runway, Radar radar, ViewOptions options)
        {
            Canvas = canvas;
            Runway = runway;
            Radar = radar;
            Options = options;
            // Lines going beyond the view (e.g. a tilted scan limit) must not be drawn over the other view.
            Canvas.ClipToBounds = true;
            // Right click near a track (not only exactly on its small symbol) hides or shows its label.
            Canvas.MouseRightButtonUp += Canvas_MouseRightButtonUp;
        }

        /// <summary>
        /// True when the vertical axis must also be mirrored with the runway on the right
        /// (azimuth view: mirroring both axes is a 180° rotation, which keeps left/right of the approach correct).
        /// </summary>
        protected virtual bool FlipVertically => false;

        protected abstract void CalculateScale();
        protected abstract void DrawStatic();
        /// <summary>
        /// Position of the aircraft in world coordinates of this view: distance along the centreline from the
        /// threshold (NM, positive on the approach side) and the second coordinate (height in ft, or lateral offset in NM).
        /// World coordinates (not pixels) are kept in the history, so it stays correct when zooming.
        /// </summary>
        protected abstract (double Along, double Value) ToWorld(Aircraft aircraft);
        /// <summary>Logical position (pixels) of a world position.</summary>
        protected abstract Point WorldToLogical(double along, double value);
        /// <summary>Colour of the track: green inside the approach limits, red outside.</summary>
        protected abstract bool IsWithinLimits(Aircraft aircraft);
        /// <summary>Label layout of this view.</summary>
        protected abstract LabelLayout Layout { get; }
        /// <summary>True for the elevation view (for the antenna scan effect).</summary>
        protected abstract bool IsElevation { get; }
        /// <summary>Logical position of the antenna, origin of the scan effect beam.</summary>
        protected abstract Point SweepOrigin();
        /// <summary>
        /// Logical end of the scan effect beam at the end of the display: <paramref name="position"/> 0 on the
        /// lower/left scan limit, 1 on the upper/right one (tilt included).
        /// </summary>
        protected abstract Point SweepEnd(double position);
        /// <summary>False when the track is outside the drawable area (it is then hidden).</summary>
        protected virtual bool IsDrawable(Point logical) => true;

        /// <summary>
        /// <paramref name="keepTracks"/>: another approach of the same runway (other glide path angle): positions
        /// and history (along the centreline from the threshold) are still valid.
        /// </summary>
        public void SetRunway(Runway runway, bool keepTracks = false)
        {
            if (runway != Runway && !keepTracks)
            {
                // Positions and history refer to the previous runway.
                ClearTracks();
            }
            Runway = runway;
            Invalidate();
        }

        public void SetRunwayOnRight(bool value)
        {
            if (RunwayOnRight != value)
            {
                RunwayOnRight = value;
                foreach (Track track in tracks.Values)
                {
                    track.Moved = false;
                }
                Invalidate();
            }
        }

        /// <summary>
        /// Rebuilds the static elements at the next <see cref="Render"/> (call after a change of range, size, settings...).
        /// </summary>
        public void Invalidate()
        {
            staticValid = false;
        }

        private void ClearTracks()
        {
            foreach (Track track in tracks.Values)
            {
                RemoveTrack(track);
            }
            tracks.Clear();
            dragging = null;
        }

        private void RemoveTrack(Track track)
        {
            foreach (UIElement element in track.Elements().ToList())
            {
                Canvas.Children.Remove(element);
            }
        }

        /// <summary>
        /// Updates the view. Must be called on the window's (UI) thread.
        /// </summary>
        /// <summary>
        /// Every frame (modern display): moves the existing tracks to their estimated positions (smoothed or
        /// coasting) without the history, afterglow and label texts, which follow at the next <see cref="Render"/>.
        /// </summary>
        public void MoveTracks(IReadOnlyList<Aircraft> aircrafts)
        {
            if (!staticValid || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;
            foreach (Aircraft aircraft in aircrafts)
            {
                if (tracks.TryGetValue(aircraft.Callsign, out Track? track)) UpdateTrack(track, aircraft, light: true);
            }
        }

        public void Render(IReadOnlyList<Aircraft> aircrafts)
        {
            if (Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;
            if (!staticValid)
            {
                foreach (UIElement element in staticElements)
                {
                    Canvas.Children.Remove(element);
                }
                staticElements.Clear();
                CalculateScale();
                DrawStatic();
                staticValid = true;
            }
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                if (!present.Add(aircraft.Callsign)) continue;
                if (!tracks.TryGetValue(aircraft.Callsign, out Track? track))
                {
                    track = CreateTrack();
                    track.Callsign = aircraft.Callsign;
                    tracks[aircraft.Callsign] = track;
                }
                UpdateTrack(track, aircraft);
            }
            // Remove the tracks of aircraft no longer received from Aurora.
            foreach (string callsign in tracks.Keys.Where(c => !present.Contains(c)).ToList())
            {
                if (dragging == tracks[callsign]) dragging = null;
                RemoveTrack(tracks[callsign]);
                tracks.Remove(callsign);
            }
        }

        /// <summary>
        /// Draws the antenna scan effect at time <paramref name="t"/> (seconds), or hides it when
        /// <paramref name="enabled"/> is false. Graphic only: the tracks are not affected.
        /// </summary>
        public void RenderSweep(bool enabled, double t, ScanEffectSpeed speed)
        {
            if (!enabled || !staticValid || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0 || Options.Theme.IsHidden(StyleElement.ScanEffect))
            {
                foreach (Polyline line in sweepLines) line.Visibility = Visibility.Collapsed;
                foreach (Track track in tracks.Values) track.Symbol.Opacity = 1;
                clutter.Hide();
                return;
            }
            var beams = new (double? Position, double Opacity)[ScanEffect.Lines];
            ScanEffect.Compute(t, speed, IsElevation, beams);
            while (sweepLines.Count < beams.Length)
            {
                Polyline line = new() { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
                Panel.SetZIndex(line, SweepZIndex);
                Canvas.Children.Add(line);
                sweepLines.Add(line);
            }
            Point origin = SweepOrigin();
            // With the compressed scale the beam is drawn along its (slightly curved) image, see Unwarp.
            Point linearOrigin = Unwarp(origin);
            int segments = Compressed ? SweepSegments : 1;
            // Width of the beam line as set in Display style; the trail lines (fading) one pixel wider.
            double width = Options.Theme.Width(StyleElement.ScanEffect);
            DoubleCollection? dash = Theme.DashArray(Options.Theme.Dash(StyleElement.ScanEffect));
            for (int i = 0; i < beams.Length; i++)
            {
                Polyline line = sweepLines[i];
                line.StrokeThickness = i == 0 ? width : width + 1;
                line.StrokeDashArray = dash;
                if (beams[i].Position is not double position)
                {
                    line.Visibility = Visibility.Collapsed;
                    continue;
                }
                Point end = SweepEnd(position);
                PointCollection points = new(segments + 1) { new Point(ToScreenX(origin.X), ToScreenY(origin.Y)) };
                if (segments > 1)
                {
                    Vector along = Unwarp(end) - linearOrigin;
                    for (int k = 1; k < segments; k++)
                    {
                        Point p = Warp(linearOrigin + along * ((double)k / segments));
                        points.Add(new Point(ToScreenX(p.X), ToScreenY(p.Y)));
                    }
                }
                points.Add(new Point(ToScreenX(end.X), ToScreenY(end.Y)));
                line.Points = points;
                line.Stroke = Options.Theme.Sweep;
                line.Opacity = beams[i].Opacity;
                line.Visibility = Visibility.Visible;
            }
            UpdateEchoBrightness(t, speed);
            UpdateClutter(t, speed);
        }

        /// <summary>Segments of the sweep line with the compressed scale.</summary>
        private const int SweepSegments = 24;

        /// <summary>Logical pixels per NM across the approach (azimuth view; 0 in the profile).</summary>
        protected virtual double LateralPixelsPerNM => 0;

        /// <summary>Horizon of the view (logical y below which there is no echo); none by default.</summary>
        protected virtual double HorizonY => double.MaxValue;

        /// <summary>Analog scope: rain clutter inside the antenna beam (see <see cref="ClutterLayer"/>).</summary>
        private void UpdateClutter(double t, ScanEffectSpeed speed)
        {
            if (!Options.Analog || Options.ClutterStrength <= 0)
            {
                clutter.Hide();
                return;
            }
            if (!clutterAdded)
            {
                Panel.SetZIndex(clutter.Element, ClutterZIndex);
                Canvas.Children.Add(clutter.Element);
                clutterAdded = true;
            }
            // Beam geometry in linear-scale coordinates (the same with the linear scale, see Unwarp): there the
            // lines from the antenna are straight, as the clutter layer expects.
            Point origin = Unwarp(SweepOrigin());
            Point low = Unwarp(SweepEnd(0));
            Point high = Unwarp(SweepEnd(1));
            Color colour = Options.Theme.Brush(StyleElement.TrackInside) is SolidColorBrush brush ? brush.Color : Colors.LimeGreen;
            clutter.Update(new ClutterLayer.Frame
            {
                Width = Canvas.ActualWidth,
                Height = Canvas.ActualHeight,
                Time = t,
                SweepCycle = 4 * ScanEffect.SweepSeconds(speed),
                Speed = speed,
                Elevation = IsElevation,
                RunwayOnRight = RunwayOnRight,
                FlipVertically = FlipVertically,
                XShift = XShift,
                OriginX = origin.X,
                OriginY = origin.Y,
                EndX = low.X,
                LowY = low.Y,
                HighY = high.Y,
                HorizonY = HorizonY,
                Strength = Options.ClutterStrength,
                Filter = Options.ClutterFilter,
                DriftX = -Options.ClutterWindAlongKt / 3600 * xscale,
                DriftY = -Options.ClutterWindRightKt / 3600 * LateralPixelsPerNM,
                Colour = colour,
                AxisY = AxisY,
                Unwarp = Compressed ? ClutterColumn : null
            });
        }

        /// <summary>
        /// Compressed scale, for the clutter layer: linear-scale x of a logical x, and the factor that brings a
        /// logical distance from the axis back to the linear scale (see <see cref="Unwarp"/>).
        /// </summary>
        private (double X, double Factor) ClutterColumn(double logicalX)
        {
            double fromEnd = FromRunwayEndOfScale(logicalX / xscale);
            return (fromEnd * xscale, 1 / ShrinkAt(fromEnd));
        }

        /// <summary>
        /// Analog scope: each echo lights up when the beam passes over it, then fades until the next pass,
        /// like the phosphor of the old screens. The position is always the latest one received from Aurora.
        /// </summary>
        private void UpdateEchoBrightness(double t, ScanEffectSpeed speed)
        {
            if (!Options.Analog)
            {
                foreach (Track track in tracks.Values) track.Symbol.Opacity = 1;
                return;
            }
            // In linear-scale coordinates (unchanged with the linear scale, see Unwarp), where the lines from the
            // antenna are straight.
            Point origin = Unwarp(SweepOrigin());
            Point low = Unwarp(SweepEnd(0));
            Point high = Unwarp(SweepEnd(1));
            // Position across the scan (0..1) of a point, from its direction seen from the antenna.
            double PositionOf(Point point)
            {
                Point logical = Unwarp(point);
                double dx = logical.X - origin.X;
                if (dx > 1 && Math.Abs(high.Y - low.Y) > 1)
                {
                    double y = origin.Y + (logical.Y - origin.Y) * (low.X - origin.X) / dx;
                    return (y - low.Y) / (high.Y - low.Y);
                }
                return 0.5;
            }
            foreach (Track track in tracks.Values)
            {
                if (track.Symbol.Visibility != Visibility.Visible)
                {
                    if (!double.IsNaN(track.OutOfBeamSince)) FadeOutOfBeam(track);
                    continue;
                }
                // Brightens as the beam arrives, full at its centre, then fades; weaker near the edge of the antenna
                // beam (soft edge).
                double light = ScanEffect.BeamLight(t, speed, IsElevation, PositionOf(track.Logical), 0.6);
                double echo = (0.18 + 0.82 * light) * (0.15 + 0.85 * track.EdgeFactor) * (1 - 0.4 * Options.ClutterFilter);
                track.Symbol.Opacity = echo;
                // Afterglow: the copies behind the echo light up and fade with it.
                for (int k = 0; k < track.Ghosts.Length; k++)
                {
                    if (track.GhostStrength[k] > 0) SetOpacity(track.Ghosts[k], track.GhostStrength[k] * echo);
                }
                // History dots: a faint glow when the beam passes over them.
                for (int i = 0; i < track.Dots.Count && i < track.DotBase.Count; i++)
                {
                    Path dot = track.Dots[i];
                    if (dot.Visibility != Visibility.Visible || dot.Tag is not Point logical) continue;
                    double dotLight = ScanEffect.BeamLight(t, speed, IsElevation, PositionOf(logical), 0.6);
                    SetOpacity(dot, track.DotBase[i] * (0.6 + 0.4 * dotLight));
                }
            }
        }

        /// <summary>
        /// Analog scope, echo out of the beam: its afterglow and history dots fade out in a few seconds, then are hidden.
        /// </summary>
        private static void FadeOutOfBeam(Track track)
        {
            // About 6 s to disappear (below 3% after 3.5 time constants).
            double fade = Math.Exp(-(HistoryClock.Elapsed.TotalSeconds - track.OutOfBeamSince) / 1.7);
            bool gone = fade < 0.03;
            for (int k = 0; k < track.Ghosts.Length; k++)
            {
                if (track.GhostStrength[k] <= 0) continue;
                if (gone) track.Ghosts[k].Visibility = Visibility.Collapsed;
                else SetOpacity(track.Ghosts[k], track.GhostStrength[k] * 0.5 * fade);
            }
            for (int i = 0; i < track.Dots.Count && i < track.DotBase.Count; i++)
            {
                Path dot = track.Dots[i];
                if (dot.Visibility != Visibility.Visible) continue;
                if (gone) dot.Visibility = Visibility.Collapsed;
                else SetOpacity(dot, track.DotBase[i] * 0.6 * fade);
            }
        }

        /// <summary>Changes the opacity only when the difference can be seen, to keep the redraw light.</summary>
        private static void SetOpacity(UIElement element, double opacity)
        {
            if (Math.Abs(element.Opacity - opacity) > 0.015) element.Opacity = opacity;
        }

        /// <summary>
        /// Analog scope: places the afterglow copies of the echo where the aircraft was 0.8, 1.6 and 2.4 s ago
        /// (only inside the scan, and only when they do not overlap the echo).
        /// </summary>
        private void UpdateGhosts(Track track, double now)
        {
            for (int k = 0; k < track.Ghosts.Length; k++)
            {
                Path ghost = track.Ghosts[k];
                track.GhostStrength[k] = 0;
                int index = Options.Analog ? track.Recent.FindLastIndex(s => s.Time <= now - (k + 1) * GhostSpacing) : -1;
                if (index < 0 || !track.Recent[index].Inside)
                {
                    ghost.Visibility = Visibility.Collapsed;
                    continue;
                }
                Point logical = WorldToLogical(track.Recent[index].Along, track.Recent[index].Value);
                if (!IsDrawable(logical) || (logical - track.Logical).Length < 1.5)
                {
                    ghost.Visibility = Visibility.Collapsed;
                    continue;
                }
                ghost.Data = EchoGeometry;
                SetEchoLength(ghost, track.Recent[index].Along);
                ghost.Stroke = track.Color;
                ghost.Fill = track.Color;
                Canvas.SetLeft(ghost, ToScreenX(logical.X));
                Canvas.SetTop(ghost, ToScreenY(logical.Y));
                track.GhostStrength[k] = GhostStrengths[k];
                SetOpacity(ghost, GhostStrengths[k] * track.Symbol.Opacity);
                ghost.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Analog scope: the echo is longer the farther the aircraft (option): 1 at the touchdown, up to
        /// <see cref="ViewOptions.EchoGrowth"/> at the end of the range. The bar grows around its centre.
        /// </summary>
        private void SetEchoLength(Path echo, double along)
        {
            double factor = 1;
            if (Options.Analog && Options.EchoGrowth > 1.001)
            {
                factor = 1 + (Options.EchoGrowth - 1) * Math.Clamp(along / Math.Max(0.1, Runway.Distance), 0, 1);
            }
            if (echo.RenderTransform is ScaleTransform scale)
            {
                if (Math.Abs(scale.ScaleY - factor) > 0.01) scale.ScaleY = factor;
            }
            else if (factor != 1)
            {
                echo.RenderTransform = new ScaleTransform(1, factor);
            }
        }

        /// <summary>
        /// Phosphor echo: a thin vertical bar with rounded ends, as on the real PAR scopes, as tall as the track symbol
        /// size (Settings → Tracks and labels; 12 px by default, about the first version).
        /// </summary>
        private static Geometry CreateEchoGeometry(double size)
        {
            double height = Math.Clamp(size, 4, 60) - 2;
            double width = Math.Clamp(height * 0.22, 2.4, 8);
            RectangleGeometry geometry = new(new Rect(-width / 2, -height / 2, width, height), width / 2, width / 2);
            geometry.Freeze();
            return geometry;
        }

        /// <param name="light">Only the position of the symbol and label (see <see cref="MoveTracks"/>).</param>
        private void UpdateTrack(Track track, Aircraft aircraft, bool light = false)
        {
            (double along, double value) = ToWorld(aircraft);
            // History: a dot every HistoryInterval seconds, at a new position given by Aurora.
            bool changed = aircraft.Latitude != track.LastLatitude || aircraft.Longitude != track.LastLongitude || aircraft.Altitude != track.LastAltitude;
            double now = HistoryClock.Elapsed.TotalSeconds;
            // Seen by the antenna of this view (each view has its own beam).
            bool inside = Radar.IsInsideBeam(aircraft, Runway, IsElevation);
            if (changed)
            {
                // Recent positions for the afterglow of the analog scope (a few seconds are enough).
                track.Recent.Add((now, along, value, inside));
                while (track.Recent.Count > 0 && track.Recent[0].Time < now - (GhostStrengths.Length + 1) * GhostSpacing)
                {
                    track.Recent.RemoveAt(0);
                }
                if (!double.IsNaN(track.LastLatitude) && now - track.LastHistoryTime >= Options.HistoryInterval - 0.05)
                {
                    track.LastHistoryTime = now;
                    track.History.Add(track.LastWorld);
                    if (track.History.Count > Profile.MaxHistoryDots)
                    {
                        track.History.RemoveAt(0);
                    }
                }
                track.LastLatitude = aircraft.Latitude;
                track.LastLongitude = aircraft.Longitude;
                track.LastAltitude = aircraft.Altitude;
            }
            track.LastWorld = (along, value, inside);

            bool coasting = false;
            if (inside)
            {
                // Modern display with the track filter: symbol and label at the smoothed position (the history
                // dots above stay at the real positions, as the plots of a radar).
                if (!Options.Analog && aircraft.Filtered is Aircraft smooth && smooth.Altitude > Runway.Elevation)
                {
                    aircraft = smooth;
                    (along, value) = ToWorld(aircraft);
                }
                track.BeamFix = aircraft.Copy();
                track.BeamFixTime = now;
                track.OutOfBeamSince = double.NaN;
                // Analog: an echo near the edge of the beam is weaker (soft edge of the beam).
                track.EdgeFactor = Options.Analog ? Math.Clamp(Radar.BeamMargin(aircraft, Runway, IsElevation) / SoftEdgeDegrees, 0, 1) : 1;
            }
            else if (Options.Analog)
            {
                // Out of the beam the echo is no longer lit: it goes dark, and its afterglow and the dots stay
                // where they were and fade (see FadeOutOfBeam).
                track.Symbol.Visibility = Visibility.Collapsed;
                track.Label.Visibility = Visibility.Collapsed;
                track.Leader.Visibility = Visibility.Collapsed;
                if (double.IsNaN(track.OutOfBeamSince)) track.OutOfBeamSince = now;
                return;
            }
            else if (track.BeamFix is Aircraft fix && Options.CoastSeconds > 0 && now - track.BeamFixTime <= Options.CoastSeconds)
            {
                // Coasting track: the radar computer estimates the position from the last one seen in the beam
                // (straight on, same ground speed and vertical speed). The real position is not used.
                aircraft = Predict(fix, now - track.BeamFixTime);
                // An estimate below the ground (landing) is not shown.
                if (aircraft.Altitude <= Runway.Elevation)
                {
                    SetTrackVisible(track, false);
                    return;
                }
                (along, value) = ToWorld(aircraft);
                coasting = true;
            }
            else
            {
                SetTrackVisible(track, false);
                return;
            }

            Point logical = WorldToLogical(along, value);
            if (!IsDrawable(logical))
            {
                SetTrackVisible(track, false);
                return;
            }
            bool within = IsWithinLimits(aircraft);
            track.Color = Options.Theme.Brush(coasting ? StyleElement.TrackCoasting : within ? StyleElement.TrackInside : StyleElement.TrackOutside);
            track.PlotColor = Options.Theme.Brush(within ? StyleElement.PlotInside : StyleElement.PlotOutside);
            track.Logical = logical;
            track.Position = new Point(ToScreenX(logical.X), ToScreenY(logical.Y));
            // Track symbol.
            SymbolSetting symbol = coasting ? Options.CoastSymbol : Options.TrackSymbol;
            if (track.SymbolVersion != Options.Version || track.SymbolCoasting != coasting)
            {
                track.Symbol.Data = Options.Analog ? EchoGeometry : Symbols.Create(symbol);
                track.SymbolVersion = Options.Version;
                track.SymbolCoasting = coasting;
            }
            track.Symbol.Stroke = track.Color;
            track.Symbol.Fill = Options.Analog || Symbols.IsFilled(symbol) ? track.Color : Brushes.Transparent;
            SetEchoLength(track.Symbol, along);
            Canvas.SetLeft(track.Symbol, track.Position.X);
            Canvas.SetTop(track.Symbol, track.Position.Y);
            track.Symbol.Visibility = Visibility.Visible;
            if (light)
            {
                if (track.Label.Visibility == Visibility.Visible) PositionLabel(track);
                return;
            }
            UpdateGhosts(track, now);
            UpdateHistory(track);
            UpdateLabel(track, aircraft);
        }

        /// <summary>
        /// Estimated position after <paramref name="seconds"/>: straight on along the track, at the same ground
        /// speed and vertical speed.
        /// </summary>
        private static Aircraft Predict(Aircraft fix, double seconds)
        {
            Aircraft estimate = fix.Copy();
            double nm = fix.Speed * seconds / 3600;
            double track = fix.Track * Math.PI / 180;
            double cosLatitude = Math.Max(0.01, Math.Cos(fix.Latitude * Math.PI / 180));
            estimate.Latitude = fix.Latitude + nm * Math.Cos(track) / 60;
            estimate.Longitude = fix.Longitude + nm * Math.Sin(track) / (60 * cosLatitude);
            estimate.Altitude = fix.Altitude + (fix.VerticalSpeedFpm ?? 0) * seconds / 60;
            return estimate;
        }

        private void SetTrackVisible(Track track, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement element in track.Elements())
            {
                element.Visibility = v;
            }
        }

        /// <summary>
        /// History dots: the last N previous positions, in the current colour of the track.
        /// </summary>
        private void UpdateHistory(Track track)
        {
            int count = Options.HistoryEnabled ? Math.Min(track.History.Count, Options.HistoryDots) : 0;
            if (track.DotsVersion != Options.Version)
            {
                // Symbol of the history dots changed: rebuild them.
                foreach (Path old in track.Dots) Canvas.Children.Remove(old);
                track.Dots.Clear();
                track.DotsVersion = Options.Version;
            }
            if (historyGeometryVersion != Options.Version)
            {
                historyGeometry = Symbols.Create(Options.HistorySymbol);
                historyGeometryVersion = Options.Version;
            }
            bool filled = Symbols.IsFilled(Options.HistorySymbol);
            while (track.Dots.Count < count)
            {
                Path dot = new() { Data = historyGeometry, StrokeThickness = 1, IsHitTestVisible = false };
                Panel.SetZIndex(dot, HistoryZIndex);
                Canvas.Children.Add(dot);
                track.Dots.Add(dot);
            }
            while (track.Dots.Count > count)
            {
                Canvas.Children.Remove(track.Dots[^1]);
                track.Dots.RemoveAt(track.Dots.Count - 1);
            }
            while (track.DotBase.Count < count) track.DotBase.Add(1);
            if (track.DotBase.Count > count) track.DotBase.RemoveRange(count, track.DotBase.Count - count);
            int first = track.History.Count - count;
            for (int i = 0; i < count; i++)
            {
                (double along, double value, bool wasInside) = track.History[first + i];
                Point logical = WorldToLogical(along, value);
                Path dot = track.Dots[i];
                dot.Stroke = track.PlotColor;
                dot.Fill = filled ? track.PlotColor : null;
                // Analog scope: the older the position, the dimmer its glow (the beam adds a little, see UpdateEchoBrightness).
                double opacity = Options.Analog ? 0.08 + 0.42 * (i + 1) / count : 1;
                track.DotBase[i] = opacity;
                // Analog: the glow of the beam is set at every frame; here only a dot that moved to a dimmer place.
                if (!Options.Analog) dot.Opacity = 1;
                else if (dot.Opacity > opacity) dot.Opacity = opacity * 0.6;
                dot.Tag = logical;
                Canvas.SetLeft(dot, ToScreenX(logical.X));
                Canvas.SetTop(dot, ToScreenY(logical.Y));
                // A radar shows only what its antenna saw: no dots where the aircraft was outside the scan limits.
                dot.Visibility = wasInside && IsDrawable(logical) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateLabel(Track track, Aircraft aircraft)
        {
            LabelLayout layout = Layout;
            if (!Options.ShowLabels || Options.Analog || track.LabelHidden || layout.IsEmpty)
            {
                track.Label.Visibility = Visibility.Collapsed;
                track.Leader.Visibility = Visibility.Collapsed;
                return;
            }
            if (track.LayoutVersion != Options.Version)
            {
                BuildLabel(track, layout, Options.Theme);
                track.LayoutVersion = Options.Version;
            }
            for (int i = 0; i < track.Cells.Length; i++)
            {
                LabelField field = layout.Cells[i];
                if (field != LabelField.None)
                {
                    track.Cells[i].Text = LabelFormatter.Format(field, aircraft, Runway, Options);
                    track.Cells[i].Foreground = LabelVisual.TextBrush(layout, i / layout.Columns, Options.Theme);
                }
            }
            track.Label.Visibility = Visibility.Visible;
            PositionLabel(track);
        }

        /// <summary>
        /// Builds the grid of text cells of a label from the layout.
        /// </summary>
        private static void BuildLabel(Track track, LabelLayout layout, Theme theme)
        {
            track.Label.Child = LabelVisual.Build(layout, theme, out track.Cells);
        }

        /// <summary>
        /// Places the label (default position, or where it was dragged) and its leader line.
        /// </summary>
        private void PositionLabel(Track track)
        {
            track.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = track.Label.DesiredSize;
            Point topLeft = track.Moved
                ? track.Position + track.Offset
                : new Point(track.Position.X + LabelDistance, track.Position.Y - LabelDistance - size.Height);
            Canvas.SetLeft(track.Label, topLeft.X);
            Canvas.SetTop(track.Label, topLeft.Y);
            if (track.Moved)
            {
                // Leader line from the track to the nearest point of the label.
                double x = Math.Clamp(track.Position.X, topLeft.X, topLeft.X + size.Width);
                double y = Math.Clamp(track.Position.Y, topLeft.Y, topLeft.Y + size.Height);
                track.Leader.X1 = track.Position.X;
                track.Leader.Y1 = track.Position.Y;
                track.Leader.X2 = x;
                track.Leader.Y2 = y;
                // Own colour, width and dash style (Display style → Label leader line), not the colour of the track.
                track.Leader.Stroke = Options.Theme.Brush(StyleElement.LabelLeader);
                track.Leader.StrokeThickness = Options.Theme.Width(StyleElement.LabelLeader);
                track.Leader.StrokeDashArray = Theme.DashArray(Options.Theme.Dash(StyleElement.LabelLeader));
                track.Leader.Visibility = Options.Theme.IsHidden(StyleElement.LabelLeader) ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                track.Leader.Visibility = Visibility.Collapsed;
            }
        }

        private Track CreateTrack()
        {
            Track track = new();
            Panel.SetZIndex(track.Symbol, TrackZIndex);
            Panel.SetZIndex(track.Leader, HistoryZIndex);
            Panel.SetZIndex(track.Label, LabelZIndex);
            track.Leader.IsHitTestVisible = false;
            track.Label.Cursor = Cursors.SizeAll;
            ToolTips.KeepOpen(track.Label);
            track.Label.ToolTip = "Drag to move · double click: back to its place · right click: hide (right click near the track or key L twice to show it again); with a Track ID field, right click gives a menu to hide the label or set an ID";
            Canvas.Children.Add(track.Symbol);
            for (int k = 0; k < track.Ghosts.Length; k++)
            {
                Path ghost = new() { Data = EchoGeometry, StrokeThickness = 2, IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed };
                Panel.SetZIndex(ghost, HistoryZIndex + 1);
                Canvas.Children.Add(ghost);
                track.Ghosts[k] = ghost;
            }
            Canvas.Children.Add(track.Leader);
            Canvas.Children.Add(track.Label);

            track.Label.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2)
                {
                    track.Moved = false;
                    PositionLabel(track);
                    return;
                }
                dragging = track;
                dragStart = e.GetPosition(Canvas);
                dragStartOffset = new Point(Canvas.GetLeft(track.Label), Canvas.GetTop(track.Label)) - track.Position;
                track.Label.CaptureMouse();
            };
            track.Label.MouseMove += (s, e) =>
            {
                if (dragging != track || !track.Label.IsMouseCaptured) return;
                track.Offset = dragStartOffset + (e.GetPosition(Canvas) - dragStart);
                track.Moved = true;
                PositionLabel(track);
            };
            track.Label.MouseLeftButtonUp += (s, e) =>
            {
                if (track.Label.IsMouseCaptured) track.Label.ReleaseMouseCapture();
                dragging = null;
                e.Handled = true;
            };
            track.Label.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                // With a Track ID field, a menu: hide the label or give an ID of one's own.
                if (Layout.Cells.Contains(LabelField.TrackId))
                {
                    ContextMenu menu = new() { PlacementTarget = track.Label };
                    AddMenuItem(menu, "Hide label", () => HideLabel(track));
                    AddMenuItem(menu, "Set ID...", () => EditTrackId(track));
                    if (Options.Identities.Assigned(track.Callsign) != null)
                    {
                        AddMenuItem(menu, "Clear ID (back to the random one)", () => Options.Identities.Assign(track.Callsign, null));
                    }
                    menu.IsOpen = true;
                    return;
                }
                HideLabel(track);
            };
            return track;
        }

        private static void HideLabel(Track track)
        {
            track.LabelHidden = true;
            track.Label.Visibility = Visibility.Collapsed;
            track.Leader.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Small entry under the label to give the track an ID of one's own (Enter confirms, Esc cancels, empty
        /// goes back to the random one). Kept for the session.
        /// </summary>
        private void EditTrackId(Track track)
        {
            TextBox box = new()
            {
                Width = 80,
                MaxLength = TrackIdentities.MaxLength,
                CharacterCasing = CharacterCasing.Upper,
                Text = Options.Identities.Assigned(track.Callsign) ?? ""
            };
            StackPanel content = new();
            content.Children.Add(new TextBlock { Text = $"ID of {track.Callsign}", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 3) });
            content.Children.Add(box);
            Popup popup = new()
            {
                PlacementTarget = track.Label,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = false,
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Child = content
                }
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    Options.Identities.Assign(track.Callsign, box.Text);
                    popup.IsOpen = false;
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            };
            popup.Opened += (s, e) =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            };
            popup.IsOpen = true;
        }

        /// <summary>Maximum distance in pixels between a right click and a track for the click to apply to it.</summary>
        private const double TrackClickDistance = 20;

        /// <summary>
        /// Right click on the view: with a single track nearby its label is hidden or shown at once; with several
        /// tracks nearby (close formation) or with labels hidden elsewhere, a menu lists them by callsign.
        /// </summary>
        private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Options.Analog) return;
            Point click = e.GetPosition(Canvas);
            List<Track> nearby = tracks.Values
                .Where(t => t.Symbol.Visibility == Visibility.Visible && (t.Position - click).Length <= TrackClickDistance)
                .OrderBy(t => (t.Position - click).Length)
                .ToList();
            List<Track> hiddenElsewhere = tracks.Values
                .Where(t => t.LabelHidden && !nearby.Contains(t))
                .OrderBy(t => t.Callsign)
                .ToList();
            if (nearby.Count == 0 && hiddenElsewhere.Count == 0) return;
            e.Handled = true;
            if (nearby.Count == 1 && hiddenElsewhere.Count == 0)
            {
                ToggleLabel(nearby[0]);
                return;
            }
            ContextMenu menu = new() { PlacementTarget = Canvas };
            foreach (Track track in nearby)
            {
                AddMenuItem(menu, $"{track.Callsign}: {(track.LabelHidden ? "show" : "hide")} label", () => ToggleLabel(track));
            }
            if (hiddenElsewhere.Count > 0)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                foreach (Track track in hiddenElsewhere)
                {
                    AddMenuItem(menu, $"{track.Callsign}: show label", () => ToggleLabel(track));
                }
            }
            if (tracks.Values.Count(t => t.LabelHidden) > 1)
            {
                menu.Items.Add(new Separator());
                AddMenuItem(menu, "Show all hidden labels", ShowAllLabels);
            }
            menu.IsOpen = true;
        }

        private static void AddMenuItem(ContextMenu menu, string header, Action action)
        {
            MenuItem item = new() { Header = header };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        private static void ToggleLabel(Track track)
        {
            track.LabelHidden = !track.LabelHidden;
            if (track.LabelHidden)
            {
                track.Label.Visibility = Visibility.Collapsed;
                track.Leader.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Shows again the labels hidden one by one with a right click.
        /// </summary>
        public void ShowAllLabels()
        {
            foreach (Track track in tracks.Values)
            {
                track.LabelHidden = false;
            }
        }

        /// <summary>
        /// Shift of the logical x on the screen: the antenna is kept at <see cref="LeftMargin"/> from the edge
        /// whatever the range (the part of the runway behind the antenna, outside the scan, is then off the view).
        /// </summary>
        protected double XShift;

        /// <summary>
        /// Distance of the antenna from the edge of the view (room for the altitude scale when shown; the compressed
        /// scale has no scale on the side, its values are on the altitude lines).
        /// </summary>
        protected double LeftMargin => Options.ShowAltitudeScale && !Compressed ? 70 : 24;

        /// <summary>
        /// Horizontal scale and shift: the antenna at <see cref="LeftMargin"/>, the end of the range 50 px from the
        /// other edge.
        /// </summary>
        protected void CalculateHorizontalScale()
        {
            double antenna = Radar.AntennaFromRunwayEnd(Runway);
            double span = Compressed
                ? Math.Max(0.1, ScaleNM(Runway.Distance + Runway.LengthNM) - ScaleNM(antenna))
                : Math.Max(0.1, Runway.Distance + Runway.LengthNM - antenna);
            xscale = Math.Max(1e-6, Canvas.ActualWidth - 50 - LeftMargin) / span;
            XShift = LeftMargin - ScaleNM(antenna) * xscale;
        }

        // Compressed distance scale (ViewOptions.DistanceKnee > 0).
        //
        // A distance d from the touchdown point (NM, negative on the runway behind it) is drawn at
        // u(d) = K·asinh(d / K): almost linear within the knee K, then logarithmic, so the last miles get more room
        // and the far ones less. A height or lateral offset v at that distance is drawn as v·u(d)/d (the "shrink"
        // factor, 1 at the touchdown): so a point at a given angle from the touchdown stays on the same straight
        // line whatever its distance. The glide path (any angle), the approach limits and the centreline are then
        // straight, as with the linear scale; only the distances along them are redistributed. Lines from the
        // antenna (scan limits, beam, sweep), behind the touchdown, become slightly curved and are drawn as
        // polylines. With the linear scale every function below returns its input unchanged (u(d) = d, shrink 1),
        // so the picture is exactly the same as without this option.

        /// <summary>True when the compressed distance scale is on.</summary>
        protected bool Compressed => Options.DistanceKnee > 0;

        /// <summary>Distance of the touchdown point from the far end of the runway, NM.</summary>
        private double TouchdownFromRunwayEnd => Runway.LengthNM - Runway.TouchdownNM;

        /// <summary>Compressed distance u(d) of a distance d from the touchdown (NM); d itself with the linear scale.</summary>
        protected double CompressNM(double fromTouchdownNM)
        {
            double knee = Options.DistanceKnee;
            return knee > 0 ? knee * Math.Asinh(fromTouchdownNM / knee) : fromTouchdownNM;
        }

        /// <summary>Inverse of <see cref="CompressNM"/>.</summary>
        protected double ExpandNM(double compressed)
        {
            double knee = Options.DistanceKnee;
            return knee > 0 ? knee * Math.Sinh(compressed / knee) : compressed;
        }

        /// <summary>
        /// Scale distance of a point at the given distance from the far end of the runway (NM): its logical x is
        /// this × xscale. The far end of the runway stays at 0; with the linear scale it is the distance itself.
        /// </summary>
        protected double ScaleNM(double fromRunwayEndNM)
        {
            if (!Compressed) return fromRunwayEndNM;
            double touchdown = TouchdownFromRunwayEnd;
            return CompressNM(fromRunwayEndNM - touchdown) - CompressNM(-touchdown);
        }

        /// <summary>Inverse of <see cref="ScaleNM"/>: distance from the far end of the runway (NM) of a scale distance.</summary>
        protected double FromRunwayEndOfScale(double scaleNM)
        {
            if (!Compressed) return scaleNM;
            double touchdown = TouchdownFromRunwayEnd;
            return touchdown + ExpandNM(scaleNM + CompressNM(-touchdown));
        }

        /// <summary>Logical x of a point at the given distance from the far end of the runway (NM).</summary>
        protected double XAt(double fromRunwayEndNM) => ScaleNM(fromRunwayEndNM) * xscale;

        /// <summary>
        /// Factor applied to heights and lateral offsets at the given distance from the far end of the runway:
        /// u(d)/d (1 at the touchdown and with the linear scale).
        /// </summary>
        protected double ShrinkAt(double fromRunwayEndNM)
        {
            if (!Compressed) return 1;
            double d = fromRunwayEndNM - TouchdownFromRunwayEnd;
            // asinh(x)/x → 1 - x²/6 near 0: the series avoids 0/0.
            double x = d / Options.DistanceKnee;
            return Math.Abs(x) < 1e-4 ? 1 - x * x / 6 : CompressNM(d) / d;
        }

        /// <summary>
        /// Logical y of the axis of the view (horizon of the profile, centreline of the azimuth view): the point
        /// from which heights or lateral offsets are measured, and towards which they shrink with the compressed scale.
        /// </summary>
        protected abstract double AxisY { get; }

        /// <summary>
        /// With the compressed scale: the point in the coordinates the same picture would have with the linear scale
        /// (same xscale and yscale), where the lines from the antenna are straight again. Unchanged with the linear scale.
        /// </summary>
        protected Point Unwarp(Point logical)
        {
            if (!Compressed) return logical;
            double fromEnd = FromRunwayEndOfScale(logical.X / xscale);
            return new Point(fromEnd * xscale, AxisY + (logical.Y - AxisY) / ShrinkAt(fromEnd));
        }

        /// <summary>Inverse of <see cref="Unwarp"/>.</summary>
        protected Point Warp(Point linear)
        {
            if (!Compressed) return linear;
            double fromEnd = linear.X / xscale;
            return new Point(XAt(fromEnd), AxisY + (linear.Y - AxisY) * ShrinkAt(fromEnd));
        }

        /// <summary>Points of a line drawn with the compressed scale: segments enough to look smooth.</summary>
        private const int CurveSegments = 48;

        /// <summary>
        /// Adds a static curve (logical coordinates of the parameter 0..1) with the style of an element, as one
        /// polyline so a dash pattern runs along it.
        /// </summary>
        protected void AddCurve(Func<double, Point> at, StyleElement element, bool dashed = false)
        {
            if (Options.Theme.IsHidden(element)) return;
            PointCollection points = new(CurveSegments + 1);
            for (int i = 0; i <= CurveSegments; i++)
            {
                Point p = at((double)i / CurveSegments);
                points.Add(new Point(ToScreenX(p.X), ToScreenY(p.Y)));
            }
            Polyline line = new()
            {
                Points = points,
                Stroke = Options.Theme.Brush(element),
                StrokeThickness = Options.Theme.Width(element),
                StrokeDashArray = Theme.DashArray(dashed ? LineDash.Dashed : Options.Theme.Dash(element)),
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            AddStatic(line);
        }

        protected double ToScreenX(double x)
        {
            x += XShift;
            return RunwayOnRight ? Canvas.ActualWidth - x : x;
        }

        protected double ToScreenY(double y)
        {
            return RunwayOnRight && FlipVertically ? Canvas.ActualHeight - y : y;
        }

        /// <summary>Analog scope: width of the soft edge of the beam, in degrees (the echo gets weaker there).</summary>
        private const double SoftEdgeDegrees = 0.5;

        /// <summary>Pixels added to the width of a range mark where it is inside the antenna beam.</summary>
        protected const double InBeamExtraWidth = 2;

        /// <summary>
        /// Adds a static line (logical coordinates, see class description) with the colour, width and dash style of
        /// an element; <paramref name="dashed"/> forces a dashed line (e.g. glide path between touchdown and threshold).
        /// <paramref name="extraWidth"/>: pixels added to the width of the element (range marks inside the beam).</summary>
        protected void AddLine(double x1, double y1, double x2, double y2, StyleElement element, bool dashed = false, double extraWidth = 0)
        {
            // Hidden in this mode (Display style → Colours & lines, Show).
            if (Options.Theme.IsHidden(element)) return;
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = Options.Theme.Brush(element),
                StrokeThickness = Options.Theme.Width(element) + extraWidth,
                StrokeDashArray = Theme.DashArray(dashed ? LineDash.Dashed : Options.Theme.Dash(element)),
                IsHitTestVisible = false
            };
            AddStatic(line);
        }

        protected Brush Brush(StyleElement element) => Options.Theme.Brush(element);

        /// <summary>Reminders of the runway within the displayed range.</summary>
        protected List<DistanceReminder> VisibleReminders()
        {
            return Options.Reminders(Runway).Where(r => r.Distance > 0 && r.Distance <= Runway.Distance + 1e-6).ToList();
        }

        /// <summary>True if a reminder line replaces the range mark at this distance.</summary>
        protected static bool HasReminderLine(List<DistanceReminder> reminders, double distance)
        {
            return reminders.Any(r => r.HasLine && Math.Abs(r.Distance - distance) < 0.005);
        }

        /// <summary>Colour of a reminder: its own, or the phosphor on the analog scope.</summary>
        private Brush ReminderBrush(DistanceReminder reminder)
        {
            if (Options.Theme.IsAnalog) return Options.Theme.Brush(StyleElement.TrackInside);
            SolidColorBrush brush = new(ColorText.Parse(reminder.Color, System.Windows.Media.Colors.Orange));
            brush.Freeze();
            return brush;
        }

        /// <summary>Line of a reminder (logical coordinates), with its note as tooltip.</summary>
        protected void AddReminderLine(DistanceReminder reminder, double x1, double y1, double x2, double y2)
        {
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = ReminderBrush(reminder),
                StrokeThickness = reminder.Width,
                StrokeDashArray = Theme.DashArray(reminder.Dash),
                IsHitTestVisible = false
            };
            AddStatic(line);
            // Invisible wider line on top: the tooltip shows anywhere near the line, not only on its few pixels.
            Line hitArea = new()
            {
                X1 = line.X1,
                Y1 = line.Y1,
                X2 = line.X2,
                Y2 = line.Y2,
                Stroke = Brushes.Transparent,
                StrokeThickness = Math.Max(12, reminder.Width + 8),
                ToolTip = reminder.ToolTip()
            };
            ToolTips.KeepOpen(hitArea);
            AddStatic(hitArea);
        }

        /// <summary>Marker of a reminder centred on a logical point, with its note as tooltip.</summary>
        protected void AddReminderMarker(DistanceReminder reminder, double x, double y)
        {
            Brush brush = ReminderBrush(reminder);
            Path path = new()
            {
                Data = Symbols.Create(reminder.Symbol, reminder.Size),
                Stroke = brush,
                StrokeThickness = 2,
                Fill = Symbols.IsFilled(reminder.Symbol) ? brush : null,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(path, ToScreenX(x));
            Canvas.SetTop(path, ToScreenY(y));
            AddStatic(path);
            // Invisible square around the symbol: the tooltip shows on the whole symbol and a little around it.
            double side = Math.Max(20, reminder.Size + 10);
            Border hitArea = new()
            {
                Width = side,
                Height = side,
                Background = Brushes.Transparent,
                ToolTip = reminder.ToolTip()
            };
            ToolTips.KeepOpen(hitArea);
            Canvas.SetLeft(hitArea, ToScreenX(x) - side / 2);
            Canvas.SetTop(hitArea, ToScreenY(y) - side / 2);
            AddStatic(hitArea);
        }

        protected void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
        {
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = stroke,
                StrokeThickness = thickness
            };
            if (dashed)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            AddStatic(line);
        }

        /// <summary>
        /// Adds a static text anchored at a logical point; <paramref name="dx"/> is a screen offset of its left side.
        /// With <paramref name="aboveAnchor"/> the text's bottom is at the anchor, otherwise its top.
        /// </summary>
        protected void AddText(string text, double x, double y, double dx, Brush foreground, bool aboveAnchor)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = Options.RangeMarks.TextSize,
                Foreground = foreground
            };
            double top = ToScreenY(y);
            if (aboveAnchor)
            {
                top -= TextHeight(textBlock);
            }
            Canvas.SetLeft(textBlock, ToScreenX(x) + dx);
            Canvas.SetTop(textBlock, top);
            AddStatic(textBlock);
        }

        /// <summary>
        /// Adds a static text near the side of the view where the runway is, vertically centred on logical y.
        /// </summary>
        protected void AddSideText(string text, double y, Brush foreground)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = 11,
                Foreground = foreground
            };
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = RunwayOnRight ? Canvas.ActualWidth - 12 - textBlock.DesiredSize.Width : 12;
            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, ToScreenY(y) - textBlock.DesiredSize.Height / 2);
            AddStatic(textBlock);
        }

        /// <summary>
        /// Adds a static symbol of the library centred on a logical point.
        /// </summary>
        protected void AddSymbol(SymbolSetting symbol, double x, double y, Brush brush)
        {
            if (symbol.Shape == SymbolShape.None) return;
            Path path = new()
            {
                Data = Symbols.Create(symbol),
                Stroke = brush,
                StrokeThickness = 2,
                Fill = Symbols.IsFilled(symbol) ? brush : null,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(path, ToScreenX(x));
            Canvas.SetTop(path, ToScreenY(y));
            AddStatic(path);
        }

        private void AddStatic(UIElement element)
        {
            Panel.SetZIndex(element, StaticZIndex);
            Canvas.Children.Add(element);
            staticElements.Add(element);
        }

        protected static double TextHeight(TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Height;
        }
    }
}
