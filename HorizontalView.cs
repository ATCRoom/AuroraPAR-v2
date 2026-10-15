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

        /// <summary>
        /// Logical y of the axis: the extended centreline (heights scale), or the direction of the antenna, parallel to
        /// the centreline (angular scale; not in the middle when the scan limits left and right differ).
        /// </summary>
        private double CenterY => Angular ? angularAxis : Canvas.ActualHeight / 2;
        private double angularAxis;

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        private double AntennaNM => Radar.AntennaFromRunwayEnd(Runway);

        protected override void CalculateScale()
        {
            CalculateHorizontalScale();
            // Fixed lateral scale, depending only on the range: the default scan limits (±10°) fill the view at the end
            // of the range. Other scan limits or a tilt move the lines (beyond the view if needed), as on a real PAR.
            // With the compressed scale lateral offsets shrink with the distance (see RadarView.ShrinkAt): the same rule there.
            double end = Runway.Distance + Runway.LengthNM;
            double half = Radar.ScanOffset(end - AntennaNM, Radar.ReferenceScanHalfWidth) * ShrinkAt(end);
            yscale = (Canvas.ActualHeight - 50) / (2 * half);
            // Angular scale: the scan limits at the edges of the view (right of the approach upwards).
            PixelsPerDegree = Math.Max(1, Canvas.ActualHeight - 24) / Math.Max(1, Radar.ScanLeft + Radar.ScanRight);
            angularAxis = 12 + Radar.ScanRight * PixelsPerDegree;
        }

        /// <summary>
        /// Logical y of a lateral offset in NM (positive right of the approach).
        /// </summary>
        private double LateralY(double fromRunwayEndNM, double offsetNM)
        {
            // Angular scale: the azimuth seen from the antenna (which may stand beside the runway).
            if (Angular) return CenterY - AngleFromAntenna(fromRunwayEndNM, offsetNM - Radar.AntennaOffsetNM) * PixelsPerDegree;
            return CenterY - offsetNM * ShrinkAt(fromRunwayEndNM) * yscale;
        }

        /// <summary>
        /// Logical y of an azimuth scan limit (signed angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            if (Angular) return CenterY - angle * PixelsPerDegree;
            // From the antenna, beside the runway when it is there.
            return LateralY(fromRunwayEndNM, Radar.AntennaOffsetNM + Radar.ScanOffset(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
        }

        /// <summary>
        /// Line from the antenna along an azimuth angle to the given distance from the far end of the runway:
        /// straight with the linear scale, slightly curved with the compressed one.
        /// </summary>
        private void AddScanLine(double angle, double toFromRunwayEndNM, StyleElement element)
        {
            // Straight with the linear distances, slightly curved with the compressed ones, horizontal with the
            // angular scale.
            if (!Compressed || Angular)
            {
                AddLine(XAt(AntennaNM), ScanY(AntennaNM, angle), XAt(toFromRunwayEndNM), ScanY(toFromRunwayEndNM, angle), element);
                return;
            }
            double from = AntennaNM;
            AddCurve(t =>
            {
                double r = from + (toFromRunwayEndNM - from) * t;
                return new Point(XAt(r), ScanY(r, angle));
            }, element);
        }

        private static readonly double[] LateralSteps = [500, 1000, 2000, 2500, 5000, 10000, 20000];
        private static readonly double[] LateralStepsMetres = [100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000];

        /// <summary>Unit of the lateral scale: the unit of the heights (feet or metres), its name and NM per unit.</summary>
        private (double[] Steps, string Name, double NMPerUnit) LateralUnit => Options.ScaleInMetres
            ? (LateralStepsMetres, "m", 1 / 1852.0)
            : (LateralSteps, "ft", 1 / Runway.FeetPerNM);

        /// <summary>
        /// Step of the lateral scale: three or four values a side, but always at least two (2 + 2), so a range where the
        /// natural step is more than half the view (15 and 30 NM) takes the next smaller one. <paramref name="half"/>:
        /// half the view in the unit of the scale.
        /// </summary>
        private double LateralStep(double half)
        {
            double[] steps = LateralUnit.Steps;
            double step = steps.FirstOrDefault(s => s >= half / 3.5, steps[^1]);
            // The second value must be inside the view (a little margin for the edge of the canvas).
            while (step > steps[0] && 2 * step > half * 0.95)
            {
                step = steps[Math.Max(0, Array.IndexOf(steps, step) - 1)];
            }
            return step;
        }

        /// <summary>
        /// Lateral scale (LIN): feet from the centreline on the side of the view, 0 at the centreline and the same
        /// values on both sides (no sign), at a round step (three or four values a side).
        /// </summary>
        private void DrawLateralScale()
        {
            (_, string unit, double nmPerUnit) = LateralUnit;
            double half = CenterY / yscale / nmPerUnit;
            double step = LateralStep(half);
            for (double value = 0; value <= half; value += step)
            {
                foreach (int sign in value == 0 ? new[] { 1 } : new[] { 1, -1 })
                {
                    double y = CenterY - sign * value * nmPerUnit * yscale;
                    if (y < 10 || y > Canvas.ActualHeight - 10) continue;
                    AddLine(-XShift, y, 8 - XShift, y, StyleElement.AltitudeScale);
                    AddSideText($"{value:0} {unit}", y, Brush(StyleElement.AltitudeScale));
                }
            }
        }

        /// <summary>
        /// Lateral scale (LOG): offsets shrink with the distance, so a constant offset is a curve getting closer to the
        /// centreline towards the far end. Each value is such a curve (style of the altitude lines) from the scan
        /// limit, with the value written where it starts, outside the scan limits.
        /// </summary>
        private void DrawCompressedLateralScale(double end, double scanLeft, double scanRight)
        {
            (_, string unit, double nmPerUnit) = LateralUnit;
            double half = CenterY / yscale / nmPerUnit;
            double step = LateralStep(half);
            double maxValue = Options.ScaleInMetres ? 10000 : 30000;
            foreach (bool isRight in new[] { true, false })
            {
                double tan = Math.Tan(Math.Abs(isRight ? scanRight : scanLeft) * Math.PI / 180);
                if (tan <= 0) continue;
                double sign = isRight ? 1 : -1;
                for (double value = step; value <= maxValue; value += step)
                {
                    double offsetNM = value * nmPerUnit;
                    // Where the offset comes into the scan, seen from the antenna (beside the runway when it is there).
                    double fromAntenna = (offsetNM - sign * Radar.AntennaOffsetNM) / tan;
                    if (fromAntenna <= 0) continue;
                    double from = AntennaNM + fromAntenna;
                    if (from >= end) break;
                    AddCurve(t =>
                    {
                        double r = from + (end - from) * t;
                        return new Point(XAt(r), LateralY(r, sign * offsetNM));
                    }, StyleElement.AltitudeLines);
                    // Outside the scan limits: above the upper line, below the lower one (on the screen, which is
                    // mirrored with the runway on the right), on the runway side of the point.
                    string text = $"{value:0} {unit}";
                    bool screenUp = isRight != RunwayOnRight;
                    double dx = RunwayOnRight ? 4 : -4 - TextWidth(text);
                    // Only when the whole text is inside the view (near the edge it would be cut, or go over the other view).
                    double y = LateralY(from, sign * offsetNM);
                    double textHeight = TextHeight(new TextBlock { Text = text, FontSize = Options.RangeMarks.TextSize });
                    double top = ToScreenY(y) - (screenUp ? textHeight : 0);
                    if (top < 0 || top + textHeight > Canvas.ActualHeight) continue;
                    AddText(text, XAt(from), y, dx, Brush(StyleElement.AltitudeScale), aboveAnchor: screenUp);
                }
            }
        }

        private double TextWidth(string text)
        {
            TextBlock measure = new() { Text = text, FontSize = Options.RangeMarks.TextSize };
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return measure.DesiredSize.Width;
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
            // Runway and threshold (a line across the runway, or a symbol). With the angular scale and the antenna
            // beside the runway the runway is seen as a curve, from the antenna (behind it nothing is seen).
            if (Angular && Radar.AntennaOffsetNM != 0)
            {
                double runwayFrom = AntennaNM + 0.001;
                if (runwayFrom < length) AddCurve(t => { double r = SampleFromEnd(runwayFrom, length, t); return new Point(XAt(r), LateralY(r, 0)); }, StyleElement.Runway, segments: AngularCurveSegments);
            }
            else
            {
                AddLine(0, cy, XAt(length), cy, StyleElement.Runway);
            }
            if (Options.ThresholdTick)
            {
                // A tick like the DH one: across the centreline by the length of the DH azimuth line (or up to the scan limits).
                double low = Math.Min(ScanY(length, Radar.ScanLeftEdge), ScanY(length, Radar.ScanRightEdge));
                double high = Math.Max(ScanY(length, Radar.ScanLeftEdge), ScanY(length, Radar.ScanRightEdge));
                if (Options.DhAzimuthLength > 0)
                {
                    double lengthNM = Options.DhAzimuthLength / 1852;
                    double a = LateralY(length, -lengthNM), b = LateralY(length, lengthNM);
                    low = Math.Max(low, Math.Min(a, b));
                    high = Math.Min(high, Math.Max(a, b));
                }
                if (low < high) AddLine(XAt(length), low, XAt(length), high, StyleElement.DecisionHeightAzimuth);
            }
            else if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                AddLine(XAt(length), LateralY(length, -Runway.WidthNM), XAt(length), LateralY(length, Runway.WidthNM), StyleElement.Runway);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Runway)) AddSymbol(Options.ThresholdSymbol, XAt(length), LateralY(length, 0), Brush(StyleElement.Runway));
            }
            // Scan limits and beam edges, from the antenna.
            AddScanLine(left, end, StyleElement.ScanLimits);
            AddScanLine(right, end, StyleElement.ScanLimits);
            if (Options.ShowBeamEdges && Radar.BeamEnabled)
            {
                AddScanLine(beamLeft, end, StyleElement.AntennaBeam);
                AddScanLine(beamRight, end, StyleElement.AntennaBeam);
            }
            AddSymbol(Options.AntennaSymbol, XAt(AntennaNM), ScanY(AntennaNM, 0), Brush(StyleElement.Antenna));
            // Extended centreline and its approach limits, all starting at the touchdown point: the last part (from
            // the threshold or the DH) dashed, the rest in their own style (straight with both distance scales, curves
            // with the angular scale).
            double touchdownFromEnd = length - Runway.TouchdownNM;
            double xTouchdown = XAt(touchdownFromEnd);
            double displayEnd = Runway.TouchdownNM + range;
            (double centreSplit, LineDash centreDash) = FinalSegmentOf(StyleElement.Centerline);
            AddLateralLine(0, Math.Min(centreSplit, displayEnd), _ => 0, StyleElement.Centerline, centreDash);
            AddLateralLine(Math.Min(centreSplit, displayEnd), displayEnd, _ => 0, StyleElement.Centerline);
            (double limitSplit, LineDash limitDash) = FinalSegmentOf(StyleElement.ApproachLimits);
            // The approach limits may end before the end of the range (as on the PAR-E, at 10 km).
            double limitsEnd = Options.ApproachLimitsTo > 0 ? Math.Min(displayEnd, Options.ApproachLimitsTo) : displayEnd;
            limitSplit = Math.Min(limitSplit, limitsEnd);
            foreach (bool isRight in Options.AzimuthApproachLimits ? new[] { false, true } : Array.Empty<bool>())
            {
                double sign = isRight ? 1 : -1;
                bool onRight = isRight;
                double Offset(double s) => sign * Radar.ApproachHalfWidth(s, onRight);
                AddLateralLine(0, limitSplit, Offset, StyleElement.ApproachLimits, limitDash);
                AddLateralLine(limitSplit, limitsEnd, Offset, StyleElement.ApproachLimits);
            }
            if (Options.ShowLateralScale && !Options.Theme.IsHidden(StyleElement.AltitudeScale))
            {
                if (Angular) DrawAngleScale(CenterY, -Radar.ScanLeft, Radar.ScanRight, signed: false);
                else if (Compressed) DrawCompressedLateralScale(end, left, right);
                else DrawLateralScale();
            }
            // Safety limits (optional): outside the approach limits, only the part inside the scan limits.
            if (Radar.SafetyLimitsAzimuth)
            {
                (double safetySplit, LineDash safetyDash) = FinalSegmentOf(StyleElement.SafetyLimits);
                foreach (bool isRight in new[] { false, true })
                {
                    double sign = isRight ? 1 : -1;
                    double tan = Math.Tan((isRight ? Radar.SafetyRight : Radar.SafetyLeft) * Math.PI / 180);
                    double Offset(double s) => sign * s * tan;
                    bool Inside(double s)
                    {
                        double fromAntenna = touchdownFromEnd + s - AntennaNM;
                        if (fromAntenna <= 0) return false;
                        // Seen from the antenna (beside the runway when it is there).
                        double offset = Offset(s) - Radar.AntennaOffsetNM;
                        return offset >= Radar.ScanOffset(fromAntenna, left) && offset <= Radar.ScanOffset(fromAntenna, right);
                    }
                    if (VisiblePart(0, Runway.TouchdownNM + range, Inside) is not { } part) continue;
                    double a = part.From, b = part.To;
                    if (safetySplit > a) AddLateralLine(a, Math.Min(safetySplit, b), Offset, StyleElement.SafetyLimits, safetyDash);
                    double start = Math.Max(a, safetySplit);
                    if (start < b) AddLateralLine(start, b, Offset, StyleElement.SafetyLimits);
                }
            }
            // Touchdown point (on the centreline, a curve with the angular scale and the antenna beside the runway).
            double yTouchdown = LateralY(touchdownFromEnd, 0);
            if (Options.TouchdownSymbol.Shape == SymbolShape.Line)
            {
                double half = Options.TouchdownSymbol.Size * 2 / 3;
                AddLine(xTouchdown, yTouchdown - half, xTouchdown, yTouchdown + half, StyleElement.Touchdown);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Touchdown)) AddSymbol(Options.TouchdownSymbol, xTouchdown, yTouchdown, Brush(StyleElement.Touchdown));
            }
            // Distance where the glide path reaches the decision height: vertical line between the scan limits.
            double interceptNM = length - Runway.TouchdownNM + Runway.MissedApproachPointNM;
            if (interceptNM <= end)
            {
                // Both sides of the centreline: up to the scan limits, or a length in metres (never beyond the limits).
                double yLeft = ScanY(interceptNM, left), yRight = ScanY(interceptNM, right);
                double low = Math.Min(yLeft, yRight), high = Math.Max(yLeft, yRight);
                double centre = LateralY(interceptNM, 0);
                if (Options.DhAzimuthLength > 0)
                {
                    double lengthNM = Options.DhAzimuthLength / 1852;
                    double a = LateralY(interceptNM, -lengthNM), b = LateralY(interceptNM, lengthNM);
                    low = Math.Max(low, Math.Min(a, b));
                    high = Math.Min(high, Math.Max(a, b));
                }
                if (low < high) AddLine(XAt(interceptNM), low, XAt(interceptNM), high, StyleElement.DecisionHeightAzimuth);
                AddSymbol(Options.DhAzimuthSymbol, XAt(interceptNM), centre, Brush(StyleElement.DecisionHeightMark));
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            List<DistanceReminder> reminders = VisibleReminders();
            foreach ((double distance, StyleElement element, bool text, bool isShort) in Options.RangeMarks.Marks(range, Options.DistanceUnit))
            {
                if (HasReminderLine(reminders, distance)) continue;
                double markNM = distance - Runway.TouchdownNM + length;
                // Ticks on the distance axis (the centreline): the marks with text are longer.
                double tickUp = Options.RangeMarks.TickAbove(text), tickDown = Options.RangeMarks.TickBelow(text);
                // On the centreline (a curve with the angular scale and the antenna beside the runway).
                double tickY = LateralY(markNM, 0);
                if (tickUp + tickDown > 0) AddLine(XAt(markNM), tickY + tickDown, XAt(markNM), tickY - tickUp, element);
                if (!Options.RangeMarks.DrawLines) continue;
                if (isShort)
                {
                    // Short line (beyond the full lines), centred on the centreline, shorter towards the end.
                    double low = Math.Min(ScanY(markNM, left), ScanY(markNM, right)), high = Math.Max(ScanY(markNM, left), ScanY(markNM, right));
                    double centre = LateralY(markNM, 0);
                    double half = (high - low) * ShortMarkFraction(distance) / 2;
                    double y1 = Math.Max(low, centre - half), y2 = Math.Min(high, centre + half);
                    if (y1 < y2) AddLine(XAt(markNM), y1, XAt(markNM), y2, element);
                    continue;
                }
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

        protected override Point SweepOrigin() => new(XAt(AntennaNM), ScanY(AntennaNM, 0));

        /// <summary>
        /// Line at a lateral offset (NM, positive right) varying along the approach, between <paramref name="a"/> and
        /// <paramref name="b"/> NM from the touchdown: straight (the offsets of the centreline and of the limits grow
        /// linearly from the touchdown point), or a curve with the angular scale.
        /// </summary>
        private void AddLateralLine(double a, double b, Func<double, double> offset, StyleElement element, LineDash? dash = null)
        {
            if (b <= a) return;
            double touchdownFromEnd = Runway.LengthNM - Runway.TouchdownNM;
            if (Angular)
            {
                AddCurve(t =>
                {
                    double s = SampleFromEnd(touchdownFromEnd + a, touchdownFromEnd + b, t) - touchdownFromEnd;
                    return new Point(XAt(touchdownFromEnd + s), LateralY(touchdownFromEnd + s, offset(s)));
                }, element, dash: dash, segments: AngularCurveSegments);
                return;
            }
            AddLine(XAt(touchdownFromEnd + a), LateralY(touchdownFromEnd + a, offset(a)), XAt(touchdownFromEnd + b), LateralY(touchdownFromEnd + b, offset(b)), element, dash: dash);
        }

        protected override Point SweepEnd(double position)
        {
            double end = Runway.LengthNM + Runway.Distance;
            double angle = Radar.AzimuthLeftEdge + position * (Radar.AzimuthRightEdge - Radar.AzimuthLeftEdge);
            return new Point(XAt(end), ScanY(end, angle));
        }
    }
}
