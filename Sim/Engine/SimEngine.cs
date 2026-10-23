namespace ATCRoom.Sim
{
    /// <summary>
    /// The simulation: the aircraft of the instructor, moved in fixed steps of <see cref="Step"/> seconds of its own
    /// clock, in the <see cref="LocalPlane"/>.
    ///
    /// Each aircraft has actual values (heading, altitude or vertical speed, speed) and commanded ones; every step
    /// moves the actual values towards the commanded ones, as an aircraft would:
    /// - Heading: to a commanded heading at the selected turn rate, or turned directly by the stick. The turn rate
    ///   grows and shrinks gradually (<see cref="TurnRateChange"/>: rate one is reached in 2 s, as when banking) and
    ///   the roll-out starts in time to stop on the heading; the bank angle limit of the class caps the rate at high
    ///   speed (rate one needs about 35° of bank at 250 kt).
    /// - Vertically, one of three modes (<see cref="VerticalMode"/>): to an altitude at a selected rate with a smooth
    ///   level-off, a commanded vertical speed (the PAR controller's "rate"), or the vertical speed that keeps the
    ///   glide path ("Normal"); the stick changes the vertical speed directly. The vertical speed itself changes
    ///   gradually (the class's vertical acceleration).
    /// - Speed: at the accelerations of the class, within its speeds.
    /// - Wind: one direction and speed for all heights, with optional gusts; the aircraft flies its heading through
    ///   the air and drifts, and the "Normal" rate follows the ground speed along the final.
    /// - Route (<see cref="SetRoute"/>): points flown in order, each with its altitude and speed; after the last one
    ///   the aircraft keeps the course of the last leg.
    ///
    /// Instructions (heading, vertical, speed) are carried out after the pilot reaction (<see cref="ReactionSeconds"/>,
    /// counted in simulation time); a new instruction of the same kind for the same aircraft replaces the one still
    /// waiting, so a run of wheel steps is carried out after the last one. The stick, the turn rate, freeze, rename
    /// and remove act at once. Used from the user interface and from the radar's data timer at the same time:
    /// everything is under one lock. The engine never reads the wall clock (the caller tells it how much time has
    /// passed, <see cref="Advance"/>) and its gusts come from a seeded random source: the same instructions give the
    /// same flight.
    /// </summary>
    public sealed class SimEngine
    {
        /// <summary>Length of a simulation step (s).</summary>
        public const double Step = 0.1;
        /// <summary>Longest wall time made up in one <see cref="Advance"/> (s): after a pause of the computer the aircraft do not jump.</summary>
        public const double MaxCatchUp = 2;
        /// <summary>Rate one turn (°/s), half rate, and the default vertical rate (ft/min).</summary>
        public const double StandardTurnRate = 3;
        public const double HalfTurnRate = 1.5;
        public const double DefaultVerticalRate = 1000;
        /// <summary>How fast the turn rate changes (°/s per second): rate one in 2 s.</summary>
        public const double TurnRateChange = 1.5;
        /// <summary>Free turn rate with the stick at its edge: start, growth per second held, maximum (°/s), and its bank limit (degrees).</summary>
        public const double FreeStartRate = 1.5;
        public const double FreeGrowth = 1.5;
        public const double FreeMaxRate = 10;
        public const double FreeMaxBank = 60;
        /// <summary>How fast the stick at its edge changes the commanded vertical speed (ft/min per second).</summary>
        public const double StickVerticalRate = 600;
        /// <summary>Limits of the commanded values (the class may set tighter ones).</summary>
        public const double MinTurnRate = 0.5;
        public const double MaxTurnRate = 10;
        public const double MinVerticalRate = 100;
        public const double MaxVerticalRate = 6000;
        public const double MaxClimb = 8000;
        public const double MaxDescent = -8000;
        public const double MaxSpeed = 600;
        public const double MaxAltitude = 60000;
        public const double MaxReaction = 5;
        public const double MaxWind = 150;
        public const double MaxGust = 60;
        /// <summary>Feet in a nautical mile (as the radar).</summary>
        public const double FeetPerNM = 6076.11549;
        /// <summary>Standard gravity (m/s²) and metres per second in a knot, for the turn rate of a bank angle.</summary>
        private const double Gravity = 9.80665;
        private const double MetresPerSecondPerKnot = 1852.0 / 3600;
        /// <summary>Rounding allowance: a value this close to its target in the last step is taken as reached.</summary>
        private const double Epsilon = 1e-9;
        /// <summary>Touching the ground this close to the runway (NM to the side, NM before the threshold) counts as landed.</summary>
        private const double RunwayHalfWidthForLanding = 0.1;
        private const double BeforeThresholdForLanding = 0.2;
        /// <summary>How long a landed aircraft rolls on before it is removed (s).</summary>
        public const double LandedRemoveAfter = 5;

        /// <summary>Kinds of pilot instructions: a new one replaces the one of the same kind still waiting.</summary>
        private enum Kind { Heading, Vertical, Speed }

        private sealed record Pending(string Callsign, Kind Kind, long DueStep, Action<SimAircraft> Change);

        private readonly object sync = new();
        private readonly List<SimAircraft> aircraft = [];
        private readonly List<Pending> pending = [];
        private readonly Random random = new(1);
        private List<PerformanceClass> classes = PerformanceClass.Defaults();
        private long steps;
        private double accumulated;
        private int number;
        private string prefix = "SIM";
        private bool paused;
        private double reaction;
        private double glidePathCourse = double.NaN;
        private double glidePathAngle;
        /// <summary>The reference runway: threshold elevation (the ground everywhere, ft) and length (NM) from the threshold along the final course.</summary>
        private double runwayElevation;
        private double runwayLength;
        private double windFrom;
        private double windSpeed;
        private double windGust;
        private double gustNow;
        private double gustTarget;
        private long gustNextStep;

        /// <summary>Time of the simulation (s since the start): a whole number of steps.</summary>
        public double Time
        {
            get { lock (sync) return steps * Step; }
        }

        /// <summary>Paused: nothing moves and the pilot reaction waits; instructions are still taken.</summary>
        public bool Paused
        {
            get { lock (sync) return paused; }
            set { lock (sync) paused = value; }
        }

        /// <summary>Pilot reaction (s, 0–5): instructions are carried out this long after they are given (simulation time).</summary>
        public double ReactionSeconds
        {
            get { lock (sync) return reaction; }
            set { lock (sync) reaction = double.IsFinite(value) ? Math.Clamp(value, 0, MaxReaction) : 0; }
        }

        /// <summary>Start of the callsigns given automatically (SIM → SIM1, SIM2...): 1 to 6 letters or digits, upper case.</summary>
        public string CallsignPrefix
        {
            get { lock (sync) return prefix; }
            set
            {
                string text = (value ?? "").Trim().ToUpperInvariant();
                if (text.Length is < 1 or > 6 || !text.All(char.IsAsciiLetterOrDigit))
                    throw new ArgumentException("The callsign prefix must be 1 to 6 letters or digits.", nameof(value));
                lock (sync) prefix = text;
            }
        }

        public int Count
        {
            get { lock (sync) return aircraft.Count; }
        }

        /// <summary>The performance classes new aircraft can have (copies), in their order.</summary>
        public IReadOnlyList<PerformanceClass> Classes
        {
            get { lock (sync) return classes.Select(c => c.Copy()).ToList(); }
        }

        /// <summary>Wind: direction it blows from (degrees true), speed and gusts above it (kt).</summary>
        public (double FromTrue, double SpeedKt, double GustKt) Wind
        {
            get { lock (sync) return (windFrom, windSpeed, windGust); }
        }

        /// <summary>Replaces the performance classes (for aircraft added from now on; those flying keep theirs).</summary>
        public void SetPerformanceClasses(IEnumerable<PerformanceClass> list)
        {
            List<PerformanceClass> copies = [];
            foreach (PerformanceClass item in list)
            {
                PerformanceClass copy = item.Copy();
                copy.Normalize();
                if (copy.Name.Length > 0 && !copies.Any(c => Same(c.Name, copy.Name))) copies.Add(copy);
            }
            lock (sync) classes = copies.Count > 0 ? copies : PerformanceClass.Defaults();
        }

        /// <summary>Wind for all aircraft, at once: from (degrees true), speed and gusts above it (kt).</summary>
        public void SetWind(double fromTrue, double speedKt, double gustKt)
        {
            lock (sync)
            {
                windFrom = double.IsFinite(fromTrue) ? Normalize(fromTrue) : 0;
                windSpeed = double.IsFinite(speedKt) ? Math.Clamp(speedKt, 0, MaxWind) : 0;
                windGust = double.IsFinite(gustKt) ? Math.Clamp(gustKt, 0, MaxGust) : 0;
                if (windGust == 0) gustNow = gustTarget = 0;
            }
        }

        /// <summary>
        /// The reference runway, threshold at the origin of the plane: final course (degrees true, the direction of
        /// landing) and glide path angle (degrees), for the "Normal" vertical speed; threshold elevation (ft), the
        /// ground of the simulation; length (NM), to tell a landing on it from a contact with the ground elsewhere.
        /// NaN course: no runway (the ground at 0 ft, every contact outside a runway).
        /// </summary>
        public void SetGlidePath(double finalCourseTrue, double angleDegrees, double elevationFt = 0, double lengthNM = 0)
        {
            lock (sync)
            {
                glidePathCourse = double.IsFinite(finalCourseTrue) ? Normalize(finalCourseTrue) : double.NaN;
                glidePathAngle = double.IsFinite(angleDegrees) ? Math.Clamp(angleDegrees, 0, 10) : 0;
                runwayElevation = double.IsFinite(elevationFt) ? Math.Clamp(elevationFt, -1500, 15000) : 0;
                runwayLength = double.IsFinite(lengthNM) ? Math.Clamp(lengthNM, 0, 5) : 0;
            }
        }

        /// <summary>Elevation of the ground of the simulation (ft): the threshold of the reference runway.</summary>
        public double GroundElevation
        {
            get { lock (sync) return runwayElevation; }
        }

        /// <summary>True if an aircraft of the simulation already has this callsign (not case sensitive).</summary>
        public bool IsCallsignTaken(string callsign)
        {
            lock (sync) return Find(callsign) != null;
        }

        /// <summary>
        /// Adds an aircraft at once (it is in the next <see cref="Snapshot"/>), flying straight and level: commanded
        /// values = actual values. Without a callsign it gets the next free one of <see cref="CallsignPrefix"/>;
        /// without a class (or an unknown one) the Turboprop, or the first class. The speed is kept within the class.
        /// Returns its callsign.
        /// </summary>
        public string Add(double x, double y, double altitudeFt, double headingTrue, double speedKt, string? callsign = null, string? performanceClass = null,
            Func<string, bool>? takenElsewhere = null)
        {
            lock (sync)
            {
                // Callsigns also flying outside the simulation (real or test traffic): never given (the radar's filters
                // and track IDs follow the callsign, so it is chosen once, before the aircraft exists).
                bool Taken(string candidate) => Find(candidate) != null || takenElsewhere?.Invoke(candidate) == true;
                string name;
                if (string.IsNullOrWhiteSpace(callsign))
                {
                    do name = prefix + (++number).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    while (Taken(name));
                }
                else
                {
                    name = CheckCallsign(callsign);
                    if (Find(name) != null) throw new InvalidOperationException($"{name} is already in the simulation.");
                    if (takenElsewhere?.Invoke(name) == true)
                    {
                        // Flying outside the simulation: the same with a letter (SIM1 → SIM1A).
                        string? free = null;
                        for (char letter = 'A'; letter <= 'Z' && free == null; letter++)
                        {
                            string candidate = name + letter;
                            if (candidate.Length <= 10 && !Taken(candidate)) free = candidate;
                        }
                        name = free ?? throw new InvalidOperationException($"{name} is already flying (real or test traffic).");
                    }
                }
                PerformanceClass kind = (classes.FirstOrDefault(c => Same(c.Name, performanceClass ?? ""))
                    ?? classes.FirstOrDefault(c => Same(c.Name, PerformanceClass.DefaultName))
                    ?? classes[0]).Copy();
                double heading = Normalize(headingTrue);
                double altitude = Math.Clamp(altitudeFt, 0, MaxAltitude);
                double speed = Math.Clamp(double.IsFinite(speedKt) ? speedKt : kind.ApproachSpeedKt, kind.MinSpeedKt, kind.MaxSpeedKt);
                SimAircraft plane = new()
                {
                    Callsign = name,
                    Class = kind,
                    X = x,
                    Y = y,
                    AltitudeFt = altitude,
                    HeadingTrue = heading,
                    SpeedKt = speed,
                    TargetHeading = heading,
                    TargetAltitudeFt = altitude,
                    TargetSpeedKt = speed,
                    GroundSpeedKt = speed,
                    TrackTrue = heading
                };
                Ground(plane, WindNow());
                plane.BestVerticalSpeedFpm = BestVerticalSpeed(plane);
                aircraft.Add(plane);
                return name;
            }
        }

        /// <summary>Gives an aircraft another callsign at once (not a pilot instruction). False if the new one is taken or the aircraft is gone.</summary>
        public bool Rename(string callsign, string newCallsign)
        {
            string name = CheckCallsign(newCallsign);
            lock (sync)
            {
                SimAircraft? plane = Find(callsign);
                if (plane == null) return false;
                SimAircraft? other = Find(name);
                if (other != null && other != plane) return false;
                plane.Callsign = name;
                // Instructions still waiting for the old name follow the aircraft.
                for (int i = 0; i < pending.Count; i++)
                {
                    if (Same(pending[i].Callsign, callsign)) pending[i] = pending[i] with { Callsign = name };
                }
                return true;
            }
        }

        /// <summary>Instruction: heading to fly (degrees true), turning on this side.</summary>
        public void SetHeading(string callsign, double headingTrue, TurnDirection direction = TurnDirection.Shortest) =>
            Instruct(callsign, Kind.Heading, a => { a.TargetHeading = Normalize(headingTrue); a.TurnDirection = direction; a.Route = null; });

        /// <summary>Instruction: altitude to reach (ft above mean sea level), optionally with a new vertical rate (ft/min).</summary>
        public void SetAltitude(string callsign, double altitudeFt, double? verticalRateFpm = null) =>
            Instruct(callsign, Kind.Vertical, a =>
            {
                a.VerticalMode = VerticalMode.Altitude;
                a.TargetAltitudeFt = Math.Clamp(altitudeFt, runwayElevation, MaxAltitude);
                a.RouteAltitude = false;
                if (verticalRateFpm is double rate) a.SelectedVerticalRateFpm = Math.Clamp(Math.Abs(rate), MinVerticalRate, MaxVerticalRate);
            });

        /// <summary>Instruction: a vertical speed to keep (ft/min, negative descending), until told otherwise; within the class.</summary>
        public void SetVerticalSpeed(string callsign, double verticalSpeedFpm) =>
            Instruct(callsign, Kind.Vertical, a =>
            {
                a.VerticalMode = VerticalMode.Rate;
                a.CommandedVerticalSpeedFpm = Math.Clamp(verticalSpeedFpm, -a.Class.MaxDescentFpm, a.Class.MaxClimbFpm);
                a.RouteAltitude = false;
            });

        /// <summary>Instruction: the "Normal" rate, the vertical speed that keeps the glide path at the aircraft's ground speed.</summary>
        public void SetGlidePathRate(string callsign) =>
            Instruct(callsign, Kind.Vertical, a => { a.VerticalMode = VerticalMode.GlidePath; a.RouteAltitude = false; });

        /// <summary>Instruction: speed to reach (kt, true airspeed), within the class.</summary>
        public void SetSpeed(string callsign, double speedKt) =>
            Instruct(callsign, Kind.Speed, a => { a.TargetSpeedKt = Math.Clamp(speedKt, a.Class.MinSpeedKt, a.Class.MaxSpeedKt); a.RouteSpeed = false; });

        /// <summary>Vertical rate used to reach a commanded altitude (ft/min). At once.</summary>
        public void SetVerticalRate(string callsign, double verticalRateFpm) =>
            Change(callsign, a => a.SelectedVerticalRateFpm = Math.Clamp(Math.Abs(verticalRateFpm), MinVerticalRate, MaxVerticalRate));

        /// <summary>Turn rate used to reach the commanded heading (°/s). At once.</summary>
        public void SetTurnRate(string callsign, double degreesPerSecond) =>
            Change(callsign, a => a.SelectedTurnRateDps = Math.Clamp(degreesPerSecond, MinTurnRate, MaxTurnRate));

        /// <summary>Half rate, rate one or free (see <see cref="TurnRateMode"/>). At once.</summary>
        public void SetTurnRateMode(string callsign, TurnRateMode mode) =>
            Change(callsign, a =>
            {
                a.TurnRateMode = mode;
                a.SelectedTurnRateDps = mode == TurnRateMode.Half ? HalfTurnRate : StandardTurnRate;
            });

        /// <summary>
        /// The stick (pilot flying), at once: X (−1..1, + right) turns at the turn rate, Y (−1..1, + up) changes the
        /// vertical speed. Moving it cancels the instruction of that axis still waiting. Released (0), the aircraft
        /// rolls out on about the heading reached, and the vertical speed reached stays.
        /// </summary>
        public void SetStick(string callsign, double x, double y)
        {
            x = double.IsFinite(x) ? Math.Clamp(x, -1, 1) : 0;
            y = double.IsFinite(y) ? Math.Clamp(y, -1, 1) : 0;
            lock (sync)
            {
                SimAircraft? plane = Find(callsign);
                if (plane == null) return;
                if (x != 0) pending.RemoveAll(p => Same(p.Callsign, callsign) && p.Kind == Kind.Heading);
                if (y != 0) pending.RemoveAll(p => Same(p.Callsign, callsign) && p.Kind == Kind.Vertical);
                // The pilot flying by hand: the stick sideways ends the route, up or down takes over its altitudes.
                if (x != 0) plane.Route = null;
                if (y != 0) plane.RouteAltitude = false;
                if (x == 0 && plane.StickX != 0)
                {
                    // Released: the heading where the roll-out ends (the turn goes on a little while the wings level).
                    double rollOut = plane.TurnRateDps * Math.Abs(plane.TurnRateDps) / (2 * TurnRateChange);
                    plane.TargetHeading = Normalize(plane.HeadingTrue + rollOut);
                    plane.TurnDirection = TurnDirection.Shortest;
                    plane.StickHeld = 0;
                }
                if (y != 0 && plane.VerticalMode != VerticalMode.Rate)
                {
                    plane.VerticalMode = VerticalMode.Rate;
                    plane.CommandedVerticalSpeedFpm = plane.VerticalSpeedFpm;
                }
                plane.StickX = x;
                plane.StickY = y;
            }
        }

        /// <summary>
        /// The route of an aircraft, at once (the instructor's plan, not a pilot instruction; the instructions still
        /// waiting for it are dropped). It flies to the points in order, each time turning towards the next one (its
        /// track pointed at the point, against the wind) and at once climbing or descending (at its selected vertical
        /// rate) and changing speed to reach that point's altitude and speed (a NaN altitude or speed: kept as it is).
        /// A point is passed when it is flown over, or when it falls behind after the aircraft turned towards it; a
        /// point inside the circle of the turn is first left behind, flying straight, then turned to (never circled). After the last point it keeps the course of the last leg (from the point before,
        /// or from where the route was given) with that point's altitude and speed: no capture of the final, the
        /// aircraft is on the final only if the last two points are on the extended centreline. A heading instruction
        /// or the stick sideways ends the route; an altitude, rate or speed instruction (or the stick up or down) takes
        /// that over for the rest of the route. Empty or null: no route.
        /// </summary>
        public void SetRoute(string callsign, IEnumerable<RoutePoint>? points)
        {
            List<RoutePoint> list = (points ?? Enumerable.Empty<RoutePoint>()).Where(p => p != null && double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
            lock (sync)
            {
                SimAircraft? plane = Find(callsign);
                if (plane == null) return;
                pending.RemoveAll(p => Same(p.Callsign, callsign));
                if (list.Count == 0 || plane.Contact != GroundContact.Airborne)
                {
                    plane.Route = null;
                    return;
                }
                plane.Route = list.ToArray();
                plane.RouteNext = 0;
                plane.LegStartX = plane.X;
                plane.LegStartY = plane.Y;
                plane.RouteApproached = false;
                plane.RouteAltitude = true;
                plane.RouteSpeed = true;
                StartLeg(plane);
            }
        }

        /// <summary>Ends the route at once: the aircraft goes on straight, at the altitude and speed it was given.</summary>
        public void ClearRoute(string callsign) => Change(callsign, a =>
        {
            if (a.Route == null) return;
            a.Route = null;
            // Straight on: the heading where the turn now going on can end.
            a.TargetHeading = Normalize(a.HeadingTrue + a.TurnRateDps * Math.Abs(a.TurnRateDps) / (2 * TurnRateChange));
            a.TurnDirection = TurnDirection.Shortest;
        });

        /// <summary>Holds an aircraft where it is (true) or lets it go on (false). At once, not a pilot instruction.</summary>
        public void SetFrozen(string callsign, bool frozen) => Change(callsign, a => a.Frozen = frozen);

        public void Remove(string callsign)
        {
            lock (sync)
            {
                aircraft.RemoveAll(a => Same(a.Callsign, callsign));
                pending.RemoveAll(p => Same(p.Callsign, callsign));
            }
        }

        /// <summary>Removes all aircraft; the automatic callsigns start again from 1.</summary>
        public void Clear()
        {
            lock (sync)
            {
                aircraft.Clear();
                pending.Clear();
                number = 0;
            }
        }

        /// <summary>
        /// Moves the simulation on by this much wall time (s): as many whole steps as fit, the rest kept for the next
        /// call. At most <see cref="MaxCatchUp"/> s at a time. Paused: only instructions already due are carried out.
        /// </summary>
        public void Advance(double wallSeconds)
        {
            lock (sync)
            {
                if (paused)
                {
                    ApplyPending();
                    accumulated = 0;
                    return;
                }
                if (double.IsNaN(wallSeconds) || wallSeconds <= 0) return;
                accumulated += Math.Min(wallSeconds, MaxCatchUp);
                while (accumulated >= Step - 1e-9)
                {
                    accumulated -= Step;
                    StepOnce();
                }
            }
        }

        /// <summary>Runs this many seconds of simulation at once, whatever the pause and the catch-up limit (tests, scenarios).</summary>
        public void RunFor(double seconds)
        {
            lock (sync)
            {
                long count = (long)Math.Round(seconds / Step);
                for (long i = 0; i < count; i++) StepOnce();
            }
        }

        /// <summary>Copies of all aircraft now, in the order they were added.</summary>
        public IReadOnlyList<AircraftState> Snapshot()
        {
            lock (sync) return aircraft.Select(a => a.ToState(PendingOf(a.Callsign))).ToList();
        }

        /// <summary>Copy of one aircraft now, or null.</summary>
        public AircraftState? Get(string callsign)
        {
            lock (sync)
            {
                SimAircraft? plane = Find(callsign);
                return plane?.ToState(PendingOf(plane.Callsign));
            }
        }

        /// <summary>Angle in 0–360.</summary>
        public static double Normalize(double degrees) => (degrees % 360 + 360) % 360;

        /// <summary>Angle in −180..180.</summary>
        public static double Wrap(double degrees) => ((degrees + 180) % 360 + 360) % 360 - 180;

        /// <summary>Turn rate (°/s) of a coordinated turn at this bank angle (degrees) and true airspeed (kt).</summary>
        public static double RateForBank(double bankDegrees, double speedKt)
        {
            double speed = Math.Max(20, speedKt) * MetresPerSecondPerKnot;
            return Gravity * Math.Tan(bankDegrees * Math.PI / 180) / speed * 180 / Math.PI;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private int PendingOf(string callsign) => pending.Count(p => Same(p.Callsign, callsign));

        /// <summary>A pilot instruction: after the reaction, replacing the one of the same kind still waiting.</summary>
        private void Instruct(string callsign, Kind kind, Action<SimAircraft> change)
        {
            lock (sync)
            {
                pending.RemoveAll(p => Same(p.Callsign, callsign) && p.Kind == kind);
                pending.Add(new Pending(callsign, kind, steps + (long)Math.Ceiling(reaction / Step - 1e-9), change));
            }
        }

        /// <summary>A change at once (not a pilot instruction).</summary>
        private void Change(string callsign, Action<SimAircraft> change)
        {
            lock (sync)
            {
                SimAircraft? plane = Find(callsign);
                if (plane != null) change(plane);
            }
        }

        private SimAircraft? Find(string callsign) => aircraft.FirstOrDefault(a => Same(a.Callsign, callsign));

        private static string CheckCallsign(string callsign)
        {
            string text = (callsign ?? "").Trim().ToUpperInvariant();
            if (text.Length is < 2 or > 10 || !text.All(char.IsAsciiLetterOrDigit))
                throw new ArgumentException("A callsign is 2 to 10 letters or digits.", nameof(callsign));
            return text;
        }

        /// <summary>Carries out the instructions whose reaction time is over, in the order they were given.</summary>
        private void ApplyPending()
        {
            List<Pending> due = pending.Where(p => p.DueStep <= steps).ToList();
            if (due.Count == 0) return;
            foreach (Pending item in due)
            {
                SimAircraft? plane = Find(item.Callsign);
                // The pilot flying with the stick on that axis: the instruction waits until the stick is let go.
                if (plane != null && ((item.Kind == Kind.Heading && plane.StickX != 0) || (item.Kind == Kind.Vertical && plane.StickY != 0))) continue;
                pending.Remove(item);
                if (plane != null) item.Change(plane);
            }
        }

        /// <summary>Wind now (towards east, towards north, kt), gusts included.</summary>
        private (double East, double North) WindNow()
        {
            double speed = Math.Max(0, windSpeed + gustNow);
            double towards = (windFrom + 180) * Math.PI / 180;
            return (speed * Math.Sin(towards), speed * Math.Cos(towards));
        }

        private void Gusts()
        {
            if (windGust <= 0)
            {
                gustNow = gustTarget = 0;
                return;
            }
            // A new gust every 2–5 s, reached gradually.
            if (steps >= gustNextStep)
            {
                gustTarget = random.NextDouble() * windGust;
                gustNextStep = steps + (long)Math.Round((2 + 3 * random.NextDouble()) / Step);
            }
            gustNow += (gustTarget - gustNow) * Step;
        }

        private void StepOnce()
        {
            ApplyPending();
            steps++;
            Gusts();
            (double East, double North) wind = WindNow();
            foreach (SimAircraft plane in aircraft)
            {
                if (plane.Frozen || plane.Contact == GroundContact.OnGround)
                {
                    plane.TurnRateDps = 0;
                    plane.VerticalSpeedFpm = 0;
                    if (plane.Contact == GroundContact.OnGround) plane.GroundSpeedKt = 0;
                    continue;
                }
                FollowRoute(plane);
                Turn(plane);
                Accelerate(plane);
                Ground(plane, wind);
                plane.BestVerticalSpeedFpm = BestVerticalSpeed(plane);
                Vertical(plane);
                plane.X += plane.GroundSpeedKt * Math.Sin(plane.TrackTrue * Math.PI / 180) * Step / 3600;
                plane.Y += plane.GroundSpeedKt * Math.Cos(plane.TrackTrue * Math.PI / 180) * Step / 3600;
            }
            // Landed aircraft roll on for a few seconds, then they are gone.
            aircraft.RemoveAll(a => a.Contact == GroundContact.Landed && steps - a.LandedStep >= (long)Math.Round(LandedRemoveAfter / Step));
        }

        /// <summary>The aircraft reached the ground: on the runway it has landed, elsewhere it stops there.</summary>
        private void TouchGround(SimAircraft plane)
        {
            if (plane.Contact != GroundContact.Airborne) return;
            bool onRunway = false;
            if (!double.IsNaN(glidePathCourse) && runwayLength > 0)
            {
                double course = glidePathCourse * Math.PI / 180;
                // From the threshold: along the landing direction and to its right.
                double along = plane.X * Math.Sin(course) + plane.Y * Math.Cos(course);
                double lateral = plane.X * Math.Cos(course) - plane.Y * Math.Sin(course);
                onRunway = along >= -BeforeThresholdForLanding && along <= runwayLength && Math.Abs(lateral) <= RunwayHalfWidthForLanding;
            }
            plane.Contact = onRunway ? GroundContact.Landed : GroundContact.OnGround;
            plane.LandedStep = steps;
            plane.Route = null;
            plane.VerticalSpeedFpm = 0;
            plane.VerticalMode = VerticalMode.Altitude;
            plane.TargetAltitudeFt = plane.AltitudeFt;
            if (!onRunway)
            {
                plane.SpeedKt = plane.TargetSpeedKt = 0;
                plane.GroundSpeedKt = 0;
                plane.TurnRateDps = 0;
            }
        }

        /// <summary>Speed and direction over the ground: the speed along the heading plus the wind.</summary>
        private static void Ground(SimAircraft plane, (double East, double North) wind)
        {
            double heading = plane.HeadingTrue * Math.PI / 180;
            double east = plane.SpeedKt * Math.Sin(heading) + wind.East;
            double north = plane.SpeedKt * Math.Cos(heading) + wind.North;
            plane.GroundSpeedKt = Math.Sqrt(east * east + north * north);
            plane.TrackTrue = plane.GroundSpeedKt > 0.5 ? Normalize(Math.Atan2(east, north) * 180 / Math.PI) : plane.HeadingTrue;
            plane.DriftDegrees = Wrap(plane.TrackTrue - plane.HeadingTrue);
        }

        /// <summary>Vertical speed that keeps the glide path: from the ground speed along the final course (ft/min, negative; 0 without a glide path).</summary>
        private double BestVerticalSpeed(SimAircraft plane)
        {
            if (double.IsNaN(glidePathCourse) || glidePathAngle <= 0) return 0;
            // With the mean wind: pilots fly through the gusts, they do not chase them.
            double course = glidePathCourse * Math.PI / 180;
            double heading = plane.HeadingTrue * Math.PI / 180;
            double towards = (windFrom + 180) * Math.PI / 180;
            double east = plane.SpeedKt * Math.Sin(heading) + windSpeed * Math.Sin(towards);
            double north = plane.SpeedKt * Math.Cos(heading) + windSpeed * Math.Cos(towards);
            double along = Math.Max(0, east * Math.Sin(course) + north * Math.Cos(course));
            return -along * FeetPerNM / 60 * Math.Tan(glidePathAngle * Math.PI / 180);
        }

        /// <summary>The altitude and the speed of the point the route now flies to (those the route still gives; NaN: kept).</summary>
        private void StartLeg(SimAircraft plane)
        {
            if (plane.Route == null) return;
            RoutePoint point = plane.Route[plane.RouteNext];
            if (plane.RouteAltitude && double.IsFinite(point.AltitudeFt))
            {
                plane.VerticalMode = VerticalMode.Altitude;
                plane.TargetAltitudeFt = Math.Clamp(point.AltitudeFt, runwayElevation, MaxAltitude);
            }
            if (plane.RouteSpeed && double.IsFinite(point.SpeedKt))
            {
                plane.TargetSpeedKt = Math.Clamp(point.SpeedKt, plane.Class.MinSpeedKt, plane.Class.MaxSpeedKt);
            }
        }

        /// <summary>Heading (degrees true) that makes good this course over the ground at this airspeed, in the mean wind.</summary>
        private double HeadingForCourse(double courseTrue, double speedKt)
        {
            double towards = (windFrom + 180) * Math.PI / 180;
            double east = windSpeed * Math.Sin(towards);
            double north = windSpeed * Math.Cos(towards);
            double course = courseTrue * Math.PI / 180;
            // Wind across the course, to its right.
            double across = east * Math.Cos(course) - north * Math.Sin(course);
            double ratio = Math.Clamp(across / Math.Max(1, speedKt), -1, 1);
            return Normalize(courseTrue - Math.Asin(ratio) * 180 / Math.PI);
        }

        /// <summary>One step of the route: the heading to the point it flies to; passing a point, the next one (or, after the last, the course of the last leg).</summary>
        private void FollowRoute(SimAircraft plane)
        {
            if (plane.Route == null) return;
            if (plane.StickX != 0 || plane.Route.Count == 0)
            {
                plane.Route = null;
                return;
            }
            RoutePoint point = plane.Route[plane.RouteNext];
            double dx = point.X - plane.X;
            double dy = point.Y - plane.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double track = plane.TrackTrue * Math.PI / 180;
            // How far ahead along the track the point is (negative: behind).
            double along = dx * Math.Sin(track) + dy * Math.Cos(track);
            // How far to the right of it (negative: to the left).
            double lateral = dx * Math.Cos(track) - dy * Math.Sin(track);
            // Turned towards it: the point within 30° of the track.
            if (along > 0 && Math.Abs(Math.Atan2(lateral, along)) < Math.PI / 6) plane.RouteApproached = true;
            // (Within this step's flight: flown over.)
            double overhead = Math.Max(0.02, plane.GroundSpeedKt / 3600 * Step);
            if (distance <= overhead || (plane.RouteApproached && along <= 0))
            {
                if (plane.RouteNext >= plane.Route.Count - 1)
                {
                    // The last point: on along the course of the last leg (where it is flying now if the leg has no length).
                    double legX = point.X - plane.LegStartX;
                    double legY = point.Y - plane.LegStartY;
                    double course = Math.Sqrt(legX * legX + legY * legY) > 0.01 ? Normalize(Math.Atan2(legX, legY) * 180 / Math.PI) : plane.TrackTrue;
                    plane.TargetHeading = HeadingForCourse(course, plane.SpeedKt);
                    plane.TurnDirection = TurnDirection.Shortest;
                    plane.Route = null;
                    return;
                }
                // The next point: steered to from the next step.
                plane.LegStartX = point.X;
                plane.LegStartY = point.Y;
                plane.RouteNext++;
                plane.RouteApproached = false;
                StartLeg(plane);
                return;
            }
            if (!plane.RouteApproached)
            {
                // A point inside the circle of the turn on its side cannot be turned to: on straight until it can.
                double rate = Math.Max(MinTurnRate, Math.Min(plane.SelectedTurnRateDps, RateForBank(plane.Class.MaxBankDegrees, plane.SpeedKt)));
                double radius = Math.Max(plane.GroundSpeedKt, 1) / 3600 / (rate * Math.PI / 180);
                int side = lateral >= 0 ? 1 : -1;
                double centreX = side * radius * Math.Cos(track);
                double centreY = -side * radius * Math.Sin(track);
                if (Math.Sqrt((dx - centreX) * (dx - centreX) + (dy - centreY) * (dy - centreY)) < radius * 1.1 + 0.05)
                {
                    plane.TargetHeading = plane.HeadingTrue;
                    plane.TurnDirection = TurnDirection.Shortest;
                    return;
                }
            }
            plane.TargetHeading = HeadingForCourse(Normalize(Math.Atan2(dx, dy) * 180 / Math.PI), plane.SpeedKt);
            plane.TurnDirection = TurnDirection.Shortest;
        }

        /// <summary>The turn rate moves towards the one wanted, at most <see cref="TurnRateChange"/> per second.</summary>
        private static void MoveTurnRate(SimAircraft plane, double wanted)
        {
            double change = TurnRateChange * Step;
            plane.TurnRateDps += Math.Clamp(wanted - plane.TurnRateDps, -change, change);
        }

        private static void Turn(SimAircraft plane)
        {
            if (plane.StickX != 0)
            {
                // Stick: turns at the turn rate (Free: faster the longer it is held at its edge, with a steeper bank).
                if (Math.Abs(plane.StickX) > 0.95) plane.StickHeld += Step; else plane.StickHeld = 0;
                bool free = plane.TurnRateMode == TurnRateMode.Free;
                double rate = free ? Math.Min(FreeMaxRate, FreeStartRate + FreeGrowth * plane.StickHeld) : plane.SelectedTurnRateDps;
                rate = Math.Min(rate, RateForBank(free ? FreeMaxBank : plane.Class.MaxBankDegrees, plane.SpeedKt));
                MoveTurnRate(plane, plane.StickX * rate);
                plane.HeadingTrue = Normalize(plane.HeadingTrue + plane.TurnRateDps * Step);
                plane.TargetHeading = plane.HeadingTrue;
                plane.TurnDirection = TurnDirection.Shortest;
                return;
            }
            double difference = Wrap(plane.TargetHeading - plane.HeadingTrue);
            int side = plane.TurnDirection switch
            {
                TurnDirection.Left => -1,
                TurnDirection.Right => 1,
                _ => Math.Sign(difference)
            };
            // Degrees still to turn on that side.
            double remaining = side > 0 ? Normalize(plane.TargetHeading - plane.HeadingTrue)
                : side < 0 ? Normalize(plane.HeadingTrue - plane.TargetHeading) : 0;
            if (remaining > 360 - 1e-6) remaining = 0;
            if (side == 0 || remaining < 1e-6)
            {
                // On the heading: level the wings (a turn still going on carries a little past it, then comes back).
                if (Math.Abs(plane.TurnRateDps) <= TurnRateChange * Step + Epsilon)
                {
                    plane.HeadingTrue = plane.TargetHeading;
                    plane.TurnRateDps = 0;
                    plane.TurnDirection = TurnDirection.Shortest;
                    return;
                }
                MoveTurnRate(plane, 0);
                plane.HeadingTrue = Normalize(plane.HeadingTrue + plane.TurnRateDps * Step);
                plane.TurnDirection = TurnDirection.Shortest;
                return;
            }
            // The rate that still lets the roll-out end on the heading, within the selected rate and the bank limit.
            double limit = Math.Min(plane.SelectedTurnRateDps, RateForBank(plane.Class.MaxBankDegrees, plane.SpeedKt));
            // (The remaining heading less this step's turn: the rate falls in time, so the roll-out ends on the heading.)
            double wanted = side * Math.Min(limit, Math.Sqrt(2 * TurnRateChange * Math.Max(0, remaining - Math.Abs(plane.TurnRateDps) * Step)));
            MoveTurnRate(plane, wanted);
            double turn = plane.TurnRateDps * Step;
            if (turn * side >= remaining - Epsilon)
            {
                if (Math.Abs(plane.TurnRateDps) <= Math.Sqrt(2 * TurnRateChange * remaining) + TurnRateChange * Step + Epsilon)
                {
                    // This step reaches the heading at the end of the roll-out.
                    plane.HeadingTrue = plane.TargetHeading;
                    plane.TurnRateDps = 0;
                    plane.TurnDirection = TurnDirection.Shortest;
                    return;
                }
                // Turning too fast onto a heading just ahead: it carries past it a little and comes back the shorter way.
                plane.TurnDirection = TurnDirection.Shortest;
            }
            plane.HeadingTrue = Normalize(plane.HeadingTrue + turn);
        }

        private void Vertical(SimAircraft plane)
        {
            PerformanceClass kind = plane.Class;
            double change = kind.VerticalAccelerationFpmPerSecond * Step;
            if (plane.StickY != 0)
            {
                // Stick: changes the commanded vertical speed at once.
                plane.VerticalMode = VerticalMode.Rate;
                plane.CommandedVerticalSpeedFpm = Math.Clamp(plane.CommandedVerticalSpeedFpm + plane.StickY * StickVerticalRate * Step, -kind.MaxDescentFpm, kind.MaxClimbFpm);
            }
            double wanted;
            switch (plane.VerticalMode)
            {
                case VerticalMode.Altitude:
                    double difference = plane.TargetAltitudeFt - plane.AltitudeFt;
                    if (Math.Abs(difference) < 0.5 && Math.Abs(plane.VerticalSpeedFpm) <= change + Epsilon)
                    {
                        plane.AltitudeFt = plane.TargetAltitudeFt;
                        plane.VerticalSpeedFpm = 0;
                        return;
                    }
                    // The rate that still lets the level-off end on the altitude: v² / (120 a) feet are needed to stop.
                    // (Less this step's climb or descent, so the level-off ends on the altitude.)
                    double capture = Math.Sqrt(120 * kind.VerticalAccelerationFpmPerSecond * Math.Max(0, Math.Abs(difference) - Math.Abs(plane.VerticalSpeedFpm) / 60 * Step));
                    wanted = Math.Sign(difference) * Math.Min(plane.SelectedVerticalRateFpm, capture);
                    break;
                case VerticalMode.Rate:
                    wanted = plane.CommandedVerticalSpeedFpm;
                    break;
                default:
                    wanted = plane.BestVerticalSpeedFpm;
                    break;
            }
            wanted = Math.Clamp(wanted, -kind.MaxDescentFpm, kind.MaxClimbFpm);
            plane.VerticalSpeedFpm += Math.Clamp(wanted - plane.VerticalSpeedFpm, -change, change);
            double before = plane.AltitudeFt;
            plane.AltitudeFt += plane.VerticalSpeedFpm / 60 * Step;
            if (plane.VerticalMode == VerticalMode.Altitude && (before - plane.TargetAltitudeFt) * (plane.AltitudeFt - plane.TargetAltitudeFt) <= 0
                && Math.Abs(plane.VerticalSpeedFpm) <= Math.Sqrt(120 * kind.VerticalAccelerationFpmPerSecond * Math.Abs(before - plane.TargetAltitudeFt)) + change + Epsilon)
            {
                // Reached the altitude in this step, nearly level. (Arriving fast, it carries past it a little and comes back.)
                plane.AltitudeFt = plane.TargetAltitudeFt;
                plane.VerticalSpeedFpm = 0;
            }
            if (plane.AltitudeFt <= runwayElevation)
            {
                plane.AltitudeFt = runwayElevation;
                if (before > runwayElevation || plane.VerticalSpeedFpm < 0) TouchGround(plane);
                if (plane.VerticalSpeedFpm < 0) plane.VerticalSpeedFpm = 0;
                return;
            }
            // The altitude to come back to with an altitude instruction starts from here.
            if (plane.VerticalMode != VerticalMode.Altitude) plane.TargetAltitudeFt = plane.AltitudeFt;
        }

        private static void Accelerate(SimAircraft plane)
        {
            double difference = plane.TargetSpeedKt - plane.SpeedKt;
            double change = (difference > 0 ? plane.Class.AccelerationKtPerSecond : plane.Class.DecelerationKtPerSecond) * Step;
            plane.SpeedKt = Math.Abs(difference) <= change + Epsilon ? plane.TargetSpeedKt : plane.SpeedKt + Math.Sign(difference) * change;
        }
    }
}
