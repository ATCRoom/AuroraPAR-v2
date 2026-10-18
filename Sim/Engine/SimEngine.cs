namespace ATCRoom.Sim
{
    /// <summary>
    /// The simulation: the aircraft of the instructor, moved in fixed steps of <see cref="Step"/> seconds of its own
    /// clock, in the <see cref="LocalPlane"/>.
    ///
    /// Each aircraft has actual values (heading, altitude or vertical speed, speed) and commanded ones; every step
    /// moves the actual values towards the commanded ones. Heading: to a commanded heading at the selected turn rate,
    /// or turned directly by the stick. Vertically, one of three modes (<see cref="VerticalMode"/>): to an altitude
    /// at a selected rate, a commanded vertical speed (the PAR controller's "rate"), or the vertical speed that keeps
    /// the glide path ("Normal"); the stick changes the vertical speed directly. Speed: at fixed accelerations.
    /// Roll-in, bank limit, smooth level-off and wind come later.
    ///
    /// Instructions (heading, vertical, speed) are carried out after the pilot reaction (<see cref="ReactionSeconds"/>,
    /// counted in simulation time); a new instruction of the same kind for the same aircraft replaces the one still
    /// waiting, so a run of wheel steps is carried out after the last one. The stick, the turn rate, freeze, rename
    /// and remove act at once. Used from the user interface and from the radar's data timer at the same time:
    /// everything is under one lock. The engine never reads the wall clock: the caller tells it how much time has
    /// passed (<see cref="Advance"/>).
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
        /// <summary>Free turn rate with the stick at its edge: start, growth per second held, maximum (°/s).</summary>
        public const double FreeStartRate = 1.5;
        public const double FreeGrowth = 1.5;
        public const double FreeMaxRate = 10;
        /// <summary>How fast the stick at its edge changes the vertical speed (ft/min per second).</summary>
        public const double StickVerticalRate = 600;
        /// <summary>Accelerations of the speed (kt/s).</summary>
        public const double Acceleration = 1.5;
        public const double Deceleration = 1.0;
        /// <summary>Limits of the commanded values.</summary>
        public const double MinTurnRate = 0.5;
        public const double MaxTurnRate = 10;
        public const double MinVerticalRate = 100;
        public const double MaxVerticalRate = 6000;
        public const double MaxClimb = 4000;
        public const double MaxDescent = -4000;
        public const double MaxSpeed = 600;
        public const double MaxAltitude = 60000;
        public const double MaxReaction = 5;
        /// <summary>Feet in a nautical mile (as the radar).</summary>
        public const double FeetPerNM = 6076.11549;
        /// <summary>Rounding allowance: a value this close to its target in the last step is taken as reached.</summary>
        private const double Epsilon = 1e-9;

        /// <summary>Kinds of pilot instructions: a new one replaces the one of the same kind still waiting.</summary>
        private enum Kind { Heading, Vertical, Speed }

        private sealed record Pending(string Callsign, Kind Kind, long DueStep, Action<SimAircraft> Change);

        private readonly object sync = new();
        private readonly List<SimAircraft> aircraft = [];
        private readonly List<Pending> pending = [];
        private long steps;
        private double accumulated;
        private int number;
        private string prefix = "SIM";
        private bool paused;
        private double reaction;
        private double glidePathCourse = double.NaN;
        private double glidePathAngle;

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

        /// <summary>
        /// The glide path of the reference runway, for the "Normal" vertical speed: the final course (degrees true,
        /// the direction of landing) and the glide path angle (degrees). NaN course: none.
        /// </summary>
        public void SetGlidePath(double finalCourseTrue, double angleDegrees)
        {
            lock (sync)
            {
                glidePathCourse = double.IsFinite(finalCourseTrue) ? Normalize(finalCourseTrue) : double.NaN;
                glidePathAngle = double.IsFinite(angleDegrees) ? Math.Clamp(angleDegrees, 0, 10) : 0;
            }
        }

        /// <summary>True if an aircraft of the simulation already has this callsign (not case sensitive).</summary>
        public bool IsCallsignTaken(string callsign)
        {
            lock (sync) return Find(callsign) != null;
        }

        /// <summary>
        /// Adds an aircraft at once (it is in the next <see cref="Snapshot"/>), flying straight and level: commanded
        /// values = actual values. Without a callsign it gets the next free one of <see cref="CallsignPrefix"/>.
        /// Returns its callsign.
        /// </summary>
        public string Add(double x, double y, double altitudeFt, double headingTrue, double speedKt, string? callsign = null)
        {
            lock (sync)
            {
                string name;
                if (string.IsNullOrWhiteSpace(callsign))
                {
                    do name = prefix + (++number).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    while (Find(name) != null);
                }
                else
                {
                    name = CheckCallsign(callsign);
                    if (Find(name) != null) throw new InvalidOperationException($"{name} is already in the simulation.");
                }
                double heading = Normalize(headingTrue);
                double altitude = Math.Clamp(altitudeFt, 0, MaxAltitude);
                double speed = Math.Clamp(speedKt, 0, MaxSpeed);
                SimAircraft plane = new()
                {
                    Callsign = name,
                    X = x,
                    Y = y,
                    AltitudeFt = altitude,
                    HeadingTrue = heading,
                    SpeedKt = speed,
                    TargetHeading = heading,
                    TargetAltitudeFt = altitude,
                    TargetSpeedKt = speed
                };
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
            Instruct(callsign, Kind.Heading, a => { a.TargetHeading = Normalize(headingTrue); a.TurnDirection = direction; });

        /// <summary>Instruction: altitude to reach (ft above mean sea level), optionally with a new vertical rate (ft/min).</summary>
        public void SetAltitude(string callsign, double altitudeFt, double? verticalRateFpm = null) =>
            Instruct(callsign, Kind.Vertical, a =>
            {
                a.VerticalMode = VerticalMode.Altitude;
                a.TargetAltitudeFt = Math.Clamp(altitudeFt, 0, MaxAltitude);
                if (verticalRateFpm is double rate) a.SelectedVerticalRateFpm = Math.Clamp(Math.Abs(rate), MinVerticalRate, MaxVerticalRate);
            });

        /// <summary>Instruction: a vertical speed to keep (ft/min, negative descending), until told otherwise.</summary>
        public void SetVerticalSpeed(string callsign, double verticalSpeedFpm) =>
            Instruct(callsign, Kind.Vertical, a =>
            {
                a.VerticalMode = VerticalMode.Rate;
                a.CommandedVerticalSpeedFpm = Math.Clamp(verticalSpeedFpm, MaxDescent, MaxClimb);
            });

        /// <summary>Instruction: the "Normal" rate, the vertical speed that keeps the glide path at the aircraft's ground speed.</summary>
        public void SetGlidePathRate(string callsign) =>
            Instruct(callsign, Kind.Vertical, a => a.VerticalMode = VerticalMode.GlidePath);

        /// <summary>Instruction: speed to reach (kt, true airspeed).</summary>
        public void SetSpeed(string callsign, double speedKt) =>
            Instruct(callsign, Kind.Speed, a => a.TargetSpeedKt = Math.Clamp(speedKt, 0, MaxSpeed));

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
        /// vertical speed. Moving it cancels the instruction of that axis still waiting. Released (0), the heading and
        /// the vertical speed reached stay.
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
                if (x == 0 && plane.StickX != 0)
                {
                    // Released: the heading reached stays.
                    plane.TargetHeading = plane.HeadingTrue;
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

        public void Clear()
        {
            lock (sync)
            {
                aircraft.Clear();
                pending.Clear();
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
                pending.Remove(item);
                SimAircraft? plane = Find(item.Callsign);
                if (plane != null) item.Change(plane);
            }
        }

        private void StepOnce()
        {
            ApplyPending();
            steps++;
            foreach (SimAircraft plane in aircraft)
            {
                plane.BestVerticalSpeedFpm = BestVerticalSpeed(plane);
                if (plane.Frozen)
                {
                    plane.TurnRateDps = 0;
                    plane.VerticalSpeedFpm = 0;
                    continue;
                }
                Turn(plane);
                Vertical(plane);
                Accelerate(plane);
                double heading = plane.HeadingTrue * Math.PI / 180;
                plane.X += plane.SpeedKt * Math.Sin(heading) * Step / 3600;
                plane.Y += plane.SpeedKt * Math.Cos(heading) * Step / 3600;
            }
        }

        /// <summary>Vertical speed that keeps the glide path: from the speed along the final course (ft/min, negative; 0 without a glide path).</summary>
        private double BestVerticalSpeed(SimAircraft plane)
        {
            if (double.IsNaN(glidePathCourse) || glidePathAngle <= 0) return 0;
            double along = Math.Max(0, plane.SpeedKt * Math.Cos((plane.HeadingTrue - glidePathCourse) * Math.PI / 180));
            return -along * FeetPerNM / 60 * Math.Tan(glidePathAngle * Math.PI / 180);
        }

        private static void Turn(SimAircraft plane)
        {
            if (plane.StickX != 0)
            {
                // Stick: turns at once at the turn rate (Free: faster the longer it is held at its edge).
                if (Math.Abs(plane.StickX) > 0.95) plane.StickHeld += Step; else plane.StickHeld = 0;
                double rate = plane.TurnRateMode == TurnRateMode.Free
                    ? Math.Min(FreeMaxRate, FreeStartRate + FreeGrowth * plane.StickHeld)
                    : plane.SelectedTurnRateDps;
                plane.TurnRateDps = plane.StickX * rate;
                plane.HeadingTrue = Normalize(plane.HeadingTrue + plane.TurnRateDps * Step);
                plane.TargetHeading = plane.HeadingTrue;
                plane.TurnDirection = TurnDirection.Shortest;
                return;
            }
            double difference = Normalize(plane.TargetHeading - plane.HeadingTrue);
            if (difference > 180) difference -= 360;
            int side = plane.TurnDirection switch
            {
                TurnDirection.Left => -1,
                TurnDirection.Right => 1,
                _ => Math.Sign(difference)
            };
            // Degrees still to turn on that side.
            double remaining = side > 0 ? Normalize(plane.TargetHeading - plane.HeadingTrue)
                : side < 0 ? Normalize(plane.HeadingTrue - plane.TargetHeading) : 0;
            double turn = plane.SelectedTurnRateDps * Step;
            if (remaining <= turn + Epsilon || remaining > 360 - Epsilon)
            {
                plane.HeadingTrue = plane.TargetHeading;
                plane.TurnRateDps = 0;
                plane.TurnDirection = TurnDirection.Shortest;
                return;
            }
            plane.HeadingTrue = Normalize(plane.HeadingTrue + side * turn);
            plane.TurnRateDps = side * plane.SelectedTurnRateDps;
        }

        private static void Vertical(SimAircraft plane)
        {
            if (plane.StickY != 0)
            {
                // Stick: changes the vertical speed at once.
                plane.VerticalMode = VerticalMode.Rate;
                plane.CommandedVerticalSpeedFpm = Math.Clamp(plane.CommandedVerticalSpeedFpm + plane.StickY * StickVerticalRate * Step, MaxDescent, MaxClimb);
            }
            switch (plane.VerticalMode)
            {
                case VerticalMode.Altitude:
                    double difference = plane.TargetAltitudeFt - plane.AltitudeFt;
                    double change = plane.SelectedVerticalRateFpm / 60 * Step;
                    if (Math.Abs(difference) <= change + Epsilon)
                    {
                        plane.AltitudeFt = plane.TargetAltitudeFt;
                        plane.VerticalSpeedFpm = 0;
                        return;
                    }
                    plane.VerticalSpeedFpm = Math.Sign(difference) * plane.SelectedVerticalRateFpm;
                    break;
                case VerticalMode.Rate:
                    plane.VerticalSpeedFpm = plane.CommandedVerticalSpeedFpm;
                    break;
                case VerticalMode.GlidePath:
                    plane.VerticalSpeedFpm = plane.BestVerticalSpeedFpm;
                    break;
            }
            plane.AltitudeFt += plane.VerticalSpeedFpm / 60 * Step;
            if (plane.AltitudeFt <= 0)
            {
                plane.AltitudeFt = 0;
                plane.VerticalSpeedFpm = 0;
            }
            // The altitude to come back to with an altitude instruction starts from here.
            if (plane.VerticalMode != VerticalMode.Altitude) plane.TargetAltitudeFt = plane.AltitudeFt;
        }

        private static void Accelerate(SimAircraft plane)
        {
            double difference = plane.TargetSpeedKt - plane.SpeedKt;
            double change = (difference > 0 ? Acceleration : Deceleration) * Step;
            plane.SpeedKt = Math.Abs(difference) <= change + Epsilon ? plane.TargetSpeedKt : plane.SpeedKt + Math.Sign(difference) * change;
        }
    }
}
