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

    /// <summary>Turn rate of an aircraft: for the heading instructions and with the stick at its edge.</summary>
    public enum TurnRateMode
    {
        /// <summary>Half rate: 1.5°/s.</summary>
        Half,
        /// <summary>Rate one: 3°/s.</summary>
        Standard,
        /// <summary>Heading instructions at 3°/s; with the stick held at its edge the turn grows up to 10°/s.</summary>
        Free
    }

    /// <summary>What the aircraft does vertically.</summary>
    public enum VerticalMode
    {
        /// <summary>Climbs or descends at the selected vertical rate to the commanded altitude, then levels off.</summary>
        Altitude,
        /// <summary>Keeps the commanded vertical speed until told otherwise (the "rate" of the PAR controller).</summary>
        Rate,
        /// <summary>The vertical speed that keeps the glide path at its ground speed ("Normal" rate), also when the speed changes.</summary>
        GlidePath
    }

    /// <summary>How the aircraft is navigated sideways.</summary>
    public enum NavigationMode
    {
        /// <summary>MANUAL: headings and the stick (a route, if any, is kept but not flown).</summary>
        Manual,
        /// <summary>ROUTE chosen, no point given yet: it keeps its heading until a point is given (direct to, next point).</summary>
        RouteArmed,
        /// <summary>ROUTE: it flies the points of its route.</summary>
        Route
    }

    /// <summary>Whether the aircraft is still flying.</summary>
    public enum GroundContact
    {
        Airborne,
        /// <summary>Touched down on the runway: it rolls on for a few seconds, then it is removed.</summary>
        Landed,
        /// <summary>Reached the ground outside the runway: it stops there until the instructor removes it.</summary>
        OnGround
    }

    /// <summary>
    /// A point of a route: position in the <see cref="LocalPlane"/> (NM east, NM north of the threshold), altitude
    /// (ft above mean sea level) and speed (kt) to have there, and an optional name (a fix of the maps, for the lists).
    /// </summary>
    public sealed record RoutePoint(double X, double Y, double AltitudeFt, double SpeedKt, string Name = "");

    /// <summary>
    /// An aircraft of the simulation at one moment, as given out by <see cref="SimEngine.Snapshot"/> (a copy: it does
    /// not change afterwards). Positions in the <see cref="LocalPlane"/> (NM), altitude above mean sea level (ft),
    /// headings true (degrees, 0–360), speed true airspeed (kt), vertical speeds ft/min (negative descending).
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
        TurnRateMode TurnRateMode,
        VerticalMode VerticalMode,
        double TargetAltitudeFt,
        double SelectedVerticalRateFpm,
        double CommandedVerticalSpeedFpm,
        double BestVerticalSpeedFpm,
        double TargetSpeedKt,
        bool Frozen,
        bool StickActive,
        int PendingCommands,
        string PerformanceClass,
        double GroundSpeedKt,
        double TrackTrue,
        double DriftDegrees,
        GroundContact Contact,
        // The route (null: none; the points already passed included) and the index of the point it flies to next
        // (= the number of points when all are passed).
        IReadOnlyList<RoutePoint>? Route = null,
        int RouteNext = -1,
        // Whether the route still gives the altitude and the speed (until the instructor gives one).
        bool RouteAltitude = false,
        bool RouteSpeed = false,
        NavigationMode Navigation = NavigationMode.Manual);

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
        public TurnRateMode TurnRateMode = TurnRateMode.Standard;
        public VerticalMode VerticalMode = VerticalMode.Altitude;
        public double TargetAltitudeFt;
        public double SelectedVerticalRateFpm = SimEngine.DefaultVerticalRate;
        public double CommandedVerticalSpeedFpm;
        public double TargetSpeedKt;
        /// <summary>Vertical speed that keeps the glide path at this heading and speed (ft/min, negative), 0 without an approach.</summary>
        public double BestVerticalSpeedFpm;
        /// <summary>Held where it is (the instructor explains a situation): it does not move and keeps its commands.</summary>
        public bool Frozen;
        /// <summary>Stick deflection (−1..1, X right, Y up) and for how long X has been at its edge (Free turn rate).</summary>
        public double StickX;
        public double StickY;
        public double StickHeld;
        /// <summary>What the aircraft can do (a copy of its class).</summary>
        public PerformanceClass Class = PerformanceClass.Defaults()[1];
        /// <summary>Over the ground, with the wind: speed (kt), track (degrees true in the plane) and drift (track − heading, −180..180).</summary>
        public double GroundSpeedKt;
        public double TrackTrue;
        public double DriftDegrees;
        /// <summary>Still flying, landed (and since which step) or on the ground outside the runway.</summary>
        public GroundContact Contact = GroundContact.Airborne;
        public long LandedStep;
        /// <summary>
        /// The route (null: none; the points passed included), the point it flies to next, where the leg to that point
        /// starts (for the course of the last leg), whether it has already flown towards that point (a point behind it
        /// is passed only after that), MANUAL / ROUTE, and whether the instructor has given an altitude (a rate) or a
        /// speed: from then on the points' altitudes or speeds are not flown.
        /// </summary>
        public IReadOnlyList<RoutePoint>? Route;
        public int RouteNext;
        public double LegStartX;
        public double LegStartY;
        public bool RouteApproached;
        public NavigationMode Navigation = NavigationMode.Manual;
        public bool AltitudeGiven;
        public bool SpeedGiven;

        public AircraftState ToState(int pendingCommands) => new(Callsign, X, Y, AltitudeFt, HeadingTrue, SpeedKt, VerticalSpeedFpm, TurnRateDps,
            TargetHeading, TurnDirection, SelectedTurnRateDps, TurnRateMode, VerticalMode, TargetAltitudeFt, SelectedVerticalRateFpm,
            CommandedVerticalSpeedFpm, BestVerticalSpeedFpm, TargetSpeedKt, Frozen, StickX != 0 || StickY != 0, pendingCommands,
            Class.Name, GroundSpeedKt, TrackTrue, DriftDegrees, Contact,
            Route, Route != null ? RouteNext : -1, Route != null && !AltitudeGiven, Route != null && !SpeedGiven, Navigation);
    }
}
