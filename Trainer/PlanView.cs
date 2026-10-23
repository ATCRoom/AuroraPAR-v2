using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>
    /// A map shown on the RDR, with the colours chosen for it: the lines and symbols are drawn when <paramref name="ShowShapes"/>,
    /// the texts (and the names of the points) when <paramref name="ShowTexts"/>.
    /// </summary>
    internal sealed record MapLayer(MapFile Map, Brush LineBrush, Brush LabelBrush, bool ShowShapes = true, bool ShowTexts = true, MapSymbol? Symbol = null, CustomSymbol? Custom = null);

    /// <summary>What the view is centred on: where the mouse put it (MOUSE), the airport (AIRPORT), the selected aircraft (TRAFFIC).</summary>
    internal enum CentreMode
    {
        Mouse,
        Airport,
        Traffic
    }

    /// <summary>A point of a route drawn on the plan view: where it is, its name (or number) and its altitude and speed.</summary>
    internal sealed record PlanRoutePoint(double Latitude, double Longitude, string Name, string Values);

    /// <summary>
    /// A route drawn on the Instructor+ plan view: from the aircraft through its points. The selected aircraft's route is
    /// bright with the names and values of its points, a route not flown yet (still being made) dashed, the others dim.
    /// </summary>
    internal sealed record PlanRoute(double FromLatitude, double FromLongitude, IReadOnlyList<PlanRoutePoint> Points, bool Selected, bool Draft);

    /// <summary>
    /// Surveillance radar of the Trainer (RDR): plan view, north up, centred on the threshold of the runway in use.
    /// Range rings, the runways of the airport, the extended centreline (a tick every NM, a longer one every 5 NM) and
    /// the tracks with their history dots and labels (callsign; altitude in hundreds of feet and ground speed).
    /// It shows the same traffic as the PAR: Aurora's, the test aircraft and the Instructor's.
    /// </summary>
    internal sealed partial class PlanView : FrameworkElement
    {
        /// <summary>Length of the extended centreline, NM.</summary>
        public const double CentrelineLength = 15;

        private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Colors.Black));
        private static readonly Pen RingPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30))), 1));
        private static readonly Brush RingTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x30)));
        private static readonly Pen RunwayPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))), 3));
        private static readonly Pen CentrelinePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF))), 1));
        private static readonly Color TrackColor = Color.FromRgb(0x35, 0xD3, 0x6A);
        private static readonly Pen TrackPen = Freeze(new Pen(Freeze(new SolidColorBrush(TrackColor)), 1.6));
        private static readonly Pen SelectPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00))), 1.6));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        private static readonly Pen LeaderPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))), 0.8));
        private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Colors.White));
        private static readonly Brush SignBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)));
        private static readonly Brush RouteBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xC8, 0xFF)));
        private static readonly Pen RoutePen = Freeze(new Pen(RouteBrush, 1.4));
        private static readonly Pen RouteDraftPen = Freeze(new Pen(RouteBrush, 1.4) { DashStyle = DashStyles.Dash });
        private static readonly Pen RouteOtherPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x6A, 0x88))), 1));
        private static readonly Typeface MonoFace = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface MapFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        /// <summary>Runways of the other airports of runways.par, and their ICAO.</summary>
        private static readonly Pen OtherRunwayPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))), 2));
        private static readonly Brush OtherAirportBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)));
        /// <summary>History dots: older ones dimmer (10 steps of brightness).</summary>
        private static readonly Brush[] HistoryBrushes = Enumerable.Range(1, 10)
            .Select(i => Freeze(new SolidColorBrush(Color.FromArgb((byte)(255 * (0.15 + 0.85 * i / 10.0)), TrackColor.R, TrackColor.G, TrackColor.B))))
            .ToArray();

        /// <summary>The previous positions of one aircraft (latitude, longitude), oldest first.</summary>
        private sealed class History
        {
            public readonly List<(double Latitude, double Longitude)> Points = [];
            public DateTime LastAdded = DateTime.MinValue;
            public DateTime LastSeen;
        }

        private readonly Dictionary<string, History> histories = new(StringComparer.Ordinal);

        /// <summary>Runway in use: its threshold is the centre of the view.</summary>
        public Runway? Runway { get; set; }
        /// <summary>All the lines of runways.par: the runways of the same airport are drawn.</summary>
        public IReadOnlyList<Runway> Runways { get; set; } = [];
        /// <summary>Runway and pressure, written at the top left.</summary>
        public string InfoText { get; set; } = "";
        public bool ShowLabels { get; set; } = true;
        /// <summary>"TEST TRAFFIC" / "SIM TRAFFIC" (same rule as the sign of the main window), or empty.</summary>
        public string SignText { get; set; } = "";
        /// <summary>Range, NM from the centre to the nearer edge of the view.</summary>
        public double Range { get; set; } = TrainerSettings.DefaultRadarRange;
        /// <summary>The traffic of the last refresh.</summary>
        public IReadOnlyList<Aircraft> Traffic { get; private set; } = [];
        /// <summary>The maps shown, under everything else.</summary>
        public IReadOnlyList<MapLayer> Layers { get; set; } = [];
        /// <summary>ICAO codes of the airports the maps show with their names (their grey ICAO from runways.par is then not written twice).</summary>
        public HashSet<string> MapAirports { get; set; } = [];

        /// <summary>A map placed on the plane of the view (NM east and north of the centre), kept until the centre or the maps change.</summary>
        private sealed record PlacedLayer(MapLayer Layer, Geometry Lines, List<(Vector At, MapLabel Label)> Labels, List<(Vector At, MapPoint Point)> Points);
        private List<PlacedLayer> placed = [];
        private (double Latitude, double Longitude, IReadOnlyList<MapLayer>? Layers) placedFor;

        /// <summary>Mouse wheel: one step of the range list (+1 larger, −1 smaller); the owner changes <see cref="Range"/>.</summary>
        public event Action<int>? RangeStepRequested;

        /// <summary>The aircraft shown with a square round it (the one the Instructor+ has selected), by callsign.</summary>
        public string? SelectedCallsign { get; set; }
        /// <summary>A track was clicked (the callsign of the nearest one, within a few pixels).</summary>
        public event Action<string>? AircraftClicked;
        /// <summary>Where the tracks were drawn the last time (to find the one that is clicked).</summary>
        private readonly List<(Point At, string Callsign)> drawn = [];

        /// <summary>The routes of the Instructor+ (only on its plan view).</summary>
        public IReadOnlyList<PlanRoute> Routes { get; set; } = [];

        /// <summary>A click gives a point of a route (<see cref="PointPicked"/>) instead of selecting a track.</summary>
        public bool PickingPoints
        {
            get => pickingPoints;
            set
            {
                pickingPoints = value;
                Cursor = value ? Cursors.Cross : null;
            }
        }
        private bool pickingPoints;

        /// <summary>A point was clicked while <see cref="PickingPoints"/> (latitude, longitude).</summary>
        public event Action<double, double>? PointPicked;

        /// <summary>Centre and scale (pixels per NM) of the last drawing, to turn a click into a position.</summary>
        private Point lastCentre;
        private double lastScale;

        /// <summary>
        /// Where the view looks (NM east and north of the threshold of the runway in use): set by AIRPORT and TRAFFIC at
        /// each drawing; in MOUSE the wheel zooms around the pointer, the right button drags the view, a double-click
        /// centres it on the airport again. On the airport when the runway changes.
        /// </summary>
        public Vector PanOffset { get; set; }

        /// <summary>MOUSE / AIRPORT / TRAFFIC (see <see cref="CentreMode"/>); changing it raises <see cref="CentreChanged"/>.</summary>
        public CentreMode Centre
        {
            get => centreMode;
            set
            {
                if (centreMode == value) return;
                centreMode = value;
                CentreChanged?.Invoke(value);
                InvalidateVisual();
            }
        }
        private CentreMode centreMode = CentreMode.Airport;

        /// <summary>The centre mode was changed (by its buttons, or a drag that goes to MOUSE): the owner saves it.</summary>
        public event Action<CentreMode>? CentreChanged;

        /// <summary>NM east and north of the threshold of a latitude and longitude (the plane of the view).</summary>
        private static Vector Local(Runway runway, double latitude, double longitude)
        {
            double radius = Earth.Radius(runway);
            double distance = Earth.DistanceNM(runway.Latitude, runway.Longitude, latitude, longitude, radius);
            double bearing = Earth.Bearing(runway.Latitude, runway.Longitude, latitude, longitude) * Math.PI / 180;
            return new Vector(distance * Math.Sin(bearing), distance * Math.Cos(bearing));
        }

        /// <summary>Where the view looks to be centred on the airport (its point, else the threshold).</summary>
        private Vector AirportOffset(Runway runway) => AirportCentre(runway) is { } c ? Local(runway, c.Latitude, c.Longitude) : new Vector();
        private (double Latitude, double Longitude) panFor;
        private Point? dragFrom;
        private Vector dragOffset;

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (LabelRightClick(e.GetPosition(this)))
            {
                e.Handled = true;
                return;
            }
            dragFrom = e.GetPosition(this);
            dragOffset = PanOffset;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (LabelMove(e.GetPosition(this))) return;
            if (dragFrom is not Point from || lastScale <= 0) return;
            Vector moved = e.GetPosition(this) - from;
            if (Centre != CentreMode.Mouse)
            {
                // Dragging (a few pixels, not a plain right click) is the MOUSE mode.
                if (moved.Length < 4) return;
                Centre = CentreMode.Mouse;
            }
            PanOffset = new Vector(dragOffset.X - moved.X / lastScale, dragOffset.Y + moved.Y / lastScale);
            InvalidateVisual();
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (dragFrom == null) return;
            dragFrom = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            dragFrom = null;
            labelDrag = null;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Point at = e.GetPosition(this);
            if (!PickingPoints && LabelPress(at, e.ClickCount))
            {
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 2 && !PickingPoints && !drawn.Any(d => (d.At - at).Length <= 16))
            {
                // Double-click: centred on the airport again.
                PanOffset = Runway is Runway current ? AirportOffset(current) : new Vector();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (PickingPoints)
            {
                Runway? runway = Runway;
                if (runway != null && lastScale > 0)
                {
                    double east = (at.X - lastCentre.X) / lastScale;
                    double north = (lastCentre.Y - at.Y) / lastScale;
                    double distance = Math.Sqrt(east * east + north * north);
                    (double latitude, double longitude) = distance < 1e-9 ? (runway.Latitude, runway.Longitude)
                        : Earth.Destination(runway.Latitude, runway.Longitude, Math.Atan2(east, north) * 180 / Math.PI, distance, Earth.Radius(runway));
                    PointPicked?.Invoke(latitude, longitude);
                }
                else
                {
                    System.Media.SystemSounds.Beep.Play();
                }
                e.Handled = true;
                return;
            }
            // A second click of a double-click on a track: nothing (it would select and deselect it).
            if (e.ClickCount >= 2) return;
            (Point At, string Callsign)? nearest = drawn.Count == 0 ? null : drawn.OrderBy(d => (d.At - at).Length).First();
            if (nearest != null && (nearest.Value.At - at).Length <= 16)
            {
                AircraftClicked?.Invoke(nearest.Value.Callsign);
                e.Handled = true;
            }
        }

        public PlanView()
        {
            ClipToBounds = true;
            Focusable = false;
        }

        /// <summary>
        /// Takes the traffic of a refresh and keeps the history dots (one every <paramref name="interval"/> s, at most
        /// <paramref name="dots"/>), also while the view is hidden, so the tails are there when it is shown.
        /// </summary>
        public void Update(IReadOnlyList<Aircraft> traffic, DateTime now, bool historyOn, int dots, double interval)
        {
            Traffic = traffic;
            // The selected aircraft gone: no selection (TRAFFIC then stays where it was).
            if (SelectedCallsign != null && !traffic.Any(a => a.Callsign == SelectedCallsign)) SelectedCallsign = null;
            foreach (Aircraft aircraft in traffic)
            {
                if (!histories.TryGetValue(aircraft.Callsign, out History? history))
                {
                    history = new History();
                    histories[aircraft.Callsign] = history;
                }
                history.LastSeen = now;
                if (!historyOn || dots <= 0)
                {
                    history.Points.Clear();
                    continue;
                }
                if ((now - history.LastAdded).TotalSeconds >= Math.Max(0.1, interval))
                {
                    history.Points.Add((aircraft.Latitude, aircraft.Longitude));
                    history.LastAdded = now;
                }
                while (history.Points.Count > dots) history.Points.RemoveAt(0);
            }
            // Aircraft gone for a while: their dots go too.
            foreach (string callsign in histories.Where(h => (now - h.Value.LastSeen).TotalSeconds > 30).Select(h => h.Key).ToList())
            {
                histories.Remove(callsign);
                labelOffsets.Remove(callsign);
            }
        }

        /// <summary>The maps on the plane of the view around this centre (computed again only when the centre or the maps change).</summary>
        private List<PlacedLayer> PlaceMaps(Runway runway)
        {
            if (placedFor.Latitude == runway.Latitude && placedFor.Longitude == runway.Longitude && ReferenceEquals(placedFor.Layers, Layers)) return placed;
            double radius = Earth.Radius(runway);
            Vector Local(double latitude, double longitude)
            {
                double distance = Earth.DistanceNM(runway.Latitude, runway.Longitude, latitude, longitude, radius);
                double bearing = Earth.Bearing(runway.Latitude, runway.Longitude, latitude, longitude) * Math.PI / 180;
                return new Vector(distance * Math.Sin(bearing), distance * Math.Cos(bearing));
            }
            List<PlacedLayer> result = [];
            foreach (MapLayer layer in Layers)
            {
                // All the lines of a map as one geometry, in NM (north up = minus Y), drawn with a scale transform.
                StreamGeometry geometry = new();
                using (StreamGeometryContext context = geometry.Open())
                {
                    foreach (MapLine line in layer.Map.Lines)
                    {
                        if (line.Points.Count < 2) continue;
                        Vector first = Local(line.Points[0][0], line.Points[0][1]);
                        context.BeginFigure(new Point(first.X, -first.Y), false, false);
                        for (int i = 1; i < line.Points.Count; i++)
                        {
                            Vector next = Local(line.Points[i][0], line.Points[i][1]);
                            context.LineTo(new Point(next.X, -next.Y), true, false);
                        }
                    }
                }
                geometry.Freeze();
                result.Add(new PlacedLayer(layer, geometry,
                    layer.Map.Labels.Select(l => (Local(l.Latitude, l.Longitude), l)).ToList(),
                    layer.Map.Points.Select(p => (Local(p.Latitude, p.Longitude), p)).ToList()));
            }
            placed = result;
            placedFor = (runway.Latitude, runway.Longitude, Layers);
            return placed;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            // Handled here, so the wheel over the RDR does not also change the range of the PAR. The point under the
            // pointer stays under it: the view is moved by what the new range changes.
            if (LabelWheel(e.GetPosition(this), e.Delta))
            {
                e.Handled = true;
                return;
            }
            if (Centre != CentreMode.Mouse)
            {
                // AIRPORT / TRAFFIC: the range only, around the centre.
                RangeStepRequested?.Invoke(e.Delta > 0 ? 1 : -1);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            Point at = e.GetPosition(this);
            double before = Math.Min(ActualWidth, ActualHeight) / 2 / (Range > 0 ? Range : TrainerSettings.DefaultRadarRange);
            Vector under = before > 0 ? new Vector(PanOffset.X + (at.X - ActualWidth / 2) / before, PanOffset.Y - (at.Y - ActualHeight / 2) / before) : PanOffset;
            RangeStepRequested?.Invoke(e.Delta > 0 ? 1 : -1);
            double range = Range > 0 ? Range : TrainerSettings.DefaultRadarRange;
            double after = Math.Min(ActualWidth, ActualHeight) / 2 / range;
            if (before > 0 && after > 0)
            {
                PanOffset = new Vector(under.X - (at.X - ActualWidth / 2) / after, under.Y + (at.Y - ActualHeight / 2) / after);
                InvalidateVisual();
            }
            e.Handled = true;
        }

        /// <summary>
        /// The centre of the range rings: the point of the airport in the maps (same ICAO: the reference point of the AIP,
        /// the centre of its ATZ and CTR), else the middle of all the ends of its runways in runways.par; null: unknown.
        /// </summary>
        private (double Latitude, double Longitude)? AirportCentre(Runway runway)
        {
            // Found again only when the runway or the maps change.
            if (centreFor.Latitude == runway.Latitude && centreFor.Longitude == runway.Longitude && ReferenceEquals(centreFor.Layers, Layers) && ReferenceEquals(centreFor.Runways, Runways)) return airportCentre;
            centreFor = (runway.Latitude, runway.Longitude, Layers, Runways);
            airportCentre = FindAirportCentre(runway);
            return airportCentre;
        }

        private (double Latitude, double Longitude, object? Layers, object? Runways) centreFor = (double.NaN, double.NaN, null, null);
        private (double Latitude, double Longitude)? airportCentre;

        private (double Latitude, double Longitude)? FindAirportCentre(Runway runway)
        {
            string icao = runway.ICAO.Trim();
            foreach (MapLayer layer in Layers)
            {
                foreach (MapPoint point in layer.Map.Points)
                {
                    if (string.Equals(point.Name.Trim(), icao, StringComparison.OrdinalIgnoreCase)) return (point.Latitude, point.Longitude);
                }
            }
            double radius = Earth.Radius(runway);
            List<(double Latitude, double Longitude)> ends = [];
            foreach (Runway line in Runways.Where(r => string.Equals(r.ICAO.Trim(), icao, StringComparison.OrdinalIgnoreCase)))
            {
                ends.Add((line.Latitude, line.Longitude));
                if (line.LengthM > 0) ends.Add(Earth.Destination(line.Latitude, line.Longitude, line.Heading, line.LengthM / 1852, radius));
            }
            if (ends.Count == 0) return null;
            return (ends.Average(p => p.Latitude), ends.Average(p => p.Longitude));
        }

        protected override void OnRender(DrawingContext dc)
        {
            drawn.Clear();
            labelBoxes.Clear();
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            Runway? runway = Runway;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (runway == null)
            {
                dc.DrawText(Text("RDR  no runway", 13, InfoBrush, pixelsPerDip), new Point(8, 6));
                return;
            }
            double range = Range > 0 ? Range : TrainerSettings.DefaultRadarRange;
            // Another runway: centred on it again.
            if (panFor.Latitude != runway.Latitude || panFor.Longitude != runway.Longitude)
            {
                panFor = (runway.Latitude, runway.Longitude);
                PanOffset = AirportOffset(runway);
            }
            if (Centre == CentreMode.Airport) PanOffset = AirportOffset(runway);
            else if (Centre == CentreMode.Traffic && SelectedCallsign != null && Traffic.FirstOrDefault(a => a.Callsign == SelectedCallsign) is Aircraft followed)
            {
                // The selected aircraft in the middle (where its track is drawn). Gone: the view stays where it was.
                Aircraft shownAt = followed.Filtered ?? followed;
                PanOffset = Local(runway, shownAt.Latitude, shownAt.Longitude);
            }
            double scale = Math.Min(width, height) / 2 / range; // pixels per NM
            // The threshold of the runway in use on the screen (the middle of the view, moved by the pan).
            Point centre = new(width / 2 - PanOffset.X * scale, height / 2 + PanOffset.Y * scale);
            lastCentre = centre;
            lastScale = scale;
            double radius = Earth.Radius(runway);
            Point Place(double latitude, double longitude)
            {
                double distance = Earth.DistanceNM(runway.Latitude, runway.Longitude, latitude, longitude, radius);
                double bearing = Earth.Bearing(runway.Latitude, runway.Longitude, latitude, longitude) * Math.PI / 180;
                return new Point(centre.X + distance * Math.Sin(bearing) * scale, centre.Y - distance * Math.Cos(bearing) * scale);
            }
            static Vector Direction(double headingDegrees)
            {
                double h = headingDegrees * Math.PI / 180;
                return new Vector(Math.Sin(h), -Math.Cos(h));
            }
            // Range rings around the airport (its reference point), as far as the farthest corner of the view.
            Point ringCentre = AirportCentre(runway) is { } airportPoint ? Place(airportPoint.Latitude, airportPoint.Longitude) : centre;
            double reach = new[] { new Point(0, 0), new Point(width, 0), new Point(0, height), new Point(width, height) }.Max(c => (c - ringCentre).Length);

            // Range rings, with their distance.
            double step = range <= 10 ? 2 : range <= 20 ? 5 : range <= 60 ? 10 : 20;
            for (double ring = step; ring * scale <= reach; ring += step)
            {
                dc.DrawEllipse(null, RingPen, ringCentre, ring * scale, ring * scale);
                FormattedText text = Text(ring.ToString("0", CultureInfo.InvariantCulture), 10, RingTextBrush, pixelsPerDip);
                double angle = 30 * Math.PI / 180;
                dc.DrawText(text, new Point(ringCentre.X + ring * scale * Math.Sin(angle) + 3, ringCentre.Y - ring * scale * Math.Cos(angle) - text.Height));
            }

            // Maps, under everything else: lines (one geometry per map, scaled), labels and points.
            Rect view = new(-40, -40, width + 80, height + 80);
            foreach (PlacedLayer layer in PlaceMaps(runway))
            {
                if (layer.Layer.ShowShapes)
                {
                    dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, centre.X, centre.Y));
                    dc.DrawGeometry(null, new Pen(layer.Layer.LineBrush, 1 / scale), layer.Lines);
                    dc.Pop();
                }
                if (layer.Layer.ShowTexts)
                {
                    foreach ((Vector at, MapLabel label) in layer.Labels)
                    {
                        Point p = new(centre.X + at.X * scale, centre.Y - at.Y * scale);
                        if (!view.Contains(p)) continue;
                        FormattedText text = new(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MapFace, label.Size + 3, layer.Layer.LabelBrush, pixelsPerDip);
                        dc.DrawText(text, new Point(p.X - text.Width / 2, p.Y - text.Height / 2));
                    }
                }
                Pen symbolPen = new(layer.Layer.LineBrush, 1);
                foreach ((Vector at, MapPoint point) in layer.Points)
                {
                    Point p = new(centre.X + at.X * scale, centre.Y - at.Y * scale);
                    if (!view.Contains(p)) continue;
                    if (layer.Layer.ShowShapes)
                    {
                        if (layer.Layer.Custom != null) CustomSymbols.Draw(dc, layer.Layer.Custom, p, symbolPen);
                        else DrawSymbol(dc, layer.Layer.Symbol ?? point.Symbol, p, symbolPen);
                    }
                    if (layer.Layer.ShowTexts && point.Name.Length > 0)
                    {
                        dc.DrawText(new FormattedText(point.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MapFace, 11, layer.Layer.LabelBrush, pixelsPerDip),
                            new Point(p.X + 6, p.Y - 7));
                    }
                }
            }

            // The other airports of runways.par: their runways in grey, with the ICAO beside the first threshold.
            foreach (IGrouping<string, Runway> airport in Runways
                .Where(r => !string.Equals(r.ICAO, runway.ICAO, StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r.ICAO.ToUpperInvariant()))
            {
                Point? first = null;
                foreach (Runway line in airport)
                {
                    Point start = Place(line.Latitude, line.Longitude);
                    if (!view.Contains(start)) continue;
                    first ??= start;
                    dc.DrawLine(OtherRunwayPen, start, start + Direction(line.Heading) * Math.Max(2, line.LengthM / 1852 * scale));
                }
                if (first is Point at && !MapAirports.Contains(airport.Key))
                {
                    dc.DrawText(new FormattedText(airport.Key, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MapFace, 11, OtherAirportBrush, pixelsPerDip),
                        new Point(at.X + 6, at.Y + 4));
                }
            }

            // Runways of the airport: from the threshold, along the heading, as long as the runway.
            foreach (Runway line in Runways)
            {
                if (!string.Equals(line.ICAO, runway.ICAO, StringComparison.OrdinalIgnoreCase)) continue;
                Point start = Place(line.Latitude, line.Longitude);
                dc.DrawLine(RunwayPen, start, start + Direction(line.Heading) * (line.LengthM / 1852 * scale));
            }

            // Extended centreline of the runway in use: a tick every NM (only when there is room), longer every 5 NM.
            Vector outward = Direction(runway.Heading + 180);
            Vector across = new(-outward.Y, outward.X);
            dc.DrawLine(CentrelinePen, centre, centre + outward * (CentrelineLength * scale));
            for (int mile = 1; mile <= CentrelineLength; mile++)
            {
                bool longTick = mile % 5 == 0;
                if (!longTick && scale < 5) continue;
                double half = longTick ? 9 : 4;
                Point at = centre + outward * (mile * scale);
                dc.DrawLine(CentrelinePen, at - across * half, at + across * half);
            }

            // Routes of the Instructor+: the others dim, then the selected one with its names and values.
            foreach (PlanRoute route in Routes.OrderBy(r => r.Selected))
            {
                Pen pen = !route.Selected ? RouteOtherPen : route.Draft ? RouteDraftPen : RoutePen;
                Point previous = Place(route.FromLatitude, route.FromLongitude);
                foreach (PlanRoutePoint point in route.Points)
                {
                    Point p = Place(point.Latitude, point.Longitude);
                    dc.DrawLine(pen, previous, p);
                    dc.DrawEllipse(null, pen, p, 3.5, 3.5);
                    if (route.Selected && view.Contains(p))
                    {
                        dc.DrawText(Text($"{point.Name}\n{point.Values}", 10, RouteBrush, pixelsPerDip), new Point(p.X + 6, p.Y + 2));
                    }
                    previous = p;
                }
            }

            // Tracks: history dots, symbol, label.
            Rect visible = new(-60, -60, width + 120, height + 120);
            foreach (Aircraft aircraft in Traffic)
            {
                if (histories.TryGetValue(aircraft.Callsign, out History? history))
                {
                    int count = history.Points.Count;
                    for (int i = 0; i < count; i++)
                    {
                        Point dot = Place(history.Points[i].Latitude, history.Points[i].Longitude);
                        if (!visible.Contains(dot)) continue;
                        int level = Math.Clamp((int)Math.Ceiling(10.0 * (i + 1) / count) - 1, 0, 9);
                        dc.DrawEllipse(HistoryBrushes[level], null, dot, 1.6, 1.6);
                    }
                }
                Aircraft shown = aircraft.Filtered ?? aircraft;
                Point position = Place(shown.Latitude, shown.Longitude);
                if (!visible.Contains(position)) continue;
                drawn.Add((position, aircraft.Callsign));
                if (SelectedCallsign != null && aircraft.Callsign == SelectedCallsign) dc.DrawRectangle(null, SelectPen, new Rect(position.X - 9, position.Y - 9, 18, 18));
                dc.DrawEllipse(null, TrackPen, position, 5, 5);
                dc.DrawLine(TrackPen, position + new Vector(-5, 0), position + new Vector(5, 0));
                dc.DrawLine(TrackPen, position + new Vector(0, -5), position + new Vector(0, 5));
                if (!ShowLabels) continue;
                DrawLabel(dc, aircraft, shown, position, pixelsPerDip);
            }

            // Information: view, runway, pressure and range; the test / sim traffic sign when there is one.
            FormattedText info = Text($"RDR   {InfoText}   {range.ToString("0", CultureInfo.InvariantCulture)} NM", 13, InfoBrush, pixelsPerDip);
            dc.DrawText(info, new Point(8, 6));
            if (SignText.Length > 0)
            {
                dc.DrawText(Text(SignText, 13, SignBrush, pixelsPerDip), new Point(8, 10 + info.Height));
            }
        }

        /// <summary>The symbol of a point of a map (about 9 pixels): FIX triangle, VRP square, airport circle with a line, VOR hexagon, VORTAC hexagon with ticks, TACAN diamond, NDB circle with a dot.</summary>
        private static void DrawSymbol(DrawingContext dc, MapSymbol symbol, Point p, Pen pen)
        {
            switch (symbol)
            {
                case MapSymbol.Airport:
                    // Circle with a vertical line that is longer than the diameter (as on real radars).
                    dc.DrawEllipse(null, pen, p, 4, 4);
                    dc.DrawLine(pen, new Point(p.X, p.Y - 6.5), new Point(p.X, p.Y + 6.5));
                    break;
                case MapSymbol.Vrp:
                    dc.DrawRectangle(null, pen, new Rect(p.X - 3.5, p.Y - 3.5, 7, 7));
                    break;
                case MapSymbol.Vor:
                case MapSymbol.Vortac:
                    Point[] corners = Hexagon(p, 5.5);
                    ClosedShape(dc, pen, corners);
                    dc.DrawEllipse(pen.Brush, null, p, 1.2, 1.2);
                    if (symbol == MapSymbol.Vortac)
                    {
                        for (int i = 0; i < 6; i += 2)
                        {
                            Vector outward = corners[i] - p;
                            outward.Normalize();
                            dc.DrawLine(pen, corners[i], corners[i] + outward * 3.5);
                        }
                    }
                    break;
                case MapSymbol.Tacan:
                    ClosedShape(dc, pen, [new Point(p.X, p.Y - 5.5), new Point(p.X + 5.5, p.Y), new Point(p.X, p.Y + 5.5), new Point(p.X - 5.5, p.Y)]);
                    dc.DrawEllipse(pen.Brush, null, p, 1.2, 1.2);
                    break;
                case MapSymbol.Ndb:
                    dc.DrawEllipse(null, pen, p, 4, 4);
                    dc.DrawEllipse(pen.Brush, null, p, 1.2, 1.2);
                    break;
                default:
                    ClosedShape(dc, pen, [new Point(p.X, p.Y - 4.5), new Point(p.X + 4, p.Y + 3), new Point(p.X - 4, p.Y + 3)]);
                    break;
            }
        }

        private static Point[] Hexagon(Point centre, double radius)
        {
            Point[] corners = new Point[6];
            for (int i = 0; i < 6; i++)
            {
                double angle = (-90 + 60 * i) * Math.PI / 180;
                corners[i] = new Point(centre.X + radius * Math.Cos(angle), centre.Y + radius * Math.Sin(angle));
            }
            return corners;
        }

        private static void ClosedShape(DrawingContext dc, Pen pen, Point[] corners)
        {
            StreamGeometry geometry = new();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(corners[0], false, true);
                for (int i = 1; i < corners.Length; i++) context.LineTo(corners[i], true, false);
            }
            dc.DrawGeometry(null, pen, geometry);
        }

        private static FormattedText Text(string text, double size, Brush brush, double pixelsPerDip) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, size, brush, pixelsPerDip);

        private static T Freeze<T>(T item) where T : Freezable
        {
            item.Freeze();
            return item;
        }
    }

    /// <summary>
    /// The CENTRE selector of a radar view, bottom right over it: MOUSE (the wheel zooms at the pointer, the right
    /// button drags, a double-click goes back to the airport), AIRPORT (centred on the airport), TRAFFIC (the selected
    /// aircraft always in the middle), with a line saying what the mouse does.
    /// </summary>
    internal sealed class CentreBar : StackPanel
    {
        private readonly PlanView view;
        private readonly TextBlock hint = new() { Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x94)), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 0, 0, 4), IsHitTestVisible = false };
        private readonly Button[] buttons = new Button[3];
        private static readonly Brush Off = new SolidColorBrush(Color.FromArgb(0xE0, 0x2A, 0x3A, 0x4A));
        private static readonly Brush On = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        private static readonly Brush BorderOff = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xD8));

        public CentreBar(PlanView view)
        {
            this.view = view;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Margin = new Thickness(0, 0, 10, 10);
            StackPanel row = new() { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "CENTRE", Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0xAA, 0xBB)), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            (string Text, CentreMode Mode, string Tip)[] modes =
            [
                ("MOUSE", CentreMode.Mouse, "The wheel zooms at the pointer, the right button drags the view, a double-click goes back to the airport."),
                ("AIRPORT", CentreMode.Airport, "Centred on the airport (its point in the maps); the wheel changes the range."),
                ("TRAFFIC", CentreMode.Traffic, "The selected aircraft always in the middle (click a track to select it); the wheel changes the range.")
            ];
            for (int i = 0; i < modes.Length; i++)
            {
                CentreMode mode = modes[i].Mode;
                Button button = new()
                {
                    Content = modes[i].Text,
                    ToolTip = modes[i].Tip,
                    Focusable = false,
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xD8)),
                    BorderThickness = new Thickness(1.5)
                };
                button.Click += (s, e) => { view.Centre = mode; Show(); };
                buttons[i] = button;
                row.Children.Add(button);
            }
            Border border = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x14, 0x1A, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 4, 6, 4),
                Child = row,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Children.Add(hint);
            Children.Add(border);
            // The wheel over the bar: never to the range of the PAR below.
            MouseWheel += (s, e) => e.Handled = true;
            view.CentreChanged += m => Show();
            Show();
        }

        private void Show()
        {
            CentreMode[] order = [CentreMode.Mouse, CentreMode.Airport, CentreMode.Traffic];
            for (int i = 0; i < buttons.Length; i++)
            {
                bool on = view.Centre == order[i];
                buttons[i].Background = on ? On : Off;
                buttons[i].BorderBrush = on ? Brushes.White : BorderOff;
            }
            hint.Text = view.Centre switch
            {
                CentreMode.Mouse => "wheel: zoom at the pointer · right drag: move · double-click: back to the airport",
                CentreMode.Airport => "centred on the airport · wheel: range",
                _ => "centred on the selected aircraft (click a track) · wheel: range"
            };
        }
    }

    /// <summary>
    /// The RDR with its button to move it: into its own window (for a second monitor) or back into the main window.
    /// The same panel goes from one place to the other, so the history dots are kept.
    /// </summary>
    internal sealed class RadarPanel : Grid
    {
        public PlanView View { get; } = new();
        private readonly Button placeButton = NewButton();
        private readonly Button mapsButton = NewButton();

        /// <summary>The button "Own window" / "Back into the main window" was clicked: the owner moves the panel.</summary>
        public event Action? PlaceClicked;
        /// <summary>The button "Maps..." was clicked.</summary>
        public event Action? MapsClicked;

        /// <summary>The buttons over the RDR: well visible on the black screen (light border, light text).</summary>
        private static Button NewButton() => new()
        {
            Focusable = false,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x2A, 0x3A, 0x4A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xD8)),
            BorderThickness = new Thickness(1.5)
        };

        public RadarPanel()
        {
            Background = Brushes.Black;
            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0)
            };
            mapsButton.Content = "Maps...";
            mapsButton.ToolTip = "The maps of the surveillance radar: which are shown, their colours; new maps are made with ATCRoom Map Creator.";
            buttons.Children.Add(mapsButton);
            buttons.Children.Add(placeButton);
            // The wheel over the buttons too: never to the PAR range below.
            buttons.MouseWheel += (s, e) => e.Handled = true;
            Children.Add(View);
            Children.Add(buttons);
            Children.Add(new CentreBar(View));
            placeButton.Click += (s, e) => PlaceClicked?.Invoke();
            mapsButton.Click += (s, e) => MapsClicked?.Invoke();
            InWindow = false;
        }

        /// <summary>True while the panel is in its own window (the button then brings it back).</summary>
        public bool InWindow
        {
            get => inWindow;
            set
            {
                inWindow = value;
                placeButton.Content = value ? "⇲ Back into the main window" : "⧉ Own window";
                placeButton.ToolTip = value
                    ? "Put the surveillance radar back into the main window (then R switches between RDR and PAR)."
                    : "Move the surveillance radar into its own window, for example on a second monitor.";
            }
        }
        private bool inWindow;
    }
}
