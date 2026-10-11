using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Elevation (profile) view. Logical x: pixels from the far end of the runway (runway end on the left,
    /// approach on the right); logical y: pixels from the top, ground (threshold elevation) at the bottom.
    /// </summary>
    internal class ProfileView(Canvas canvas, Runway runway, Radar radar, ViewOptions options) : RadarView(canvas, runway, radar, options)
    {
        /// <summary>
        /// Horizon (ground) line: above the bottom of the view by <see cref="bottomBand"/>, the room for the distance
        /// text or the reminder markers below it.
        /// </summary>
        private double H => Canvas.ActualHeight - bottomBand;
        private double bottomBand = 24;

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        private double AntennaNM => Radar.AntennaFromRunwayEnd(Runway);

        protected override void CalculateScale()
        {
            CalculateHorizontalScale();
            // Fixed vertical scale, depending only on the range: the default upper scan limit (8°) reaches the top at
            // the end of the range. Other scan limits or a tilt move the lines (beyond the view if needed), as on a real PAR.
            // With the compressed scale heights shrink with the distance (see RadarView.ShrinkAt): the same rule there.
            double end = Runway.Distance + Runway.LengthNM;
            double top = Radar.ScanHeight(end - AntennaNM, Radar.ReferenceScanUp) * ShrinkAt(end);
            // Room below the horizon for the distance text or the reminder markers (the larger of the two).
            double markers = VisibleReminders().Where(r => r.HasMarker).Select(r => r.Size + 8).DefaultIfEmpty(0).Max();
            // Ticks below the horizon line push the distance text (when it is below) further down.
            double textRoom = Options.RangeMarks.TextSize + 10 + (Options.RangeTextBelowHorizon ? Options.RangeMarks.TickBelow(true) : 0);
            bottomBand = Math.Max(textRoom, markers + Options.RangeMarks.TickBelow(true));
            yscale = (H - 30) / top;
        }

        /// <summary>
        /// Logical x of a point at the given distance from the touchdown point.
        /// </summary>
        private double X(double distanceFromTouchdownNM)
        {
            return XAt(Runway.LengthNM - Runway.TouchdownNM + distanceFromTouchdownNM);
        }

        /// <summary>
        /// Logical y of a height in ft above the threshold elevation, with the linear scale (or at the touchdown).
        /// </summary>
        private double Y(double heightFt)
        {
            return H - heightFt * yscale;
        }

        /// <summary>
        /// Logical y of a height in ft above the threshold elevation at the given distance from the touchdown
        /// (heights shrink with the distance with the compressed scale; the same as <see cref="Y"/> otherwise).
        /// </summary>
        private double Y(double distanceFromTouchdownNM, double heightFt)
        {
            return YEnd(Runway.LengthNM - Runway.TouchdownNM + distanceFromTouchdownNM, heightFt);
        }

        /// <summary>Logical y of a height in ft at the given distance (NM) from the far end of the runway.</summary>
        private double YEnd(double fromRunwayEndNM, double heightFt)
        {
            return H - heightFt * ShrinkAt(fromRunwayEndNM) * yscale;
        }

        /// <summary>
        /// Logical y of an elevation scan limit (angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            return YEnd(fromRunwayEndNM, Radar.ScanHeight(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
        }

        /// <summary>
        /// Line from the antenna along an elevation angle to the given distance from the far end of the runway:
        /// straight with the linear scale, slightly curved with the compressed one.
        /// </summary>
        private void AddScanLine(double angle, double toFromRunwayEndNM, StyleElement element)
        {
            if (!Compressed)
            {
                AddLine(AntennaNM * xscale, H, toFromRunwayEndNM * xscale, ScanY(toFromRunwayEndNM, angle), element);
                return;
            }
            double from = AntennaNM;
            AddCurve(t =>
            {
                double r = from + (toFromRunwayEndNM - from) * t;
                return new Point(XAt(r), ScanY(r, angle));
            }, element);
        }

        /// <summary>
        /// Draws a line starting at the touchdown point with angle GlideSlope + angleOffset: its last part (from the
        /// threshold or the DH, see <see cref="RadarView.FinalSegmentOf"/>) dashed, the rest in its own style.
        /// </summary>
        private void AddGlidePathLine(double angleOffset, StyleElement element)
        {
            (double split, LineDash dash) = FinalSegmentOf(element);
            double end = Runway.TouchdownNM + Runway.Distance;
            // Straight with both scales (a line from the touchdown point, see RadarView.ShrinkAt).
            if (split > 0) AddLine(X(0), Y(0), X(split), Y(split, Runway.GlidePathHeight(split, angleOffset)), element, dash: dash);
            AddLine(X(split), Y(split, Runway.GlidePathHeight(split, angleOffset)), X(end), Y(end, Runway.GlidePathHeight(end, angleOffset)), element);
        }

        protected override void DrawStatic()
        {
            double length = Runway.LengthNM;
            double range = Runway.Distance;
            double end = length + range;
            // Scan limits (physical, fixed) and antenna beam (moved by the tilt inside them).
            double upper = Radar.ScanUp;
            double lower = Radar.ScanDown;
            double beamUpper = Radar.ElevationUpper;
            double beamLower = Radar.ElevationLower;
            if (Options.ShowAltitudeLines || (Compressed && Options.ShowAltitudeScale)) DrawAltitudeLines(end, upper, lower);
            // Ground beyond the threshold.
            AddLine(XAt(length), H, XAt(end), H, StyleElement.Ground);
            // Runway.
            AddLine(0, H, XAt(length), H, StyleElement.Runway);
            // Threshold: a line up to the scan limit, or a symbol on the runway.
            if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                AddLine(XAt(length), H, XAt(length), Math.Min(H, ScanY(length, upper)), StyleElement.Runway);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Runway)) AddSymbol(Options.ThresholdSymbol, XAt(length), H - Options.ThresholdSymbol.Size / 2, Brush(StyleElement.Runway));
            }
            // Scan limits and beam edges, from the antenna (the lower ones only when above the ground).
            AddScanLine(upper, end, StyleElement.ScanLimits);
            if (lower > 0)
            {
                AddScanLine(lower, end, StyleElement.ScanLimits);
            }
            if (Options.ShowBeamEdges && Radar.BeamEnabled)
            {
                AddScanLine(beamUpper, end, StyleElement.AntennaBeam);
                if (beamLower > 0)
                {
                    AddScanLine(beamLower, end, StyleElement.AntennaBeam);
                }
            }
            AddSymbol(Options.AntennaSymbol, XAt(AntennaNM), H - Options.AntennaSymbol.Size / 2, Brush(StyleElement.Antenna));
            // Glide path and its approach limits, all starting at the touchdown point.
            AddGlidePathLine(0, StyleElement.GlidePath);
            AddGlidePathLine(-Radar.ApproachBelow, StyleElement.ApproachLimits);
            AddGlidePathLine(Radar.ApproachAbove, StyleElement.ApproachLimits);
            // Decision height: horizontal line from the touchdown point to 3 NM (or the end of the display),
            // and a dashed vertical line from its intercept with the glide path down to the runway axis.
            double displayEnd = Runway.TouchdownNM + range;
            // Everything starts from the point where the glide path reaches the DH.
            double intercept = Runway.MissedApproachPointNM;
            if (intercept <= displayEnd)
            {
                // Horizontal line: left, right or both sides of the point on the screen (the runway may be on either side).
                bool towardsApproach = Options.DhLineSide is DhHorizontalSide.Left or DhHorizontalSide.Right && (Options.DhLineSide == DhHorizontalSide.Right) != RunwayOnRight;
                bool towardsRunway = Options.DhLineSide is DhHorizontalSide.Left or DhHorizontalSide.Right && !towardsApproach;
                // Length 0: up to the edge of the view (a line across the screen, as on some scopes).
                bool toEdge = Options.DhLineLength <= 0;
                double touchdownFromEnd = Runway.LengthNM - Runway.TouchdownNM;
                double edgeLow = FromRunwayEndOfScale(-XShift / xscale) - touchdownFromEnd;
                double edgeHigh = FromRunwayEndOfScale((Canvas.ActualWidth - XShift) / xscale) - touchdownFromEnd;
                double from = towardsApproach ? intercept : toEdge ? edgeLow : Math.Max(0, intercept - Options.DhLineLength);
                double to = towardsRunway ? intercept : toEdge ? edgeHigh : Math.Min(displayEnd, intercept + Options.DhLineLength);
                if (Options.DhLineSide != DhHorizontalSide.None)
                {
                    // Constant height: straight with the linear scale, a slight curve with the compressed one.
                    if (Compressed) AddCurve(t => { double d = from + (to - from) * t; return new Point(X(d), Y(d, Runway.MDH)); }, StyleElement.DecisionHeight);
                    else AddLine(X(from), Y(Runway.MDH), X(to), Y(Runway.MDH), StyleElement.DecisionHeight);
                }
                // Vertical line: down, up or both; by default as long as the DH (down to the ground).
                double drop = Options.DhDropLength > 0 ? Options.DhDropLength : Runway.MDH;
                double down = Options.DhDropSide is DhVerticalSide.Down or DhVerticalSide.Both ? Math.Min(drop, Runway.MDH) : 0;
                double up = Options.DhDropSide is DhVerticalSide.Up or DhVerticalSide.Both ? drop : 0;
                if (Options.DhDropSide != DhVerticalSide.None) AddLine(X(intercept), Y(intercept, Runway.MDH - down), X(intercept), Y(intercept, Runway.MDH + up), StyleElement.DecisionHeightDrop);
                AddSymbol(Options.DhSymbol, X(intercept), Y(intercept, Runway.MDH), Brush(StyleElement.DecisionHeightMark));
            }
            // Touchdown point: origin of the range marks and of the glide path.
            if (Options.TouchdownSymbol.Shape == SymbolShape.Line)
            {
                AddLine(X(0), Y(0), X(0), Y(0) - Options.TouchdownSymbol.Size, StyleElement.Touchdown);
            }
            else
            {
                if (!Options.Theme.IsHidden(StyleElement.Touchdown)) AddSymbol(Options.TouchdownSymbol, X(0), H - Options.TouchdownSymbol.Size / 2, Brush(StyleElement.Touchdown));
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            List<DistanceReminder> reminders = VisibleReminders();
            // The horizon line is the base: distance text above it and reminder markers below, or the opposite.
            bool textBelow = Options.RangeTextBelowHorizon;
            foreach ((double distance, StyleElement element, bool text) in Options.RangeMarks.Marks(range))
            {
                double markNM = length - Runway.TouchdownNM + distance;
                double top = ScanY(markNM, upper);
                double bottom = lower > 0 ? ScanY(markNM, lower) : H;
                // A reminder line at the same distance replaces the range mark (the text stays).
                // Ticks on the distance axis (the horizon line): the marks with text are longer.
                double tickUp = Options.RangeMarks.TickAbove(text), tickDown = Options.RangeMarks.TickBelow(text);
                if (tickUp + tickDown > 0 && !HasReminderLine(reminders, distance))
                {
                    AddLine(XAt(markNM), H + tickDown, XAt(markNM), H - tickUp, element);
                }
                if (Options.RangeMarks.DrawLines && top < bottom && !HasReminderLine(reminders, distance))
                {
                    AddLine(XAt(markNM), bottom, XAt(markNM), top, element);
                    // Thicker where the antenna beam looks.
                    double beamTop = ScanY(markNM, beamUpper);
                    double beamBottom = beamLower > 0 ? ScanY(markNM, beamLower) : H;
                    if (Radar.BeamEnabled && beamTop < beamBottom)
                    {
                        AddLine(XAt(markNM), beamBottom, XAt(markNM), beamTop, element, extraWidth: InBeamExtraWidth);
                    }
                }
                if (text)
                {
                    AddText(Options.RangeMarks.Label(distance, range), XAt(markNM), textBelow ? H + 2 + tickDown : H - 1 - tickUp, -10, Brush(StyleElement.RangeText), aboveAnchor: !textBelow);
                }
            }
            // Distance reminders: line between the scan limits and/or marker on the other side of the horizon from the text.
            foreach (DistanceReminder reminder in reminders)
            {
                double markNM = length - Runway.TouchdownNM + reminder.Distance;
                double top = ScanY(markNM, upper);
                double bottom = lower > 0 ? ScanY(markNM, lower) : H;
                if (reminder.HasLine && top < bottom)
                {
                    AddReminderLine(reminder, XAt(markNM), bottom, XAt(markNM), top);
                }
                if (reminder.HasMarker)
                {
                    double y = textBelow
                        ? H - 3 - reminder.Size / 2
                        : H + 3 + reminder.Size / 2;
                    AddReminderMarker(reminder, XAt(markNM), y);
                }
            }
            // With the compressed scale heights depend on the distance: no scale on the side, the values are written
            // on the altitude lines instead (see DrawAltitudeLines).
            if (Options.ShowAltitudeScale && !Compressed)
            {
                DrawAltitudeScale();
            }
        }

        /// <summary>
        /// Horizontal lines every 1000 (ft or m, as the scale), inside the scan limits, from the upper limit to the
        /// end of the range (or to the lower limit when it is above the ground).
        /// </summary>
        private void DrawAltitudeLines(double end, double upper, double lower)
        {
            if (Compressed)
            {
                DrawCompressedAltitudeLines(end, upper, lower);
                return;
            }
            const double FeetPerMetre = 1 / 0.3048;
            double unitToFeet = Options.ScaleInMetres ? FeetPerMetre : 1;
            double baseValue = Options.Qfe ? 0 : Runway.Elevation / unitToFeet;
            double topValue = baseValue + H / yscale / unitToFeet;
            double tanUpper = Math.Tan(upper * Math.PI / 180);
            if (tanUpper <= 0) return;
            for (double value = (Math.Floor(baseValue / 1000) + 1) * 1000; value <= topValue; value += 1000)
            {
                double heightFt = (value - baseValue) * unitToFeet;
                if (heightFt <= 0) continue;
                double y = Y(heightFt);
                if (y < 0) break;
                double heightNM = heightFt / Runway.FeetPerNM;
                double fromX = (AntennaNM + heightNM / tanUpper) * xscale;
                double toX = end * xscale;
                if (lower > 0) toX = Math.Min(toX, (AntennaNM + heightNM / Math.Tan(lower * Math.PI / 180)) * xscale);
                if (fromX < toX) AddLine(fromX, y, toX, y, StyleElement.AltitudeLines);
            }
        }

        /// <summary>
        /// Compressed scale: heights shrink with the distance, so a constant altitude is a curve going down towards
        /// the far end. The lines every 1000 (option) are drawn as such curves, and the altitude scale (option) is
        /// written as values where they start, on the upper scan limit (a scale on the side would be right only at
        /// the touchdown).
        /// </summary>
        private void DrawCompressedAltitudeLines(double end, double upper, double lower)
        {
            const double FeetPerMetre = 1 / 0.3048;
            double unitToFeet = Options.ScaleInMetres ? FeetPerMetre : 1;
            string unit = Options.ScaleInMetres ? "m" : "ft";
            double baseValue = Options.Qfe ? 0 : Runway.Elevation / unitToFeet;
            // Highest value seen in the view: at the far end, where heights shrink the most.
            double topValue = baseValue + H / (yscale * ShrinkAt(end)) / unitToFeet;
            double tanUpper = Math.Tan(upper * Math.PI / 180);
            if (tanUpper <= 0) return;
            double tanLower = Math.Tan(lower * Math.PI / 180);
            // The lines every 1000; the values at the step of the altitude scale (about six in the view).
            double[] steps = [10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000];
            double textStep = steps.FirstOrDefault(s => s >= (topValue - baseValue) / 6, steps[^1]);
            bool lines = Options.ShowAltitudeLines;
            bool texts = Options.ShowAltitudeScale && !Options.Theme.IsHidden(StyleElement.AltitudeScale);
            double step = lines && texts ? Math.Min(1000, textStep) : lines ? 1000 : textStep;
            for (double value = (Math.Floor(baseValue / step) + 1) * step; value <= topValue; value += step)
            {
                double heightFt = (value - baseValue) * unitToFeet;
                if (heightFt <= 0) continue;
                double heightNM = heightFt / Runway.FeetPerNM;
                double from = AntennaNM + heightNM / tanUpper;
                double to = end;
                if (lower > 0) to = Math.Min(to, AntennaNM + heightNM / tanLower);
                if (from >= to) continue;
                if (lines && IsMultiple(value, 1000))
                {
                    AddCurve(t => { double r = from + (to - from) * t; return new Point(XAt(r), YEnd(r, heightFt)); }, StyleElement.AltitudeLines);
                }
                if (texts && IsMultiple(value, textStep))
                {
                    // Where the line starts, or where it comes into the view (scan limit tilted above the top).
                    double at = from;
                    for (int i = 0; i <= 48 && YEnd(at, heightFt) <= 14; i++) at = from + (to - from) * i / 48;
                    double y = YEnd(at, heightFt);
                    if (y > 14)
                    {
                        string text = $"{value:0} {unit}";
                        TextBlock measure = new() { Text = text, FontSize = Options.RangeMarks.TextSize };
                        measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        double width = measure.DesiredSize.Width;
                        if (at == from)
                        {
                            // On the upper scan limit: just outside it, above and towards the runway, so the text
                            // never touches the scan limit (which goes down towards the runway) or the altitude line.
                            double dx = RunwayOnRight ? 4 : -4 - width;
                            // Inside the view (low values near the antenna, at the edge on the runway side).
                            double left = ToScreenX(XAt(at)) + dx;
                            dx += Math.Clamp(left, 2, Math.Max(2, Canvas.ActualWidth - width - 2)) - left;
                            if (y - 2 - measure.DesiredSize.Height > 0) AddText(text, XAt(at), y - 2, dx, Brush(StyleElement.AltitudeScale), aboveAnchor: true);
                        }
                        else
                        {
                            // The line comes into the view from the top: under it, towards the far end.
                            double dx = RunwayOnRight ? -4 - width : 4;
                            AddText(text, XAt(at), y + 2, dx, Brush(StyleElement.AltitudeScale), aboveAnchor: false);
                        }
                    }
                }
            }
        }

        private static bool IsMultiple(double value, double step)
        {
            double ratio = value / step;
            return Math.Abs(ratio - Math.Round(ratio)) < 1e-6;
        }

        /// <summary>
        /// Altitude scale on the runway side: altitudes with QNH, heights above the threshold with QFE,
        /// in feet or metres, at a round step chosen for the current zoom.
        /// </summary>
        private void DrawAltitudeScale()
        {
            // Hidden in this mode (Display style → Colours & lines, Show): no ticks and no values.
            if (Options.Theme.IsHidden(StyleElement.AltitudeScale)) return;
            const double FeetPerMetre = 1 / 0.3048;
            double unitToFeet = Options.ScaleInMetres ? FeetPerMetre : 1;
            string unit = Options.ScaleInMetres ? "m" : "ft";
            // Value (in display units) at the bottom (threshold elevation) and at the top of the view.
            double baseValue = Options.Qfe ? 0 : Runway.Elevation / unitToFeet;
            double topValue = baseValue + H / yscale / unitToFeet;
            double[] steps = [10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000];
            double step = steps.FirstOrDefault(s => s >= (topValue - baseValue) / 6, steps[^1]);
            for (double value = Math.Ceiling(baseValue / step) * step; value <= topValue; value += step)
            {
                double y = Y((value - baseValue) * unitToFeet);
                if (y < 10 || y > H - 22) continue;
                // At the edge of the view (logical x minus the shift, see XShift).
                AddLine(-XShift, y, 8 - XShift, y, StyleElement.AltitudeScale);
                AddSideText($"{value:0} {unit}", y, Brush(StyleElement.AltitudeScale));
            }
        }

        protected override (double Along, double Value) ToWorld(Aircraft aircraft)
        {
            return (aircraft.AlongTrackDistance(Runway), aircraft.Altitude - Runway.Elevation);
        }

        protected override Point WorldToLogical(double along, double value)
        {
            double fromEnd = along + Runway.LengthNM;
            return new Point(XAt(fromEnd), YEnd(fromEnd, value));
        }

        protected override bool IsWithinLimits(Aircraft aircraft)
        {
            return Radar.IsWithinGlidePathLimits(aircraft, Runway);
        }

        protected override LabelLayout Layout => Options.ElevationLabel;

        protected override bool IsElevation => true;

        protected override double HorizonY => H;

        protected override double AxisY => H;

        protected override Point SweepOrigin() => new(XAt(AntennaNM), H);

        protected override Point SweepEnd(double position)
        {
            double end = Runway.LengthNM + Runway.Distance;
            double angle = Radar.ElevationLower + position * (Radar.ElevationUpper - Radar.ElevationLower);
            return new Point(XAt(end), ScanY(end, angle));
        }
    }
}
