namespace AuroraPAR
{
    /// <summary>
    /// Radar equipment geometry: approach limits, scan limits, antenna beam and antenna tilt.
    /// Limits and beam come from the active profile (they depend on the radar type, not on the runway);
    /// the tilt is the current antenna position, changed during the session and never saved.
    ///
    /// The scan limits are the physical limits of the antenna (fixed). The antenna beam is narrower and is moved
    /// by the tilt inside them: only what is inside the beam is seen. The tilt is an offset from the neutral
    /// position of the beam, and stops where the beam reaches a scan limit (no tilt at all when the beam is as
    /// wide as the scan limits).
    ///
    /// Angles are in degrees. Lateral angles are signed: positive to the right of the approach course,
    /// as seen by the pilot flying the approach (same sign as <see cref="Aircraft.LateralOffset"/>).
    /// The antenna is on the runway centreline, at half the runway length.
    /// </summary>
    internal class Radar
    {
        /// <summary>Approach limits, angles from the ideal glide path (above/below) and centreline (left/right) at the touchdown point.</summary>
        public double ApproachAbove { get; set; } = 0.5;
        /// <summary>Safety limits (see <see cref="Profile.SafetyLimitsElevation"/>): shown per view, angles from the touchdown.</summary>
        public bool SafetyLimitsElevation { get; set; }
        public bool SafetyLimitsAzimuth { get; set; }
        public double SafetyAbove { get; set; } = 2;
        public double SafetyBelow { get; set; } = 2;
        public double SafetyLeft { get; set; } = 5;
        public double SafetyRight { get; set; } = 5;
        /// <summary>False: no upper approach limit (see <see cref="Profile.UpperApproachLimit"/>).</summary>
        public bool UpperApproachLimit { get; set; } = true;
        public double ApproachBelow { get; set; } = 0.5;
        public double ApproachLeft { get; set; } = 1.5;
        public double ApproachRight { get; set; } = 1.5;

        /// <summary>Scan limits: physical limits of the antenna, angles from the antenna (fixed, not tilted).</summary>
        public double ScanUp { get; set; } = 18;
        public double ScanDown { get; set; } = -11;
        public double ScanLeft { get; set; } = 20;
        public double ScanRight { get; set; } = 20;

        /// <summary>Width of the antenna beam (degrees) and its centre in neutral position (EL up, AZ right).</summary>
        public double BeamElevation { get; set; } = 9;
        public double BeamAzimuth { get; set; } = 20;
        public double BeamElevationNeutral { get; set; } = 3.5;
        public double BeamAzimuthNeutral { get; set; }
        /// <summary>Narrow beam moved by the tilt; off: the beam is the scan limits (no tilt, no thicker marks).</summary>
        public bool BeamEnabled { get; set; } = true;
        /// <summary>Elevation centre in neutral = <see cref="GlidePathAngle"/> (the approach in use).</summary>
        public bool NeutralFollowsGlidePath { get; set; }
        /// <summary>Glide path angle of the approach in use (set by the window).</summary>
        public double GlidePathAngle { get; set; } = 3;

        /// <summary>Beam actually used: the one of the profile, or the scan limits when the beam is off.</summary>
        private double BeamWidthElevation => BeamEnabled ? BeamElevation : ScanUp - ScanDown;
        private double BeamWidthAzimuth => BeamEnabled ? BeamAzimuth : ScanLeft + ScanRight;
        private double BeamNeutralElevation => !BeamEnabled ? (ScanUp + ScanDown) / 2
            : NeutralFollowsGlidePath ? GlidePathAngle : BeamElevationNeutral;
        private double BeamNeutralAzimuth => BeamEnabled ? BeamAzimuthNeutral : (ScanRight - ScanLeft) / 2;

        /// <summary>
        /// Fixed angles used for the scale of the views (the default scan limits), so the scale depends only on the
        /// range: changing the scan limits or tilting the antenna moves the lines without resizing everything else.
        /// </summary>
        public const double ReferenceScanUp = 8;
        public const double ReferenceScanHalfWidth = 10;

        public double TiltStep { get; set; } = 2;

