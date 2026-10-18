namespace ATCRoom.Sim
{
    /// <summary>Side of a turn to a heading.</summary>
    public enum TurnDirection
    {
        /// <summary>The shorter way.</summary>
        Shortest,
        Left,
        Right
    }

    /// <summary>
    /// An aircraft of the simulation at one moment, as given out by <see cref="SimEngine.Snapshot"/> (a copy: it does
    /// not change afterwards). Positions in the <see cref="LocalPlane"/> (NM), altitude above mean sea level (ft),
    /// headings true (degrees, 0–360), speed true airspeed (kt).
    /// </summary>
    public sealed record AircraftState(
        string Callsign,
        double X,
        double Y,
        double AltitudeFt,
        double HeadingTrue,
        double SpeedKt,
        double VerticalSpeedFpm,
        double TurnRateDps,
        double TargetHeading,
        TurnDirection TurnDirection,
        double SelectedTurnRateDps,
        double TargetAltitudeFt,
        double SelectedVerticalRateFpm,
        double TargetSpeedKt,
        bool Frozen);

    /// <summary>An aircraft inside the engine: actual values and the values commanded by the instructor. Only the engine changes it, under its lock.</summary>
    internal sealed class SimAircraft
    {
        public string Callsign = "";
        // Actual values.
        public double X;
        public double Y;
        public double AltitudeFt;
        public double HeadingTrue;
        public double SpeedKt;
        public double VerticalSpeedFpm;
        public double TurnRateDps;
        // Commanded values.
        public double TargetHeading;
        public TurnDirection TurnDirection = TurnDirection.Shortest;
        public double SelectedTurnRateDps = SimEngine.StandardTurnRate;
        public double TargetAltitudeFt;
        public double SelectedVerticalRateFpm = SimEngine.DefaultVerticalRate;
        public double TargetSpeedKt;
        /// <summary>Held where it is (the instructor explains a situation): it does not move and keeps its commands.</summary>
        public bool Frozen;

        public AircraftState ToState() => new(Callsign, X, Y, AltitudeFt, HeadingTrue, SpeedKt, VerticalSpeedFpm, TurnRateDps,
            TargetHeading, TurnDirection, SelectedTurnRateDps, TargetAltitudeFt, SelectedVerticalRateFpm, TargetSpeedKt, Frozen);
    }
}
