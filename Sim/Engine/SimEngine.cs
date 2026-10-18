namespace ATCRoom.Sim
{
    /// <summary>
    /// The simulation: the aircraft of the instructor, moved in fixed steps of <see cref="Step"/> seconds of its own
    /// clock, in the <see cref="LocalPlane"/>.
    ///
    /// Each aircraft has actual values (heading, altitude, speed) and commanded ones; every step moves the actual
    /// values towards the commanded ones at the selected rates (turn rate, vertical rate) and at fixed accelerations.
    /// This is the minimal flight model of the first step: roll-in, bank limit, smooth level-off, wind, pilot
    /// reaction and the modes come later.
    ///
    /// Commands are queued and carried out at the start of the next step, so the same commands give the same flight
    /// (no dependence on when the user clicked inside a step). Used from the user interface and from the radar's data
    /// timer at the same time: everything is under one lock. The engine never reads the wall clock: the caller tells
    /// it how much time has passed (<see cref="Advance"/>).
    /// </summary>
    public sealed class SimEngine
    {
        /// <summary>Length of a simulation step (s).</summary>
        public const double Step = 0.1;
        /// <summary>Longest wall time made up in one <see cref="Advance"/> (s): after a pause of the computer the aircraft do not jump.</summary>
        public const double MaxCatchUp = 2;
        /// <summary>Rate one turn (°/s) and the default vertical rate (ft/min).</summary>
        public const double StandardTurnRate = 3;
        public const double DefaultVerticalRate = 1000;
        /// <summary>Accelerations of the speed (kt/s).</summary>
        public const double Acceleration = 1.5;
        public const double Deceleration = 1.0;
        /// <summary>Limits of the commanded values.</summary>
        public const double MinTurnRate = 0.5;
        public const double MaxTurnRate = 10;
        public const double MinVerticalRate = 100;
        public const double MaxVerticalRate = 6000;
        public const double MaxSpeed = 600;
        public const double MaxAltitude = 60000;
        /// <summary>Rounding allowance: a value this close to its target in the last step is taken as reached.</summary>
        private const double Epsilon = 1e-9;

        private readonly object sync = new();
        private readonly List<SimAircraft> aircraft = [];
        private readonly List<(string Callsign, Action<SimAircraft> Change)> pending = [];
        private long steps;
        private double accumulated;
        private int number;
        private string prefix = "SIM";
        private bool paused;

        /// <summary>Time of the simulation (s since the start): a whole number of steps.</summary>
        public double Time
        {
            get { lock (sync) return steps * Step; }
        }

        /// <summary>Paused: nothing moves; commands are still taken (carried out at the next <see cref="Advance"/>).</summary>
        public bool Paused
        {
            get { lock (sync) return paused; }
            set { lock (sync) paused = value; }
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
                aircraft.Add(new SimAircraft
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
                });
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
                // Commands still queued for the old name follow the aircraft.
                for (int i = 0; i < pending.Count; i++)
                {
                    if (string.Equals(pending[i].Callsign, callsign, StringComparison.OrdinalIgnoreCase))
                    {
                        pending[i] = (name, pending[i].Change);
                    }
                }
                return true;
            }
        }

        /// <summary>Heading to fly (degrees true), turning on this side.</summary>
        public void SetHeading(string callsign, double headingTrue, TurnDirection direction = TurnDirection.Shortest) =>
            Command(callsign, a => { a.TargetHeading = Normalize(headingTrue); a.TurnDirection = direction; });

        /// <summary>Turn rate used to reach the commanded heading (°/s).</summary>
        public void SetTurnRate(string callsign, double degreesPerSecond) =>
            Command(callsign, a => a.SelectedTurnRateDps = Math.Clamp(degreesPerSecond, MinTurnRate, MaxTurnRate));

        /// <summary>Altitude to reach (ft above mean sea level), optionally with a new vertical rate (ft/min).</summary>
        public void SetAltitude(string callsign, double altitudeFt, double? verticalRateFpm = null) =>
            Command(callsign, a =>
            {
                a.TargetAltitudeFt = Math.Clamp(altitudeFt, 0, MaxAltitude);
                if (verticalRateFpm is double rate) a.SelectedVerticalRateFpm = Math.Clamp(Math.Abs(rate), MinVerticalRate, MaxVerticalRate);
            });

        /// <summary>Vertical rate used to reach the commanded altitude (ft/min).</summary>
        public void SetVerticalRate(string callsign, double verticalRateFpm) =>
            Command(callsign, a => a.SelectedVerticalRateFpm = Math.Clamp(Math.Abs(verticalRateFpm), MinVerticalRate, MaxVerticalRate));

        /// <summary>Speed to reach (kt, true airspeed).</summary>
        public void SetSpeed(string callsign, double speedKt) =>
            Command(callsign, a => a.TargetSpeedKt = Math.Clamp(speedKt, 0, MaxSpeed));

        /// <summary>Holds an aircraft where it is (true) or lets it go on (false). At once, not a pilot instruction.</summary>
        public void SetFrozen(string callsign, bool frozen)
        {
            lock (sync)
            {
                SimAircraft? plane = Find(callsign);
                if (plane != null) plane.Frozen = frozen;
            }
        }

        public void Remove(string callsign)
        {
            lock (sync)
            {
                aircraft.RemoveAll(a => string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
                pending.RemoveAll(p => string.Equals(p.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
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
        /// call. At most <see cref="MaxCatchUp"/> s at a time. Paused: only the queued commands are carried out.
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
            lock (sync) return aircraft.Select(a => a.ToState()).ToList();
        }

        /// <summary>Copy of one aircraft now, or null.</summary>
        public AircraftState? Get(string callsign)
        {
            lock (sync) return Find(callsign)?.ToState();
        }

        /// <summary>Angle in 0–360.</summary>
        public static double Normalize(double degrees) => (degrees % 360 + 360) % 360;

        private void Command(string callsign, Action<SimAircraft> change)
        {
            lock (sync) pending.Add((callsign, change));
        }

        private SimAircraft? Find(string callsign) =>
            aircraft.FirstOrDefault(a => string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));

        private static string CheckCallsign(string callsign)
        {
            string text = (callsign ?? "").Trim().ToUpperInvariant();
            if (text.Length is < 2 or > 10 || !text.All(char.IsAsciiLetterOrDigit))
                throw new ArgumentException("A callsign is 2 to 10 letters or digits.", nameof(callsign));
            return text;
        }

        private void ApplyPending()
        {
            foreach ((string callsign, Action<SimAircraft> change) in pending)
            {
                SimAircraft? plane = Find(callsign);
                if (plane != null) change(plane);
            }
            pending.Clear();
        }

        private void StepOnce()
        {
            ApplyPending();
            steps++;
            foreach (SimAircraft plane in aircraft)
            {
                if (plane.Frozen)
                {
                    plane.TurnRateDps = 0;
                    plane.VerticalSpeedFpm = 0;
                    continue;
                }
                Turn(plane);
                Climb(plane);
                Accelerate(plane);
                double heading = plane.HeadingTrue * Math.PI / 180;
                plane.X += plane.SpeedKt * Math.Sin(heading) * Step / 3600;
                plane.Y += plane.SpeedKt * Math.Cos(heading) * Step / 3600;
            }
        }

        private static void Turn(SimAircraft plane)
        {
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
            if (remaining <= turn + Epsilon)
            {
                plane.HeadingTrue = plane.TargetHeading;
                plane.TurnRateDps = 0;
                plane.TurnDirection = TurnDirection.Shortest;
                return;
            }
            plane.HeadingTrue = Normalize(plane.HeadingTrue + side * turn);
            plane.TurnRateDps = side * plane.SelectedTurnRateDps;
        }

        private static void Climb(SimAircraft plane)
        {
            double difference = plane.TargetAltitudeFt - plane.AltitudeFt;
            double change = plane.SelectedVerticalRateFpm / 60 * Step;
            if (Math.Abs(difference) <= change + Epsilon)
            {
                plane.AltitudeFt = plane.TargetAltitudeFt;
                plane.VerticalSpeedFpm = 0;
                return;
            }
            plane.VerticalSpeedFpm = Math.Sign(difference) * plane.SelectedVerticalRateFpm;
            plane.AltitudeFt += Math.Sign(difference) * change;
        }

        private static void Accelerate(SimAircraft plane)
        {
            double difference = plane.TargetSpeedKt - plane.SpeedKt;
            double change = (difference > 0 ? Acceleration : Deceleration) * Step;
            plane.SpeedKt = Math.Abs(difference) <= change + Epsilon ? plane.TargetSpeedKt : plane.SpeedKt + Math.Sign(difference) * change;
        }
    }
}
