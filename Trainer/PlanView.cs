using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Surveillance radar of the Trainer (RDR): plan view, north up, centred on the threshold of the runway in use.
    /// Range rings, the runways of the airport, the extended centreline (a tick every NM, a longer one every 5 NM) and
    /// the tracks with their history dots and labels (callsign; altitude in hundreds of feet and ground speed).
    /// It shows the same traffic as the PAR: Aurora's, the test aircraft and the Instructor's.
    /// </summary>
    internal sealed class PlanView : FrameworkElement
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
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        private static readonly Pen LeaderPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))), 0.8));
        private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Colors.White));
        private static readonly Brush SignBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)));
        private static readonly Typeface MonoFace = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
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

        /// <summary>Mouse wheel: one step of the range list (+1 larger, −1 smaller); the owner changes <see cref="Range"/>.</summary>
        public event Action<int>? RangeStepRequested;

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
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            // Handled here, so the wheel over the RDR does not also change the range of the PAR.
            RangeStepRequested?.Invoke(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
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
            Point centre = new(width / 2, height / 2);
            double scale = Math.Min(width, height) / 2 / range; // pixels per NM
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
            double reach = Math.Sqrt(width * width + height * height) / 2;

            // Range rings, with their distance.
            double step = range <= 10 ? 2 : range <= 20 ? 5 : range <= 60 ? 10 : 20;
            for (double ring = step; ring * scale <= reach; ring += step)
            {
                dc.DrawEllipse(null, RingPen, centre, ring * scale, ring * scale);
                FormattedText text = Text(ring.ToString("0", CultureInfo.InvariantCulture), 10, RingTextBrush, pixelsPerDip);
                double angle = 30 * Math.PI / 180;
                dc.DrawText(text, new Point(centre.X + ring * scale * Math.Sin(angle) + 3, centre.Y - ring * scale * Math.Cos(angle) - text.Height));
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
                dc.DrawEllipse(null, TrackPen, position, 5, 5);
                dc.DrawLine(TrackPen, position + new Vector(-5, 0), position + new Vector(5, 0));
                dc.DrawLine(TrackPen, position + new Vector(0, -5), position + new Vector(0, 5));
                if (!ShowLabels) continue;
                string altitude = Math.Max(0, Math.Round(shown.Altitude / 100)).ToString("000", CultureInfo.InvariantCulture);
                string speed = Math.Max(0, Math.Round(shown.Speed)).ToString("0", CultureInfo.InvariantCulture);
                FormattedText label = Text($"{aircraft.Callsign}\n{altitude}  {speed}", 11, LabelBrush, pixelsPerDip);
                Point corner = new(position.X + 14, position.Y - 14 - label.Height);
                dc.DrawLine(LeaderPen, position + new Vector(4, -4), new Point(corner.X, corner.Y + label.Height));
                dc.DrawText(label, corner);
            }

            // Information: view, runway, pressure and range; the test / sim traffic sign when there is one.
            FormattedText info = Text($"RDR   {InfoText}   {range.ToString("0", CultureInfo.InvariantCulture)} NM", 13, InfoBrush, pixelsPerDip);
            dc.DrawText(info, new Point(8, 6));
            if (SignText.Length > 0)
            {
                dc.DrawText(Text(SignText, 13, SignBrush, pixelsPerDip), new Point(8, 10 + info.Height));
            }
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
    /// The RDR with its button to move it: into its own window (for a second monitor) or back into the main window.
    /// The same panel goes from one place to the other, so the history dots are kept.
    /// </summary>
    internal sealed class RadarPanel : Grid
    {
        public PlanView View { get; } = new();
        private readonly Button placeButton = new()
        {
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
        };

        /// <summary>The button was clicked: the owner moves the panel.</summary>
        public event Action? PlaceClicked;

        public RadarPanel()
        {
            Background = Brushes.Black;
            // The wheel over the button too: never to the PAR range below.
            placeButton.MouseWheel += (s, e) => e.Handled = true;
            Children.Add(View);
            Children.Add(placeButton);
            placeButton.Click += (s, e) => PlaceClicked?.Invoke();
            InWindow = false;
        }

        /// <summary>True while the panel is in its own window (the button then brings it back).</summary>
        public bool InWindow
        {
            get => inWindow;
            set
            {
                inWindow = value;
                placeButton.Content = value ? "Back into the main window" : "Own window";
                placeButton.ToolTip = value
                    ? "Put the surveillance radar back into the main window (then R switches between RDR and PAR)."
                    : "Move the surveillance radar into its own window, for example on a second monitor.";
            }
        }
        private bool inWindow;
    }
}
