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
            // With the compressed scale the lateral offsets are compressed too (see W): the same rule there.
            double half = W(LateralReferenceNM);
            yscale = (Canvas.ActualHeight - 50) / (2 * half);
        }

        /// <summary>
        /// Logical y of a lateral offset in NM (positive right of the approach).
        /// </summary>
        private double LateralY(double fromRunwayEndNM, double offsetNM)
        {
            return CenterY - W(offsetNM) * yscale;
        }

        // Version D: lateral offsets stay linear (only the distances are compressed); the approach limits are curves.

        private double LateralReferenceNM => Radar.ScanOffset(Runway.Distance + Runway.LengthNM - AntennaNM, Radar.ReferenceScanHalfWidth);

        private static double W(double offsetNM) => offsetNM;

        private static double WInverse(double compressed) => compressed;

        protected override Point Unwarp(Point logical)
        {
            if (!Compressed) return logical;
            double fromEnd = FromRunwayEndOfScale(logical.X / xscale);
            return new Point(fromEnd * xscale, CenterY - WInverse((CenterY - logical.Y) / yscale) * yscale);
        }

        protected override Point Warp(Point linear)
        {
            if (!Compressed) return linear;
            return new Point(XAt(linear.X / xscale), CenterY - W((CenterY - linear.Y) / yscale) * yscale);
        }

        protected override (double X, double Factor) ClutterColumn(double logicalX)
        {
            return (FromRunwayEndOfScale(logicalX / xscale) * xscale, 1);
        }

        protected override Func<double, double>? ClutterRow => y => CenterY - WInverse((CenterY - y) / yscale) * yscale;

        /// <summary>
        /// Logical y of an azimuth scan limit (signed angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            return LateralY(fromRunwayEndNM, Radar.ScanOffset(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
        }

        /// <summary>
        /// Line from the antenna along an azimuth angle to the given distance from the far end of the runway:
        /// straight with the linear scale, slightly curved with the compressed one.
        /// </summary>
        private void AddScanLine(double angle, double toFromRunwayEndNM, StyleElement element)
        {
            if (!Compressed)
            {
                AddLine(AntennaNM * xscale, CenterY, toFromRunwayEndNM * xscale, ScanY(toFromRunwayEndNM, angle), element);
                return;
            }
            double from = AntennaNM;
            AddCurve(t =>
            {
                double r = from + (toFromRunwayEndNM - from) * t;
                return new Point(XAt(r), ScanY(r, angle));
            }, element);
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
            AddLine(0, cy, XAt(length), cy, StyleElement.Runway);
            if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                double halfWidth = W(Runway.WidthNM) * yscale;
                AddLine(XAt(length), cy - halfWidth, XAt(length), cy + halfWidth, StyleElement.Runway);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Runway)) AddSymbol(Options.ThresholdSymbol, XAt(length), cy, Brush(StyleElement.Runway));
            }
            // Scan limits and beam edges, from the antenna.
            AddScanLine(left, end, StyleElement.ScanLimits);
            AddScanLine(right, end, StyleElement.ScanLimits);
            if (Options.ShowBeamEdges && Radar.BeamEnabled)
            {
                AddScanLine(beamLeft, end, StyleElement.AntennaBeam);
                AddScanLine(beamRight, end, StyleElement.AntennaBeam);
            }
            AddSymbol(Options.AntennaSymbol, XAt(AntennaNM), cy, Brush(StyleElement.Antenna));
            // Extended centreline and its approach limits, all starting at the touchdown point:
            // dashed between touchdown and threshold, solid beyond the threshold (straight with both scales).
            double xTouchdown = XAt(length - Runway.TouchdownNM);
            double xThreshold = XAt(length);
            double xEnd = XAt(end);
            AddLine(xTouchdown, cy, xThreshold, cy, StyleElement.Centerline, dashed: true);
            AddLine(xThreshold, cy, xEnd, cy, StyleElement.Centerline);
            foreach (bool isRight in new[] { false, true })
            {
                double sign = isRight ? 1 : -1;
                double atThreshold = LateralY(length, sign * Radar.ApproachHalfWidth(Runway.TouchdownNM, isRight));
                double atEnd = LateralY(end, sign * Radar.ApproachHalfWidth(Runway.TouchdownNM + range, isRight));
                if (Compressed)
                {
                    // Curves: distances compressed, lateral offsets linear.
                    double touchdownFromEnd = length - Runway.TouchdownNM;
                    Point At(double d) => new(XAt(touchdownFromEnd + d), LateralY(touchdownFromEnd + d, sign * Radar.ApproachHalfWidth(d, isRight)));
                    double thresholdD = Runway.TouchdownNM, endD = Runway.TouchdownNM + range;
                    AddCurve(t => At(thresholdD * t), StyleElement.ApproachLimits, dashed: true);
                    AddCurve(t => At(thresholdD + (endD - thresholdD) * t), StyleElement.ApproachLimits);
                    continue;
                }
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
                double reach = Options.DhAzimuthLength > 0 ? W(Options.DhAzimuthLength / 1852) * yscale : double.MaxValue;
                AddLine(XAt(interceptNM), Math.Max(low, cy - reach), XAt(interceptNM), Math.Min(high, cy + reach), StyleElement.DecisionHeightAzimuth);
                AddSymbol(Options.DhAzimuthSymbol, XAt(interceptNM), cy, Brush(StyleElement.DecisionHeightMark));
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            List<DistanceReminder> reminders = VisibleReminders();
            foreach ((double distance, StyleElement element, bool text) in Options.RangeMarks.Marks(range))
            {
                if (HasReminderLine(reminders, distance)) continue;
                double markNM = distance - Runway.TouchdownNM + length;
                // Ticks on the distance axis (the centreline): the marks with text are longer.
                double tickUp = Options.RangeMarks.TickAbove(text), tickDown = Options.RangeMarks.TickBelow(text);
                if (tickUp + tickDown > 0) AddLine(XAt(markNM), cy + tickDown, XAt(markNM), cy - tickUp, element);
                if (!Options.RangeMarks.DrawLines) continue;
                AddLine(XAt(markNM), ScanY(markNM, left), XAt(markNM), ScanY(markNM, right), element);
                // Thicker where the antenna beam looks.
                if (Radar.BeamEnabled)
                {
                    AddLine(XAt(markNM), ScanY(markNM, beamLeft), XAt(markNM), ScanY(markNM, beamRight), element, extraWidth: InBeamExtraWidth);
                }
            }
            // Distance reminders: only the line in this view (the markers are in the elevation view).
            foreach (DistanceReminder reminder in reminders.Where(r => r.HasLine))
            {
                double markNM = reminder.Distance - Runway.TouchdownNM + length;
                AddReminderLine(reminder, XAt(markNM), ScanY(markNM, left), XAt(markNM), ScanY(markNM, right));
            }
        }

        protected override (double Along, double Value) ToWorld(Aircraft aircraft)
        {
            return (aircraft.AlongTrackDistance(Runway), aircraft.LateralOffset(Runway));
        }

        protected override Point WorldToLogical(double along, double value)
        {
            double fromEnd = along + Runway.LengthNM;
            return new Point(XAt(fromEnd), LateralY(fromEnd, value));
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

        protected override double AxisY => CenterY;

        protected override Point SweepOrigin() => new(XAt(AntennaNM), CenterY);

        protected override Point SweepEnd(double position)
        {
            double end = Runway.LengthNM + Runway.Distance;
            double angle = Radar.AzimuthLeftEdge + position * (Radar.AzimuthRightEdge - Radar.AzimuthLeftEdge);
            return new Point(XAt(end), ScanY(end, angle));
        }
    }
}
