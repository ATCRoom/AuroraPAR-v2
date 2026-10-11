using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Azimuth view. Logical x: pixels from the far end of the runway (runway end on the left, approach on the right);
    /// logical y: pixels from the top, extended centreline in the middle, right of the approach (pilot's view) upwards.
    /// With the runway on the right both axes are mirrored (180° rotation), so the side of the centreline
    /// shown above/below stays consistent with the direction of flight.
    /// </summary>
    internal class HorizontalView(Canvas canvas, Runway runway, Radar radar, ViewOptions options) : RadarView(canvas, runway, radar, options)
    {
        protected override bool FlipVertically => true;

        private double CenterY => Canvas.ActualHeight / 2;

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        private double AntennaNM => Radar.AntennaFromRunwayEnd(Runway);

        protected override void CalculateScale()
        {
            CalculateHorizontalScale();
            // Fixed lateral scale, depending only on the range: the default scan limits (±10°) fill the view at the end
            // of the range. Other scan limits or a tilt move the lines (beyond the view if needed), as on a real PAR.
            double half = Radar.ScanOffset(Runway.Distance + Runway.LengthNM - AntennaNM, Radar.ReferenceScanHalfWidth);
            yscale = (Canvas.ActualHeight - 50) / (2 * half);
        }

        /// <summary>
        /// Logical y of a lateral offset in NM (positive right of the approach).
        /// </summary>
        private double LateralY(double offsetNM)
        {
            return CenterY - offsetNM * yscale;
        }

        /// <summary>
        /// Logical y of an azimuth scan limit (signed angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            return LateralY(Radar.ScanOffset(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
        }

        protected override void DrawStatic()
        {
            double length = Runway.LengthNM;
            double range = Runway.Distance;
            double end = length + range;
            double cy = CenterY;
            // Scan limits (physical, fixed) and antenna beam (moved by the tilt inside them).
            double left = Radar.ScanLeftEdge;
            double right = Radar.ScanRightEdge;
            double beamLeft = Radar.AzimuthLeftEdge;
            double beamRight = Radar.AzimuthRightEdge;
            // Runway and threshold (a line across the runway, or a symbol).
            AddLine(0, cy, length * xscale, cy, StyleElement.Runway);
            if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                AddLine(length * xscale, cy - Runway.WidthNM * yscale, length * xscale, cy + Runway.WidthNM * yscale, StyleElement.Runway);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Runway)) AddSymbol(Options.ThresholdSymbol, length * xscale, cy, Brush(StyleElement.Runway));
            }
            // Scan limits and beam edges, from the antenna.
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, left), StyleElement.ScanLimits);
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, right), StyleElement.ScanLimits);
            if (Options.ShowBeamEdges && Radar.BeamEnabled)
            {
                AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, beamLeft), StyleElement.AntennaBeam);
                AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, beamRight), StyleElement.AntennaBeam);
            }
            AddSymbol(Options.AntennaSymbol, AntennaNM * xscale, cy, Brush(StyleElement.Antenna));
            // Extended centreline and its approach limits, all starting at the touchdown point:
            // dashed between touchdown and threshold, solid beyond the threshold.
            double xTouchdown = (length - Runway.TouchdownNM) * xscale;
            double xThreshold = length * xscale;
            double xEnd = end * xscale;
            AddLine(xTouchdown, cy, xThreshold, cy, StyleElement.Centerline, dashed: true);
            AddLine(xThreshold, cy, xEnd, cy, StyleElement.Centerline);
            foreach (bool isRight in new[] { false, true })
            {
                double sign = isRight ? 1 : -1;
                double atThreshold = LateralY(sign * Radar.ApproachHalfWidth(Runway.TouchdownNM, isRight));
                double atEnd = LateralY(sign * Radar.ApproachHalfWidth(Runway.TouchdownNM + range, isRight));
                AddLine(xTouchdown, cy, xThreshold, atThreshold, StyleElement.ApproachLimits, dashed: true);
                AddLine(xThreshold, atThreshold, xEnd, atEnd, StyleElement.ApproachLimits);
            }
            // Touchdown point.
            if (Options.TouchdownSymbol.Shape == SymbolShape.Line)
            {
                double half = Options.TouchdownSymbol.Size * 2 / 3;
                AddLine(xTouchdown, cy - half, xTouchdown, cy + half, StyleElement.Touchdown);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Touchdown)) AddSymbol(Options.TouchdownSymbol, xTouchdown, cy, Brush(StyleElement.Touchdown));
            }
            // Distance where the glide path reaches the decision height: vertical line between the scan limits.
            double interceptNM = length - Runway.TouchdownNM + Runway.MissedApproachPointNM;
            if (interceptNM <= end)
            {
                // Both sides of the centreline: up to the scan limits, or a length in metres (never beyond the limits).
                double yLeft = ScanY(interceptNM, left), yRight = ScanY(interceptNM, right);
                double low = Math.Min(yLeft, yRight), high = Math.Max(yLeft, yRight);
                double reach = Options.DhAzimuthLength > 0 ? Options.DhAzimuthLength / 1852 * yscale : double.MaxValue;
                AddLine(interceptNM * xscale, Math.Max(low, cy - reach), interceptNM * xscale, Math.Min(high, cy + reach), StyleElement.DecisionHeightAzimuth);
                AddSymbol(Options.DhAzimuthSymbol, interceptNM * xscale, cy, Brush(StyleElement.DecisionHeightMark));
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            List<DistanceReminder> reminders = VisibleReminders();
            foreach ((double distance, StyleElement element, bool text) in Options.RangeMarks.Marks(range))
            {
                if (HasReminderLine(reminders, distance)) continue;
                double markNM = distance - Runway.TouchdownNM + length;
                // Ticks on the distance axis (the centreline): the marks with text are longer.
                double tickUp = Options.RangeMarks.TickAbove(text), tickDown = Options.RangeMarks.TickBelow(text);
                if (tickUp + tickDown > 0) AddLine(markNM * xscale, cy + tickDown, markNM * xscale, cy - tickUp, element);
                if (!Options.RangeMarks.DrawLines) continue;
                AddLine(markNM * xscale, ScanY(markNM, left), markNM * xscale, ScanY(markNM, right), element);
                // Thicker where the antenna beam looks.
                if (Radar.BeamEnabled)
                {
                    AddLine(markNM * xscale, ScanY(markNM, beamLeft), markNM * xscale, ScanY(markNM, beamRight), element, extraWidth: InBeamExtraWidth);
                }
            }
            // Distance reminders: only the line in this view (the markers are in the elevation view).
            foreach (DistanceReminder reminder in reminders.Where(r => r.HasLine))
            {
                double markNM = reminder.Distance - Runway.TouchdownNM + length;
                AddReminderLine(reminder, markNM * xscale, ScanY(markNM, left), markNM * xscale, ScanY(markNM, right));
            }
        }

        protected override (double Along, double Value) ToWorld(Aircraft aircraft)
        {
            return (aircraft.AlongTrackDistance(Runway), aircraft.LateralOffset(Runway));
        }

        protected override Point WorldToLogical(double along, double value)
        {
            return new Point((along + Runway.LengthNM) * xscale, LateralY(value));
        }

        protected override bool IsDrawable(Point logical)
        {
            return logical.Y >= 0 && logical.Y <= Canvas.ActualHeight;
        }

        protected override bool IsWithinLimits(Aircraft aircraft)
        {
            return Radar.IsWithinCenterlineLimits(aircraft, Runway);
        }

        protected override LabelLayout Layout => Options.AzimuthLabel;

        protected override bool IsElevation => false;

        protected override double LateralPixelsPerNM => yscale;

        protected override Point SweepOrigin() => new(AntennaNM * xscale, CenterY);

        protected override Point SweepEnd(double position)
        {
            double end = Runway.LengthNM + Runway.Distance;
            double angle = Radar.AzimuthLeftEdge + position * (Radar.AzimuthRightEdge - Radar.AzimuthLeftEdge);
            return new Point(end * xscale, ScanY(end, angle));
        }
    }
}
