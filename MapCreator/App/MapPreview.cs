using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Maps;

namespace ATCRoom.MapCreator
{
    /// <summary>
    /// The picture of everything that is going into the map, all the files together, each category in its own colour:
    /// north up, wheel to zoom, drag to move, double click to see it all. It also shows the circle of the cut: what is
    /// outside it is dimmed. The circle is drawn with the mouse (centre, then drag for the radius) when asked.
    /// </summary>
    internal sealed class MapPreview : FrameworkElement
    {
        /// <summary>NM in one degree of latitude.</summary>
        private const double NmPerDegree = 3440.065 * Math.PI / 180;
        private static readonly Typeface Face = new("Segoe UI");
        private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)));
        private static readonly Dictionary<string, Color> Colours = MapCategory.All.ToDictionary(c => c.Id, c => (Color)ColorConverter.ConvertFromString(c.DefaultColour));
        private static readonly Dictionary<string, Brush> Brushes_ = Colours.ToDictionary(c => c.Key, c => (Brush)Freeze(new SolidColorBrush(c.Value)));

        private MapFile? map;
        private readonly List<(Color Colour, StreamGeometry Lines)> shapes = [];
        /// <summary>Width of a degree of longitude compared with a degree of latitude (at the middle latitude of the map).</summary>
        private double k = 1;
        private double centreLatitude = 45, centreLongitude = 10;
        /// <summary>Pixels per degree of latitude.</summary>
        private double scale = 20;
        private bool needsFit = true;
        private bool drawingCircle;
        private Point? panStart;
        private double panLatitude, panLongitude;
        private bool circleStarted;
        private Point circleCentre;

        /// <summary>The circle of the cut, shown when <see cref="CutOn"/>.</summary>
        public bool CutOn { get; set; }
        public double CutLatitude { get; set; }
        public double CutLongitude { get; set; }
        public double CutRadiusNM { get; set; } = 60;

        /// <summary>True while the next click and drag draw the circle of the cut.</summary>
        public bool DrawingCircle
        {
            get => drawingCircle;
            set
            {
                drawingCircle = value;
                Cursor = value ? Cursors.Cross : Cursors.Arrow;
            }
        }

        /// <summary>The circle was drawn: latitude and longitude of the centre, radius in NM.</summary>
        public event Action<double, double, double>? CircleDrawn;

        public MapPreview()
        {
            ClipToBounds = true;
            Focusable = false;
        }

        private static T Freeze<T>(T item) where T : Freezable
        {
            item.Freeze();
            return item;
        }

        /// <summary>What to show (items with their categories). <paramref name="refit"/>: show it all again.</summary>
        public void SetMap(MapFile? value, bool refit)
        {
            map = value;
            shapes.Clear();
            if (value != null)
            {
                List<double[]> all = value.Lines.SelectMany(l => l.Points)
                    .Concat(value.Labels.Select(l => new[] { l.Latitude, l.Longitude }))
                    .Concat(value.Points.Select(p => new[] { p.Latitude, p.Longitude })).ToList();
                if (all.Count > 0) k = Math.Max(0.05, Math.Cos(all.Average(p => p[0]) * Math.PI / 180));
                foreach (MapCategoryInfo category in MapCategory.All)
                {
                    List<MapLine> lines = value.Lines.Where(l => l.Category == category.Id && l.Points.Count >= 2).ToList();
                    if (lines.Count == 0) continue;
                    StreamGeometry geometry = new();
                    using (StreamGeometryContext context = geometry.Open())
                    {
                        foreach (MapLine line in lines)
                        {
                            context.BeginFigure(new Point(line.Points[0][1] * k, -line.Points[0][0]), false, false);
                            for (int i = 1; i < line.Points.Count; i++) context.LineTo(new Point(line.Points[i][1] * k, -line.Points[i][0]), true, false);
                        }
                    }
                    geometry.Freeze();
                    shapes.Add((Colours[category.Id], geometry));
                }
            }
            if (refit) needsFit = true;
            InvalidateVisual();
        }

        /// <summary>Everything in the frame.</summary>
        public void Fit()
        {
            needsFit = true;
            InvalidateVisual();
        }

        private void DoFit()
        {
            needsFit = false;
            if (map == null) return;
            double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;
            bool any = false;
            void Add(double lat, double lon)
            {
                any = true;
                minLat = Math.Min(minLat, lat);
                maxLat = Math.Max(maxLat, lat);
                minLon = Math.Min(minLon, lon);
                maxLon = Math.Max(maxLon, lon);
            }
            foreach (MapLine line in map.Lines) foreach (double[] p in line.Points) Add(p[0], p[1]);
            foreach (MapLabel label in map.Labels) Add(label.Latitude, label.Longitude);
            foreach (MapPoint point in map.Points) Add(point.Latitude, point.Longitude);
            if (!any) return;
            double width = Math.Max(ActualWidth, 60), height = Math.Max(ActualHeight, 60);
            double spanX = Math.Max(1e-4, (maxLon - minLon) * k), spanY = Math.Max(1e-4, maxLat - minLat);
            scale = Math.Min((width - 40) / spanX, (height - 40) / spanY);
            centreLatitude = (minLat + maxLat) / 2;
            centreLongitude = (minLon + maxLon) / 2;
        }

        private Point ToScreen(double latitude, double longitude) =>
            new(ActualWidth / 2 + (longitude - centreLongitude) * k * scale, ActualHeight / 2 + (centreLatitude - latitude) * scale);

        private (double Latitude, double Longitude) ToGeo(Point p) =>
            (centreLatitude - (p.Y - ActualHeight / 2) / scale, centreLongitude + (p.X - ActualWidth / 2) / (scale * k));

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth, height = ActualHeight;
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            if (width < 20 || height < 20) return;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (map == null)
            {
                FormattedText hint = new("Drop the files here: everything is shown together", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 14, System.Windows.Media.Brushes.Gray, pixelsPerDip);
                dc.DrawText(hint, new Point((width - hint.Width) / 2, height / 2 - hint.Height / 2));
                return;
            }
            if (needsFit) DoFit();
            Matrix matrix = new(scale, 0, 0, scale, width / 2 - centreLongitude * k * scale, height / 2 + centreLatitude * scale);
            Rect view = new(-20, -20, width + 40, height + 40);
            if (CutOn && MapCoordinates.IsValid(CutLatitude, CutLongitude))
            {
                Point centre = ToScreen(CutLatitude, CutLongitude);
                double radius = CutRadiusNM / NmPerDegree * scale;
                dc.PushOpacity(0.28);
                DrawContent(dc, matrix, view, pixelsPerDip);
                dc.Pop();
                dc.PushClip(new EllipseGeometry(centre, radius, radius));
                DrawContent(dc, matrix, view, pixelsPerDip);
                dc.Pop();
                Pen circlePen = new(System.Windows.Media.Brushes.White, 1.5) { DashStyle = DashStyles.Dash };
                dc.DrawEllipse(null, circlePen, centre, radius, radius);
                dc.DrawEllipse(System.Windows.Media.Brushes.White, null, centre, 2.5, 2.5);
            }
            else
            {
                DrawContent(dc, matrix, view, pixelsPerDip);
            }
        }

        private void DrawContent(DrawingContext dc, Matrix matrix, Rect view, double pixelsPerDip)
        {
            if (map == null) return;
            dc.PushTransform(new MatrixTransform(matrix));
            foreach ((Color colour, StreamGeometry geometry) in shapes)
            {
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(colour), 1.0 / scale), geometry);
            }
            dc.Pop();
            // Texts and points: only those in the frame; the names only when there are not too many.
            List<(Point At, MapLabel Label)> labels = [];
            foreach (MapLabel label in map.Labels)
            {
                Point at = ToScreen(label.Latitude, label.Longitude);
                if (view.Contains(at)) labels.Add((at, label));
            }
            List<(Point At, MapPoint Point)> points = [];
            foreach (MapPoint point in map.Points)
            {
                Point at = ToScreen(point.Latitude, point.Longitude);
                if (view.Contains(at)) points.Add((at, point));
            }
            bool names = labels.Count + points.Count <= 1500;
            foreach ((Point at, MapLabel label) in labels)
            {
                if (!names) break;
                Brush brush = Brushes_.TryGetValue(label.Category, out Brush? found) ? found : System.Windows.Media.Brushes.LightGray;
                FormattedText text = new(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10, brush, pixelsPerDip);
                dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
            }
            foreach ((Point at, MapPoint point) in points)
            {
                Brush brush = Brushes_.TryGetValue(point.Category, out Brush? found) ? found : System.Windows.Media.Brushes.LightGray;
                dc.DrawEllipse(brush, null, at, 2.5, 2.5);
                if (!names || point.Name.Length == 0) continue;
                dc.DrawText(new FormattedText(point.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10, brush, pixelsPerDip), new Point(at.X + 4, at.Y - 6));
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (map == null) return;
            Point at = e.GetPosition(this);
            (double latitude, double longitude) = ToGeo(at);
            scale = Math.Clamp(scale * Math.Pow(1.2, e.Delta / 120.0), 0.5, 5_000_000);
            // The point under the mouse stays under it.
            centreLongitude = longitude - (at.X - ActualWidth / 2) / (scale * k);
            centreLatitude = latitude + (at.Y - ActualHeight / 2) / scale;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (map == null) return;
            Point at = e.GetPosition(this);
            if (e.ClickCount == 2 && !drawingCircle)
            {
                Fit();
                return;
            }
            if (drawingCircle)
            {
                circleStarted = true;
                circleCentre = at;
                (double latitude, double longitude) = ToGeo(at);
                CutLatitude = latitude;
                CutLongitude = longitude;
                CutRadiusNM = 0.1;
                CutOn = true;
            }
            else
            {
                panStart = at;
                (panLatitude, panLongitude) = (centreLatitude, centreLongitude);
            }
            CaptureMouse();
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point at = e.GetPosition(this);
            if (circleStarted)
            {
                CutRadiusNM = Math.Max(0.1, (at - circleCentre).Length / scale * NmPerDegree);
                InvalidateVisual();
            }
            else if (panStart is Point start && e.LeftButton == MouseButtonState.Pressed)
            {
                centreLongitude = panLongitude - (at.X - start.X) / (scale * k);
                centreLatitude = panLatitude + (at.Y - start.Y) / scale;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (circleStarted)
            {
                circleStarted = false;
                DrawingCircle = false;
                ReleaseMouseCapture();
                CircleDrawn?.Invoke(CutLatitude, CutLongitude, CutRadiusNM);
                return;
            }
            panStart = null;
            ReleaseMouseCapture();
        }
    }
}
