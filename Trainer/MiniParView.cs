using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: a simple PAR for the instructor (under the plan view). Elevation above,
    /// azimuth below, linear scales, the runway and range of the student's PAR, touchdown on the left: the glide path,
    /// the extended centreline, a tick every NM, the tracks with a few history dots, the selected aircraft with a
    /// yellow square; a click on a track selects it. No LOG view, beam limits, safety lines or full labels.
    /// </summary>
    internal sealed class MiniParView : FrameworkElement
    {
        private static readonly Brush Background = Freeze(new SolidColorBrush(Colors.Black));
        private static readonly Pen GroundPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x8A, 0x1D))), 1));
        private static readonly Pen TickPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x5C, 0x14))), 1));
        private static readonly Brush TickText = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0xBF, 0x2F)));
        private static readonly Pen PathPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0x00))), 1.6));
        private static readonly Color TrackColor = Color.FromRgb(0x35, 0xD3, 0x6A);
        private static readonly Pen TrackPen = Freeze(new Pen(Freeze(new SolidColorBrush(TrackColor)), 1.6));
        private static readonly Brush DotBrush = Freeze(new SolidColorBrush(TrackColor));
        private static readonly Pen SelectPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00))), 1.6));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Colors.White));
        private static readonly Color UnseenColor = Color.FromRgb(0x80, 0x80, 0x80);
        private static readonly Pen UnseenPen = Freeze(new Pen(Freeze(new SolidColorBrush(UnseenColor)), 1.6));
        private static readonly Brush UnseenBrush = Freeze(new SolidColorBrush(UnseenColor));
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface MonoFace = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        /// <summary>Margins: touchdown this far from the left edge, the end of the range this far from the right edge (pixels).</summary>
        private const double LeftMargin = 40;
        private const double RightMargin = 30;
        private const int Dots = 6;

        /// <summary>The student's runway (touchdown, glide path, elevation) and the range of the PAR (NM).</summary>
        public Runway? Runway { get; set; }
        public double RangeNM { get; set; } = 10;
        public string? SelectedCallsign { get; set; }
        /// <summary>Whether the student's PAR sees an aircraft (inside its beams): the others are drawn grey, "not seen". Null: all as seen.</summary>
        public Func<Aircraft, bool>? IsSeen { get; set; }
        /// <summary>A track was clicked (the nearest one, within a few pixels).</summary>
        public event Action<string>? AircraftClicked;

        private IReadOnlyList<Aircraft> traffic = [];
        private readonly Dictionary<string, (List<(double Latitude, double Longitude, double Altitude)> Points, DateTime Last)> histories = new(StringComparer.Ordinal);
        private readonly List<(Point At, string Callsign)> drawn = [];

        public MiniParView()
        {
            ClipToBounds = true;
            Focusable = false;
        }

        /// <summary>The traffic of a refresh, and a history dot every second (the last few).</summary>
        public void Update(IReadOnlyList<Aircraft> aircrafts, DateTime now)
        {
            traffic = aircrafts;
            foreach (Aircraft aircraft in aircrafts)
            {
                if (!histories.TryGetValue(aircraft.Callsign, out var history)) history = (new List<(double Latitude, double Longitude, double Altitude)>(), DateTime.MinValue);
                if ((now - history.Last).TotalSeconds >= 1)
                {
                    Aircraft at = aircraft.Filtered ?? aircraft;
                    history.Points.Add((at.Latitude, at.Longitude, at.Altitude));
                    while (history.Points.Count > Dots) history.Points.RemoveAt(0);
                    history.Last = now;
                }
                histories[aircraft.Callsign] = history;
            }
            foreach (string gone in histories.Keys.Where(k => !aircrafts.Any(a => a.Callsign == k)).ToList()) histories.Remove(gone);
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Point at = e.GetPosition(this);
            (Point At, string Callsign)? nearest = drawn.Count == 0 ? null : drawn.OrderBy(d => (d.At - at).Length).First();
            if (nearest != null && (nearest.Value.At - at).Length <= 14)
            {
                AircraftClicked?.Invoke(nearest.Value.Callsign);
                e.Handled = true;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            drawn.Clear();
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;
            dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Runway? runway = Runway;
            // Too small to draw the two views (the splitter dragged down): only the background.
            if (height < 100 || width < 120) return;
            if (runway == null)
            {
                dc.DrawText(Text("PAR  no runway", 12, InfoBrush, MonoFace, pixelsPerDip), new Point(8, 4));
                return;
            }
            double range = RangeNM > 0 ? RangeNM : 10;
            double x0 = LeftMargin;
            double x1 = Math.Max(x0 + 50, width - RightMargin);
            double X(double distanceNM) => x0 + (x1 - x0) * distanceNM / range;

            // Elevation: from the ground (its line) up; the glide path at the end of the range in the middle of the height.
            double elevationTop = 22;
            double elevationBase = Math.Max(elevationTop + 40, height / 2 - 6);
            double maxHeight = Math.Max(500, runway.GlidePathHeight(range) * 2.2);
            double HeightY(double feet) => elevationBase - (elevationBase - elevationTop) * feet / maxHeight;
            // Azimuth: the centreline in the middle of the lower half; the width of the view = ±(range × 0.15) NM.
            double azimuthTop = elevationBase + 14;
            double azimuthMiddle = (azimuthTop + height - 4) / 2;
            double halfHeight = Math.Max(10, height - 4 - azimuthMiddle);
            double maxLateral = Math.Max(0.5, range * 0.15);
            double LateralY(double nm) => azimuthMiddle - halfHeight * nm / maxLateral;

            // Ticks every NM (fewer when the range is long) with the distance; ground, glide path, centreline.
            double tickStep = range <= 15 ? 1 : range <= 30 ? 2 : 5;
            for (double d = tickStep; d <= range + 1e-9; d += tickStep)
            {
                double x = X(d);
                dc.DrawLine(TickPen, new Point(x, HeightY(maxHeight * d / range)), new Point(x, elevationBase));
                dc.DrawLine(TickPen, new Point(x, LateralY(maxLateral * d / range)), new Point(x, LateralY(-maxLateral * d / range)));
                FormattedText label = Text($"{d.ToString("0", CultureInfo.InvariantCulture)}NM", 10, TickText, Face, pixelsPerDip);
                dc.DrawText(label, new Point(x - label.Width / 2, elevationBase + 1));
            }
            dc.DrawLine(GroundPen, new Point(0, elevationBase), new Point(width, elevationBase));
            dc.DrawLine(GroundPen, new Point(0, azimuthMiddle), new Point(width, azimuthMiddle));
            dc.DrawLine(PathPen, new Point(X(0), HeightY(runway.GlidePathHeight(0))), new Point(X(range), HeightY(runway.GlidePathHeight(range))));
            dc.DrawLine(PathPen, new Point(X(0), azimuthMiddle), new Point(X(range), azimuthMiddle));

            // Tracks: history dots, symbol in both views, a short label; the selected one with a square.
            foreach (Aircraft aircraft in traffic)
            {
                Aircraft shown = aircraft.Filtered ?? aircraft;
                double distance = shown.DistanceFromTouchdown(runway);
                if (distance < -0.5 || distance > range * 1.05) continue;
                double feet = shown.Altitude - runway.Elevation;
                double lateral = shown.LateralOffset(runway);
                // Not seen by the student's PAR (outside its beams): grey.
                bool seen = IsSeen?.Invoke(aircraft) ?? true;
                Pen trackPen = seen ? TrackPen : UnseenPen;
                Brush dotBrush = seen ? DotBrush : UnseenBrush;
                Brush labelBrush = seen ? LabelBrush : UnseenBrush;
                if (histories.TryGetValue(aircraft.Callsign, out var history))
                {
                    foreach ((double latitude, double longitude, double altitude) in history.Points)
                    {
                        Aircraft past = new() { Callsign = aircraft.Callsign, Latitude = latitude, Longitude = longitude, Altitude = altitude };
                        double pastDistance = past.DistanceFromTouchdown(runway);
                        if (pastDistance < -0.5 || pastDistance > range * 1.05) continue;
                        // Each dot only inside its own view.
                        double upY = HeightY(altitude - runway.Elevation);
                        double downY = LateralY(past.LateralOffset(runway));
                        if (upY >= elevationTop && upY <= elevationBase) dc.DrawEllipse(dotBrush, null, new Point(X(pastDistance), upY), 1.4, 1.4);
                        if (downY >= azimuthTop && downY <= height) dc.DrawEllipse(dotBrush, null, new Point(X(pastDistance), downY), 1.4, 1.4);
                    }
                }
                Point up = new(X(distance), Math.Clamp(HeightY(feet), elevationTop - 10, elevationBase));
                Point down = new(X(distance), Math.Clamp(LateralY(lateral), azimuthTop, Math.Max(azimuthTop, height - 2)));
                bool selected = SelectedCallsign != null && aircraft.Callsign == SelectedCallsign;
                foreach (Point p in new[] { up, down })
                {
                    drawn.Add((p, aircraft.Callsign));
                    if (selected) dc.DrawRectangle(null, SelectPen, new Rect(p.X - 9, p.Y - 9, 18, 18));
                    dc.DrawEllipse(null, trackPen, p, 5, 5);
                    dc.DrawLine(trackPen, p + new Vector(-5, 0), p + new Vector(5, 0));
                    dc.DrawLine(trackPen, p + new Vector(0, -5), p + new Vector(0, 5));
                }
                double deviation = feet - runway.GlidePathHeight(distance);
                string upper = string.Format(CultureInfo.InvariantCulture, "{0}  {1:0.0} NM{5}\nA {2:0}  {3} {4:0}", aircraft.Callsign, distance, shown.Altitude, deviation >= 0 ? "U" : "D", Math.Abs(deviation), seen ? "" : "  not seen");
                string lower = string.Format(CultureInfo.InvariantCulture, "{0}  {1} {2:0} ft", aircraft.Callsign, lateral >= 0 ? "R" : "L", Math.Abs(lateral * Runway.FeetPerNM));
                FormattedText upperText = Text(upper, 11, labelBrush, Face, pixelsPerDip);
                // The labels inside their own views (not over the title or the distances).
                dc.DrawText(upperText, new Point(up.X + 12, Math.Max(elevationTop, up.Y - 12 - upperText.Height)));
                dc.DrawText(Text(lower, 11, labelBrush, Face, pixelsPerDip), new Point(down.X + 12, Math.Max(azimuthTop + 12, down.Y - 26)));
            }

            dc.DrawText(Text(string.Format(CultureInfo.InvariantCulture, "PAR  RWY {0}  GP {1:0.0#}°  {2:0} NM  (as the student)", runway.DisplayName, runway.GlideSlope, range),
                12, InfoBrush, MonoFace, pixelsPerDip), new Point(8, 4));
        }

        private static FormattedText Text(string text, double size, Brush brush, Typeface face, double pixelsPerDip) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, pixelsPerDip);

        private static T Freeze<T>(T item) where T : Freezable
        {
            item.Freeze();
            return item;
        }
    }
}
