using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Deviation crosshair of one aircraft, as on the ARTMACS display (like the needles of an ILS): a cross with
    /// the scale written at its ends (±500 ft up / down, ±1000 ft left / right, or the same in metres: ±152 / ±304 m)
    /// and a dot at the deviation from the glide path (up = above) and from the centreline (right = right of it, as
    /// seen by the pilot). A deviation beyond the scale puts a hollow dot on the edge. The callsign above.
    /// </summary>
    internal sealed class DeviationCrosshair : FrameworkElement
    {
        public const double VerticalScaleFt = 500;
        public const double LateralScaleFt = 1000;
        private const double Half = 60;
        private const double Gap = 22;

        public Brush Stroke { get; set; } = Brushes.Red;
        public double Thickness { get; set; } = 1;
        public Brush DotBrush { get; set; } = Brushes.White;
        public Brush TextBrush { get; set; } = Brushes.White;
        public double FontSize { get; set; } = 11;
        public bool Metres { get; set; }
        /// <summary>Above the glide path (ft, + up) and right of the centreline (ft, + right).</summary>
        public double VerticalFt { get; set; }
        public double LateralFt { get; set; }
        public string Title { get; set; } = "";

        public DeviationCrosshair()
        {
            IsHitTestVisible = false;
            Width = 2 * (Half + Gap) + 20;
            Height = 2 * (Half + Gap) + 14;
        }

        private string Value(double feet) =>
            (Metres ? Math.Truncate(feet * 0.3048) : feet).ToString("+0;-0;0", CultureInfo.InvariantCulture);

        private FormattedText Text(string text) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), FontSize, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected override void OnRender(DrawingContext dc)
        {
            Pen pen = new(Stroke, Thickness);
            Point c = new(Width / 2, Height / 2 + 6);
            dc.DrawLine(pen, new Point(c.X, c.Y - Half), new Point(c.X, c.Y + Half));
            dc.DrawLine(pen, new Point(c.X - Half, c.Y), new Point(c.X + Half, c.Y));
            // Ticks at half and full scale.
            foreach (double f in new[] { -1, -0.5, 0.5, 1 })
            {
                double tick = Math.Abs(f) == 1 ? 6 : 4;
                dc.DrawLine(pen, new Point(c.X - tick, c.Y - f * Half), new Point(c.X + tick, c.Y - f * Half));
                dc.DrawLine(pen, new Point(c.X + f * Half, c.Y - tick), new Point(c.X + f * Half, c.Y + tick));
            }
            void Label(string text, Point at, double alignX, double alignY)
            {
                FormattedText t = new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), FontSize - 1, Stroke,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(t, new Point(at.X - t.Width * alignX, at.Y - t.Height * alignY));
            }
            Label(Value(VerticalScaleFt), new Point(c.X + 4, c.Y - Half), 0, 1);
            Label(Value(-VerticalScaleFt), new Point(c.X + 4, c.Y + Half), 0, 0);
            Label(Value(-LateralScaleFt), new Point(c.X - Half, c.Y + 5), 0.5, 0);
            Label(Value(LateralScaleFt), new Point(c.X + Half, c.Y + 5), 0.5, 0);
            if (Title.Length > 0)
            {
                FormattedText title = Text(Title);
                dc.DrawText(title, new Point(c.X - Half - Gap + 2, c.Y - Half - Gap - title.Height / 2));
            }
            if (double.IsNaN(VerticalFt) || double.IsNaN(LateralFt)) return;
            double x = LateralFt / LateralScaleFt;
            double y = VerticalFt / VerticalScaleFt;
            bool beyond = Math.Abs(x) > 1 || Math.Abs(y) > 1;
            x = Math.Clamp(x, -1, 1);
            y = Math.Clamp(y, -1, 1);
            Point dot = new(c.X + x * Half, c.Y - y * Half);
            if (beyond) dc.DrawEllipse(null, new Pen(DotBrush, 2), dot, 4, 4);
            else dc.DrawEllipse(DotBrush, null, dot, 4, 4);
        }
    }
}