        /// <summary>Current elevation tilt (from the neutral position of the beam), positive up.</summary>
        public double TiltElevation { get; private set; }
        /// <summary>Current azimuth tilt (from the neutral position of the beam), positive to the right.</summary>
        public double TiltAzimuth { get; private set; }

        public bool IsNeutral => TiltElevation == 0 && TiltAzimuth == 0;

        /// <summary>
        /// Positions of the beam centre that keep the whole beam inside the scan limits; a beam wider than the scan
        /// limits stays in the middle.
        /// </summary>
        private static (double Min, double Max) CentreRange(double lower, double upper, double width)
        {
            double min = lower + width / 2;
            double max = upper - width / 2;
            if (min > max)
            {
                double middle = (lower + upper) / 2;
                return (middle, middle);
            }
            return (min, max);
        }

        private (double Min, double Max) ElevationCentreRange => CentreRange(ScanDown, ScanUp, BeamWidthElevation);
        private (double Min, double Max) AzimuthCentreRange => CentreRange(-ScanLeft, ScanRight, BeamWidthAzimuth);

        /// <summary>Beam centre in neutral position (the one of the profile, brought inside the possible range).</summary>
        private double ElevationNeutralCentre => Math.Clamp(BeamNeutralElevation, ElevationCentreRange.Min, ElevationCentreRange.Max);
        private double AzimuthNeutralCentre => Math.Clamp(BeamNeutralAzimuth, AzimuthCentreRange.Min, AzimuthCentreRange.Max);

        /// <summary>Tilt range from neutral (minimum ≤ 0 ≤ maximum).</summary>
        public double TiltElevationMin => ElevationCentreRange.Min - ElevationNeutralCentre;
        public double TiltElevationMax => ElevationCentreRange.Max - ElevationNeutralCentre;
        public double TiltAzimuthMin => AzimuthCentreRange.Min - AzimuthNeutralCentre;
        public double TiltAzimuthMax => AzimuthCentreRange.Max - AzimuthNeutralCentre;

        private double ElevationCentre => ElevationNeutralCentre + TiltElevation;
        private double AzimuthCentre => AzimuthNeutralCentre + TiltAzimuth;

        /// <summary>Elevation edges of the antenna beam, tilt included (inside the scan limits).</summary>
        public double ElevationLower => Math.Max(ScanDown, ElevationCentre - BeamWidthElevation / 2);
        public double ElevationUpper => Math.Min(ScanUp, ElevationCentre + BeamWidthElevation / 2);
        /// <summary>Azimuth edges of the antenna beam, tilt included (signed, positive right; inside the scan limits).</summary>
        public double AzimuthLeftEdge => Math.Max(-ScanLeft, AzimuthCentre - BeamWidthAzimuth / 2);
        public double AzimuthRightEdge => Math.Min(ScanRight, AzimuthCentre + BeamWidthAzimuth / 2);

        /// <summary>Azimuth scan limits as signed angles (positive right).</summary>
        public double ScanLeftEdge => -ScanLeft;
        public double ScanRightEdge => ScanRight;

        /// <summary>
        /// Copies the limits and the beam from a profile. The tilt is kept, inside its new range.
        /// </summary>
        public void ApplyProfile(Profile profile)
        {
            profile.MigrateScanModel();
            ApproachAbove = profile.ApproachAbove;
            UpperApproachLimit = profile.UpperApproachLimit;
            SafetyLimitsElevation = profile.SafetyLimitsElevation;
            SafetyLimitsAzimuth = profile.SafetyLimitsAzimuth;
            SafetyAbove = profile.SafetyAbove;
            SafetyBelow = profile.SafetyBelow;
            SafetyLeft = profile.SafetyLeft;
            SafetyRight = profile.SafetyRight;
            ApproachBelow = profile.ApproachBelow;
            ApproachLeft = profile.ApproachLeft;
            ApproachRight = profile.ApproachRight;
            ScanUp = profile.ScanUp;
            ScanDown = profile.ScanDown;
            ScanLeft = profile.ScanLeft;
            ScanRight = profile.ScanRight;
            BeamElevation = profile.BeamElevation;
            BeamAzimuth = profile.BeamAzimuth;
            BeamElevationNeutral = profile.BeamElevationNeutral;
            BeamAzimuthNeutral = profile.BeamAzimuthNeutral;
            BeamEnabled = profile.BeamEnabled;
            NeutralFollowsGlidePath = profile.BeamElevationNeutralAuto;
            TiltStep = profile.TiltStep;
            ClampTilt();
        }

