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

        private DeviationCrosshair? crosshair;

        protected override bool IsNoiseArea(Point logical) => logical.Y < H;

        /// <summary>
        /// Deviation crosshair (option) of <paramref name="aircraft"/>, at the top on the runway side, beside the
        /// information area (<paramref name="inset"/>: its width from the edge, pixels); with no aircraft the empty cross.
        /// </summary>
        public void SetCrosshair(Aircraft? aircraft, double inset = 0)
        {
            if (!Options.DeviationCrosshair || Options.Analog || Options.Theme.IsHidden(StyleElement.DeviationCrosshair)
                || Canvas.ActualWidth < 300 || Canvas.ActualHeight < 220)
            {
                if (crosshair != null) crosshair.Visibility = Visibility.Collapsed;
                return;
            }
            if (crosshair == null)
            {
                crosshair = new DeviationCrosshair();
                Panel.SetZIndex(crosshair, 13);
                Canvas.Children.Add(crosshair);
            }
            crosshair.Stroke = Options.Theme.Brush(StyleElement.DeviationCrosshair);
            crosshair.Thickness = Options.Theme.Width(StyleElement.DeviationCrosshair);
            crosshair.DotBrush = Options.Theme.Brush(StyleElement.TrackInside);
            crosshair.TextBrush = Options.Theme.Brush(StyleElement.LabelText);
            crosshair.FontSize = Options.RangeMarks.TextSize;
            crosshair.Metres = Options.ScaleInMetres;
            // No aircraft: the empty cross (the option is on), no dot and no callsign.
            crosshair.VerticalFt = aircraft == null ? double.NaN : aircraft.Altitude - Runway.Elevation - Runway.GlidePathHeight(aircraft.DistanceFromTouchdown(Runway));
            crosshair.LateralFt = aircraft == null ? double.NaN : aircraft.LateralOffset(Runway) * Runway.FeetPerNM;
            // Primary video: no identity.
            crosshair.Title = Options.PrimaryVideo || aircraft == null ? "" : aircraft.Callsign;
            double fromEdge = Math.Max(LeftMargin, inset);
            Canvas.SetLeft(crosshair, RunwayOnRight ? Canvas.ActualWidth - crosshair.Width - fromEdge : fromEdge);
            Canvas.SetTop(crosshair, 4);
            crosshair.Visibility = Visibility.Visible;
            crosshair.InvalidateVisual();
        }

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
            // Angular scale: the upper scan limit at the top of the view (its own angle, not the reference one).
            PixelsPerDegree = Math.Max(1, H - 12) / Math.Max(1, Radar.ScanUp);
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
            // Angular scale: the elevation angle seen from the antenna.
            if (Angular) return H - AngleFromAntenna(fromRunwayEndNM, heightFt / Runway.FeetPerNM) * PixelsPerDegree;
            return H - heightFt * ShrinkAt(fromRunwayEndNM) * yscale;
        }

        /// <summary>
        /// Logical y of an elevation scan limit (angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            if (Angular) return H - angle * PixelsPerDegree;
            return YEnd(fromRunwayEndNM, Radar.ScanHeight(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
        }

        /// <summary>
        /// Line from the antenna along an elevation angle to the given distance from the far end of the runway:
        /// straight with the linear scale, slightly curved with the compressed one.
        /// </summary>
        private void AddScanLine(double angle, double toFromRunwayEndNM, StyleElement element)
        {
            // Angular scale: a direction from the antenna is a horizontal line.
            if (Angular)
            {
                AddLine(XAt(AntennaNM), ScanY(AntennaNM, angle), XAt(toFromRunwayEndNM), ScanY(toFromRunwayEndNM, angle), element);
                return;
            }
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
        private void AddGlidePathLine(double angleOffset, StyleElement element, double maxDistance = double.MaxValue)
        {
            (double split, LineDash dash) = FinalSegmentOf(element);
            double end = Math.Min(Runway.TouchdownNM + Runway.Distance, maxDistance);
            split = Math.Min(split, end);
            // Straight with both distance scales (a line from the touchdown point, see RadarView.ShrinkAt); a curve
            // with the angular scale.
            if (split > 0) AddGlideSegment(0, split, angleOffset, element, dash);
            AddGlideSegment(split, end, angleOffset, element);
        }

        /// <summary>
        /// Part of a line from the touchdown point (glide path angle + <paramref name="angleOffset"/>) between
        /// <paramref name="a"/> and <paramref name="b"/> NM from the touchdown: straight, or a curve with the angular scale.
        /// </summary>
        private void AddGlideSegment(double a, double b, double angleOffset, StyleElement element, LineDash? dash = null)
        {
            if (b <= a) return;
            if (Angular)
            {
                double fromEnd = Runway.LengthNM - Runway.TouchdownNM;
                AddCurve(t =>
                {
                    double s = SampleFromEnd(fromEnd + a, fromEnd + b, t) - fromEnd;
                    return new Point(X(s), Y(s, Runway.GlidePathHeight(s, angleOffset)));
                }, element, dash: dash, segments: AngularCurveSegments);
                return;
            }
            AddLine(X(a), Y(a, Runway.GlidePathHeight(a, angleOffset)), X(b), Y(b, Runway.GlidePathHeight(b, angleOffset)), element, dash: dash);
        }

        /// <summary>
        /// Safety limit line (glide path angle + <paramref name="angleOffset"/>, from the touchdown point), only the part
        /// inside the scan limits (angles from the antenna <paramref name="scanUp"/>, <paramref name="scanDown"/>).
        /// </summary>
        private void AddSafetyLine(double angleOffset, double scanUp, double scanDown)
        {
            double end = Runway.TouchdownNM + Runway.Distance;
            double fromEnd = Runway.LengthNM - Runway.TouchdownNM;
            bool Inside(double s)
            {
                double fromAntenna = fromEnd + s - AntennaNM;
                if (fromAntenna <= 0) return false;
                double height = Runway.GlidePathHeight(s, angleOffset);
                return height >= 0 && height <= Radar.ScanHeight(fromAntenna, scanUp) && height >= Radar.ScanHeight(fromAntenna, scanDown);
            }
            if (VisiblePart(0, end, Inside) is not { } part) return;
            double a = part.From, b = part.To;
            (double split, LineDash dash) = FinalSegmentOf(StyleElement.SafetyLimits);
            if (split > a) AddGlideSegment(a, Math.Min(split, b), angleOffset, StyleElement.SafetyLimits, dash);
            double start = Math.Max(a, split);
            if (start < b) AddGlideSegment(start, b, angleOffset, StyleElement.SafetyLimits);
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
            // Angular scale: no altitude lines (a scale in degrees on the side, and the fixed height lines).
            // Terrain first: everything else (altitude lines, scan limits, glide path...) is drawn over it.
            DrawTerrain();
            if (!Angular && (Options.ShowAltitudeLines || (Compressed && Options.ShowAltitudeScale))) DrawAltitudeLines(end, upper, lower);
            DrawFixedHeights(end, upper, lower);
            // Ground beyond the threshold.
            AddLine(XAt(length), H, XAt(end), H, StyleElement.Ground);
            // Runway.
            AddLine(0, H, XAt(length), H, StyleElement.Runway);
            // Threshold: a tick like the DH (option), a line up to the scan limit, or a symbol on the runway.
            if (Options.ThresholdTick)
            {
                double tick = Options.DhDropLength > 0 ? Options.DhDropLength : Runway.MDH;
                AddLine(XAt(length), H, XAt(length), YEnd(length, tick), StyleElement.DecisionHeightDrop);
            }
            else if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
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
            if (Options.LineEndCircles)
            {
                double gpEnd = Runway.TouchdownNM + Runway.Distance;
                double s = BackFromEnd(X, gpEnd, EndCircleSize / 2 + 1);
                AddEndCircle(X(s), Y(s, Runway.GlidePathHeight(s, 0)), StyleElement.GlidePath);
            }
            // The approach limits may end before the end of the range (as on the PAR-E, at 10 km).
            double limitsTo = Options.ApproachLimitsTo > 0 ? Options.ApproachLimitsTo : double.MaxValue;
            AddGlidePathLine(-Radar.ApproachBelow, StyleElement.ApproachLimits, limitsTo);
            if (Radar.UpperApproachLimit) AddGlidePathLine(Radar.ApproachAbove, StyleElement.ApproachLimits, limitsTo);
            if (Radar.SafetyLimitsElevation)
            {
                AddSafetyLine(Radar.SafetyAbove, upper, lower);
                AddSafetyLine(-Radar.SafetyBelow, upper, lower);
            }
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
                    // Constant height: straight with the linear scale, a slight curve with the compressed one, a curve
                    // going down with the distance with the angular one.
                    if (Angular)
                    {
                        double fromEnd = Runway.LengthNM - Runway.TouchdownNM;
                        AddCurve(t => { double d = SampleFromEnd(fromEnd + from, fromEnd + to, t) - fromEnd; return new Point(X(d), Y(d, Runway.MDH)); }, StyleElement.DecisionHeight, segments: AngularCurveSegments);
                    }
                    else if (Compressed) AddCurve(t => { double d = from + (to - from) * t; return new Point(X(d), Y(d, Runway.MDH)); }, StyleElement.DecisionHeight);
                    else AddLine(X(from), Y(Runway.MDH), X(to), Y(Runway.MDH), StyleElement.DecisionHeight);
                }
                // Vertical line: down, up or both; by default as long as the DH (down to the ground).
                double drop = Options.DhDropLength > 0 ? Options.DhDropLength : Runway.MDH;
                double down = Options.DhDropSide is DhVerticalSide.Down or DhVerticalSide.Both ? Math.Min(drop, Runway.MDH) : 0;
                double up = Options.DhDropSide is DhVerticalSide.Up or DhVerticalSide.Both ? drop : 0;
                // Full height: from the ground to the top of the view, as a distance mark.
                if (Options.DhDropSide == DhVerticalSide.FullHeight) AddLine(X(intercept), H, X(intercept), 0, StyleElement.DecisionHeightDrop);
                else if (Options.DhDropSide != DhVerticalSide.None) AddLine(X(intercept), Y(intercept, Runway.MDH - down), X(intercept), Y(intercept, Runway.MDH + up), StyleElement.DecisionHeightDrop);
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
            List<(double Distance, StyleElement Element, bool Text, bool Short)> marks = [.. Options.RangeMarks.Marks(range, Options.DistanceUnit)];
            // Compressed scale: the far marks are so close that their texts would touch: from the far end towards the
            // runway, a text that would touch the one kept is left out (the line stays).
            HashSet<double> shownTexts = [];
            List<(double Left, double Right)> kept = [];
            foreach ((double distance, StyleElement element, bool text, bool _) in Enumerable.Reverse(marks))
            {
                if (!text) continue;
                if (!Compressed) { shownTexts.Add(distance); continue; }
                TextBlock rangeMeasure = new() { Text = Options.RangeMarks.Label(distance, range, Options.DistanceUnit), FontSize = Options.RangeMarks.TextSize };
                rangeMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textLeft = ToScreenX(XAt(length - Runway.TouchdownNM + distance)) - 10;
                double textRight = textLeft + rangeMeasure.DesiredSize.Width;
                if (!kept.Any(k => textRight + 4 > k.Left && textLeft - 4 < k.Right))
                {
                    shownTexts.Add(distance);
                    kept.Add((textLeft, textRight));
                }
            }
            foreach ((double distance, StyleElement element, bool text, bool isShort) in marks)
            {
                double markNM = length - Runway.TouchdownNM + distance;
                double top = ScanY(markNM, upper);
                double bottom = lower > 0 ? ScanY(markNM, lower) : H;
                if (isShort && Options.RangeMarks.DrawLines && top < bottom && !HasReminderLine(reminders, distance))
                {
                    // Short line (beyond the full lines), centred on the glide path, shorter towards the end.
                    double centre = Y(distance, Runway.GlidePathHeight(distance, 0));
                    double half = (bottom - top) * ShortMarkFraction(distance) / 2;
                    double y1 = Math.Max(top, centre - half), y2 = Math.Min(bottom, centre + half);
                    if (y1 < y2) AddLine(XAt(markNM), y2, XAt(markNM), y1, element);
                    // The lines below are only for the full marks; ticks and text as usual.
                    top = bottom;
                }
                // A reminder line at the same distance replaces the range mark (the text stays).
                // Ticks on the distance axis (the horizon line): the marks with text are longer.
                double tickUp = Options.RangeMarks.TickAbove(text), tickDown = Options.RangeMarks.TickBelow(text);
                if (tickUp + tickDown > 0 && !HasReminderLine(reminders, distance))
                {
                    if (Options.RangeMarks.TicksOnHorizon) AddLine(XAt(markNM), H + tickDown, XAt(markNM), H - tickUp, element);
                    // On the glide path (as on the PAR 2000): vertical, centred on the line, in its colour.
                    if (Options.RangeMarks.TicksOnGlidePath && distance > 0)
                    {
                        double gpY = Y(distance, Runway.GlidePathHeight(distance, 0));
                        AddLine(XAt(markNM), gpY + tickDown, XAt(markNM), gpY - tickUp, StyleElement.GlidePath, dash: LineDash.Solid);
                    }
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
                if (text && shownTexts.Contains(distance))
                {
                    double axisDown = Options.RangeMarks.TicksOnHorizon ? tickDown : 0, axisUp = Options.RangeMarks.TicksOnHorizon ? tickUp : 0;
                    AddText(Options.RangeMarks.Label(distance, range, Options.DistanceUnit), XAt(markNM), textBelow ? H + 2 + axisDown : H - 1 - axisUp, -10, RangeTextBrush(element), aboveAnchor: !textBelow);
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
            if (Options.ShowAltitudeScale && Angular)
            {
                // From the horizon up (below it is the band of the distance texts).
                DrawAngleScale(H, Math.Max(0, Math.Ceiling(Radar.ScanDown)), Radar.ScanUp, signed: true);
            }
            else if (Options.ShowAltitudeScale && !Compressed)
            {
                DrawAltitudeScale();
            }
        }

        /// <summary>
        /// Fixed height lines (option, e.g. FH600M on the PAR-E): from where the height comes into the scan (upper scan
        /// limit) to the end of the range; straight with the linear heights, a curve going down with the distance with
        /// the compressed or the angular scale. The name is written where the line starts.
        /// </summary>
        private void DrawFixedHeights(double end, double upper, double lower)
        {
            // The fixed heights of the profile (FH) and the final approach altitude of the runway (FAP, height above the threshold).
            List<(double HeightFt, string Name)> lines = [.. Options.FixedHeights.Select(h => (h, "FH"))];
            if (Options.ShowFapLine && Runway.FapAltitude is double fap && fap > Runway.Elevation) lines.Add((fap - Runway.Elevation, "FAP"));
            if (lines.Count == 0 || Options.Theme.IsHidden(StyleElement.FixedHeight)) return;
            double tanUpper = Math.Tan(upper * Math.PI / 180);
            if (tanUpper <= 0) return;
            foreach ((double heightFt, string name) in lines)
            {
                double heightNM = heightFt / Runway.FeetPerNM;
                double from = AntennaNM + heightNM / tanUpper;
                // Out of the scan below the lower limit when it is above the horizon (as the altitude lines).
                double to = lower > 0 ? Math.Min(end, AntennaNM + heightNM / Math.Tan(lower * Math.PI / 180)) : end;
                if (from >= to) continue;
                if (!Compressed && !Angular) AddLine(XAt(from), Y(heightFt), XAt(to), Y(heightFt), StyleElement.FixedHeight);
                else AddCurve(t => { double r = SampleFromEnd(from, to, t); return new Point(XAt(r), YEnd(r, heightFt)); }, StyleElement.FixedHeight, segments: AngularCurveSegments);
                // FH: the height set; FAP: as the other altitudes, a height with QFE and an altitude with QNH.
                double shownFt = name == "FAP" && !Options.Qfe ? heightFt + Runway.Elevation : heightFt;
                double value = Options.ScaleInMetres ? shownFt * 0.3048 : shownFt;
                string text = name + Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + (Options.ScaleInMetres ? "M" : "FT");
                double y = YEnd(from, heightFt);
                double width = MeasureWidth(new TextBlock { Text = text, FontSize = Options.RangeMarks.TextSize });
                // Towards the far end, above the line (below it when there is no room at the top of the view).
                double dx = RunwayOnRight ? -4 - width : 4;
                // Inside the view (a line starting above the top or near the far edge).
                y = Math.Clamp(y, 2, H - 2);
                double left = ToScreenX(XAt(from)) + dx;
                dx += Math.Clamp(left, 2, Math.Max(2, Canvas.ActualWidth - width - 2)) - left;
                bool above = y - 2 - Options.RangeMarks.TextSize * 1.4 > 0;
                AddText(text, XAt(from), above ? y - 2 : y + 2, dx, Brush(StyleElement.FixedHeight), aboveAnchor: above);
            }
        }

        /// <summary>
        /// Terrain / obstacle profile of the runway (terrain.par): each step filled from the ground up to its altitude, or
        /// as a stepped line with the altitude written on it (QNH: altitude, QFE: height). Distances from the threshold;
        /// cut at the end of the range. Straight with the linear heights, curves with the compressed or angular scale.
        /// </summary>
        private void DrawTerrain()
        {
            if (Options.TerrainDisplay == TerrainDisplay.Off || Runway.Terrain.Count == 0 || Options.Theme.IsHidden(StyleElement.Terrain)) return;
            double threshold = Runway.LengthNM;
            double maxFromEnd = Runway.LengthNM + Runway.Distance;
            int segments = Compressed || Angular ? AngularCurveSegments / 4 : 1;
            List<(double A, double B, double Height, double Altitude)> steps = [];
            foreach (TerrainStep step in Runway.Terrain)
            {
                double a = threshold + step.FromNM;
                double b = Math.Min(threshold + step.ToNM, maxFromEnd);
                double height = step.AltitudeFt - Runway.Elevation;
                if (b > a && height > 0) steps.Add((a, b, height, step.AltitudeFt));
            }
            if (steps.Count == 0) return;
            IEnumerable<Point> Edge(double a, double b, double height, bool forward) =>
                Enumerable.Range(0, segments + 1).Select(i =>
                {
                    double t = forward ? (double)i / segments : 1 - (double)i / segments;
                    double r = SampleFromEnd(a, b, t);
                    return new Point(XAt(r), YEnd(r, height));
                });
            if (Options.TerrainDisplay == TerrainDisplay.Filled)
            {
                foreach ((double a, double b, double height, _) in steps)
                {
                    AddPolygon(Edge(a, b, height, true).Concat(Edge(a, b, 0, false)), StyleElement.Terrain, 0.7);
                }
                return;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                (double a, double b, double height, double altitude) = steps[i];
                AddCurve(t => { double r = SampleFromEnd(a, b, t); return new Point(XAt(r), YEnd(r, height)); }, StyleElement.Terrain, segments: Math.Max(1, segments));
                // Riser at the start: from the step before when they touch, else from the ground; at the end down to the ground when nothing follows.
                bool joined = i > 0 && Math.Abs(steps[i - 1].B - a) < 1e-6;
                double from = joined ? steps[i - 1].Height : 0;
                AddLine(XAt(a), YEnd(a, from), XAt(a), YEnd(a, height), StyleElement.Terrain);
                bool followed = i + 1 < steps.Count && Math.Abs(steps[i + 1].A - b) < 1e-6;
                if (!followed && b < maxFromEnd) AddLine(XAt(b), YEnd(b, height), XAt(b), YEnd(b, 0), StyleElement.Terrain);
                // The value above the middle of the step.
                double shownFt = Options.Qfe ? height : altitude;
                double value = Options.ScaleInMetres ? shownFt * 0.3048 : shownFt;
                string text = Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                double width = MeasureWidth(new TextBlock { Text = text, FontSize = Options.RangeMarks.TextSize });
                double middle = SampleFromEnd(a, b, 0.5);
                double y = YEnd(middle, height);
                if (Math.Abs(XAt(b) - XAt(a)) > width + 4 && y - 2 - Options.RangeMarks.TextSize * 1.4 > 0)
                {
                    AddText(text, XAt(middle), y - 2, -width / 2, Brush(StyleElement.Terrain), aboveAnchor: true);
                }
            }
        }

        private static double MeasureWidth(TextBlock text)
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return text.DesiredSize.Width;
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

        /// <summary>Compressed scale: highest altitude (in the unit of the scale) with its line and value; above, nothing is drawn.</summary>
        private const double MaxCompressedAltitude = 10000;

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
            // Round values: about 60 px apart near the touchdown, thinned where the heights are the most shrunk.
            double pixelsPerFoot = yscale;
            double textStep = steps.FirstOrDefault(s => s * unitToFeet * pixelsPerFoot >= 60, steps[^1]);
            // Values are written from the lowest up; one that would overlap an earlier one is left out.
            List<Rect> placed = [];
            bool lines = Options.ShowAltitudeLines;
            bool texts = Options.ShowAltitudeScale && !Options.Theme.IsHidden(StyleElement.AltitudeScale);
            double step = lines && texts ? Math.Min(1000, textStep) : lines ? 1000 : textStep;
            for (double value = (Math.Floor(baseValue / step) + 1) * step; value <= Math.Min(topValue, MaxCompressedAltitude); value += step)
            {
                double heightFt = (value - baseValue) * unitToFeet;
                if (heightFt <= 0) continue;
                double heightNM = heightFt / Runway.FeetPerNM;
                double from = AntennaNM + heightNM / tanUpper;
                double to = end;
                if (lower > 0) to = Math.Min(to, AntennaNM + heightNM / tanLower);
                if (from >= to)
                {
                    // Above the scan limit at the end of the range: no line, but the value is written at the far edge.
                    if (texts && IsMultiple(value, textStep)) DrawEdgeAltitude(value, unit, heightFt, end, placed);
                    continue;
                }
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
                            Rect box = new(ToScreenX(XAt(at)) + dx, y - 2 - measure.DesiredSize.Height, width, measure.DesiredSize.Height);
                            if (box.Top > 0 && TryPlace(placed, box)) AddText(text, XAt(at), y - 2, dx, Brush(StyleElement.AltitudeScale), aboveAnchor: true);
                        }
                        else
                        {
                            // The line comes into the view from the top: under it, towards the far end.
                            double dx = RunwayOnRight ? -4 - width : 4;
                            Rect box = new(ToScreenX(XAt(at)) + dx, y + 2, width, measure.DesiredSize.Height);
                            if (TryPlace(placed, box)) AddText(text, XAt(at), y + 2, dx, Brush(StyleElement.AltitudeScale), aboveAnchor: false);
                        }
                    }
                }
            }
        }

        /// <summary>Value of an altitude (compressed scale) written inside the far edge of the view, at the height of that altitude.</summary>
        private void DrawEdgeAltitude(double value, string unit, double heightFt, double end, List<Rect> placed)
        {
            double y = YEnd(end, heightFt);
            if (y <= 14) return;
            string text = $"{value:0} {unit}";
            TextBlock measure = new() { Text = text, FontSize = Options.RangeMarks.TextSize };
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = measure.DesiredSize.Width;
            // The far end is on the side away from the runway: the text is inside the view, next to the edge.
            double dx = RunwayOnRight ? 4 : -4 - width;
            double left = ToScreenX(XAt(end)) + dx;
            dx += Math.Clamp(left, 2, Math.Max(2, Canvas.ActualWidth - width - 2)) - left;
            Rect box = new(ToScreenX(XAt(end)) + dx, y - 2 - measure.DesiredSize.Height, width, measure.DesiredSize.Height);
            if (box.Top > 0 && TryPlace(placed, box)) AddText(text, XAt(end), y - 2, dx, Brush(StyleElement.AltitudeScale), aboveAnchor: true);
        }

        /// <summary>True (and the box is kept) when the box does not overlap any of the earlier ones.</summary>
        private static bool TryPlace(List<Rect> placed, Rect box)
        {
            Rect padded = new(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2);
            if (placed.Any(r => r.IntersectsWith(padded))) return false;
            placed.Add(box);
            return true;
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