        /// <summary>Keeps the tilt inside its range (after a change of the beam, the limits or the glide path).</summary>
        public void ClampTilt()
        {
            TiltElevation = Math.Clamp(TiltElevation, TiltElevationMin, TiltElevationMax);
            TiltAzimuth = Math.Clamp(TiltAzimuth, TiltAzimuthMin, TiltAzimuthMax);
        }

        /// <summary>
        /// Moves the antenna by the given number of steps (positive up / right). Returns true if it moved.
        /// </summary>
        public bool Tilt(int elevationSteps, int azimuthSteps)
        {
            double elevation = StepTilt(TiltElevation, elevationSteps, TiltElevationMin, TiltElevationMax, ElevationStepsDown, ElevationStepsUp);
            double azimuth = StepTilt(TiltAzimuth, azimuthSteps, TiltAzimuthMin, TiltAzimuthMax, AzimuthStepsLeft, AzimuthStepsRight);
            bool moved = elevation != TiltElevation || azimuth != TiltAzimuth;
            TiltElevation = elevation;
            TiltAzimuth = azimuth;
            return moved;
        }

        /// <summary>
        /// Tilt after some steps, always on the grid of the steps from neutral (k × step), the ends of the range
        /// included (the last step to an end may be shorter): coming back from an end returns to the grid.
        /// </summary>
        private double StepTilt(double current, int steps, double min, double max, int down, int up)
        {
            if (steps == 0 || TiltStep <= 0) return current;
            int k = current >= max - 0.001 ? up
                : current <= min + 0.001 ? -down
                : (int)Math.Round(current / TiltStep, MidpointRounding.AwayFromZero);
            k = Math.Clamp(k + steps, -down, up);
            if (k == up) return max;
            if (k == -down) return min;
            return Math.Round(k * TiltStep, 3);
        }

        /// <summary>
        /// Back to the neutral position. Returns true if it moved.
        /// </summary>
        public bool Neutral()
        {
            bool moved = !IsNeutral;
            TiltElevation = 0;
            TiltAzimuth = 0;
            return moved;
        }

        /// <summary>
        /// Elevation (or azimuth) only back to neutral. Returns true if it moved.
        /// </summary>
        public bool NeutralElevation()
        {
            bool moved = TiltElevation != 0;
            TiltElevation = 0;
            return moved;
        }

        public bool NeutralAzimuth()
        {
            bool moved = TiltAzimuth != 0;
            TiltAzimuth = 0;
            return moved;
        }

        /// <summary>Number of tilt steps from neutral to the end of a tilt range (the last one may be shorter).</summary>
        private int Steps(double span) => TiltStep > 0 && span > 0 ? (int)Math.Ceiling(Math.Round(span / TiltStep, 3)) : 0;

        public int ElevationStepsDown => Steps(-TiltElevationMin);
        public int ElevationStepsUp => Steps(TiltElevationMax);
        public int AzimuthStepsLeft => Steps(-TiltAzimuthMin);
        public int AzimuthStepsRight => Steps(TiltAzimuthMax);

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        public static double AntennaFromRunwayEnd(Runway runway) => runway.LengthNM / 2;

        private static double Tan(double degrees) => Math.Tan(degrees * Math.PI / 180);

        /// <summary>
        /// Height in ft above the threshold of an elevation scan limit (angle from the antenna) at the given
        /// distance from the antenna in NM.
        /// </summary>
        public static double ScanHeight(double distanceFromAntennaNM, double angle)
        {
            return distanceFromAntennaNM * Tan(angle) * Runway.FeetPerNM;
        }

        /// <summary>
        /// Lateral position in NM (positive right) of an azimuth scan limit at the given distance from the antenna.
        /// </summary>
        public static double ScanOffset(double distanceFromAntennaNM, double angle)
        {
            return distanceFromAntennaNM * Tan(angle);
        }

        /// <summary>
        /// Half width in NM of the approach limit on one side of the centreline, at the given distance from touchdown.
        /// </summary>
        public double ApproachHalfWidth(double distanceFromTouchdownNM, bool right)
        {
            return distanceFromTouchdownNM * Tan(right ? ApproachRight : ApproachLeft);
        }

        /// <summary>
        /// True when the aircraft is seen in a view. Each view has its own antenna, as on a real PAR: the elevation
        /// view needs the aircraft inside the elevation beam (and inside the azimuth scan limits), the azimuth view
        /// inside the azimuth beam (and inside the elevation scan limits). So an aircraft leaving one beam disappears
        /// only from that view. Always also in front of the antenna, within the displayed range and above the ground.
        /// </summary>
        /// <param name="elevationView">True for the elevation view, false for the azimuth view.</param>
        public bool IsInsideBeam(Aircraft aircraft, Runway runway, bool elevationView)
        {
            if (!Angles(aircraft, runway, out double azimuth, out double elevation)) return false;
            if (aircraft.Altitude - runway.Elevation <= 0) return false;
            return elevationView
                ? elevation >= ElevationLower && elevation <= ElevationUpper && azimuth >= ScanLeftEdge && azimuth <= ScanRightEdge
                : azimuth >= AzimuthLeftEdge && azimuth <= AzimuthRightEdge && elevation >= ScanDown && elevation <= ScanUp;
        }

        /// <summary>True when the aircraft is seen in at least one view (see <see cref="IsInsideBeam"/>).</summary>
        public bool IsSeen(Aircraft aircraft, Runway runway) => IsInsideBeam(aircraft, runway, true) || IsInsideBeam(aircraft, runway, false);

        /// <summary>
        /// Azimuth and elevation of the aircraft from the antenna (degrees); false when it is behind the antenna or
        /// beyond the displayed range.
        /// </summary>
        private static bool Angles(Aircraft aircraft, Runway runway, out double azimuth, out double elevation)
        {
            azimuth = elevation = 0;
            double fromRunwayEnd = runway.LengthNM + aircraft.AlongTrackDistance(runway);
            if (fromRunwayEnd > runway.LengthNM + runway.Distance) return false;
            double fromAntenna = fromRunwayEnd - AntennaFromRunwayEnd(runway);
            if (fromAntenna <= 0) return false;
            azimuth = Math.Atan2(aircraft.LateralOffset(runway), fromAntenna) * 180 / Math.PI;
            double height = aircraft.Altitude - runway.Elevation;
            elevation = Math.Atan2(height / Runway.FeetPerNM, fromAntenna) * 180 / Math.PI;
            return true;
        }

        /// <summary>
        /// Angle (degrees) between the aircraft and the nearest edge of the beam of a view (positive inside): the
        /// elevation beam (lower edge only above the ground) or the azimuth beam.
        /// </summary>
        public double BeamMargin(Aircraft aircraft, Runway runway, bool elevationView)
        {
            if (!Angles(aircraft, runway, out double azimuth, out double elevation)) return 0;
            if (!elevationView) return Math.Min(azimuth - AzimuthLeftEdge, AzimuthRightEdge - azimuth);
            double margin = ElevationUpper - elevation;
            if (ElevationLower > 0) margin = Math.Min(margin, elevation - ElevationLower);
            return margin;
        }

        /// <summary>
        /// True when the aircraft is inside the vertical approach limits (angles from the touchdown point); without the
        /// upper limit, anywhere above the lower one.
        /// </summary>
        public bool IsWithinGlidePathLimits(Aircraft aircraft, Runway runway)
        {
            double s = aircraft.DistanceFromTouchdown(runway);
            double height = aircraft.Altitude - runway.Elevation;
            return height > runway.GlidePathHeight(s, -ApproachBelow)
                && (!UpperApproachLimit || height < runway.GlidePathHeight(s, ApproachAbove));
        }

        /// <summary>
        /// True when the aircraft is inside the lateral approach limits (angles from the touchdown point).
        /// </summary>
        public bool IsWithinCenterlineLimits(Aircraft aircraft, Runway runway)
        {
            double offset = aircraft.LateralOffset(runway);
            return Math.Abs(offset) <= ApproachHalfWidth(aircraft.DistanceFromTouchdown(runway), right: offset > 0);
        }
    }
}
