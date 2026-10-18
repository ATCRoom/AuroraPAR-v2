using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>Settings of the Instructor (the same for all profiles).</summary>
    internal sealed class InstructorSettings
    {
        /// <summary>The Instructor is turned on in the Settings: only then key I and the Instructor button.</summary>
        public bool Enabled { get; set; }
        /// <summary>Start of the callsigns given automatically (SIM → SIM1, SIM2...).</summary>
        public string CallsignPrefix { get; set; } = "SIM";
        /// <summary>Positions as Aurora gives them (every about 0.5 s, altitude in steps), so the student sees realistic data.</summary>
        public bool AuroraLike { get; set; } = true;
        /// <summary>Pilot reaction (s, 0–5): instructions are carried out this long after they are given.</summary>
        public double ReactionSeconds { get; set; } = 1;
        /// <summary>Last position and size of the Instructor window.</summary>
        public WindowPlacement? Window { get; set; }

        public void Normalize()
        {
            string prefix = (CallsignPrefix ?? "").Trim().ToUpperInvariant();
            CallsignPrefix = prefix.Length is >= 1 and <= 6 && prefix.All(char.IsAsciiLetterOrDigit) ? prefix : "SIM";
            ReactionSeconds = double.IsFinite(ReactionSeconds) ? Math.Clamp(ReactionSeconds, 0, SimEngine.MaxReaction) : 1;
        }
    }

    /// <summary>
    /// Link between the simulation engine (<see cref="SimEngine"/>, ATCRoom Sim) and the radar: it keeps the
    /// reference runway of the Instructor, moves the engine on with the wall clock and gives the simulated aircraft
    /// to the radar as traffic (latitude, longitude, altitude, track, ground speed), as Aurora would.
    ///
    /// The engine works in a flat plane centred on the threshold of the reference runway (<see cref="LocalPlane"/>,
    /// on the radar's own sphere): an aircraft on the extended centreline is exactly on the centreline of the PAR
    /// while the student uses the same runway with the same course. The reference runway is a copy with the heading
    /// of the runway file: if the student changes runway or course, the simulated aircraft stay where they are.
    ///
    /// <see cref="Snapshot"/> runs on the radar's data timer, the window on the user interface: the engine has its
    /// own lock, the bridge's own state (reference, Aurora-like positions) is under <c>sync</c>.
    /// </summary>
    internal sealed class InstructorBridge
    {
        /// <summary>Step used to measure the track at a point (NM).</summary>
        private const double TrackProbe = 0.01;

        private readonly object sync = new();
        private readonly Random random = new();
        private readonly Dictionary<string, Given> given = new(StringComparer.OrdinalIgnoreCase);
        private Runway? reference;
        private LocalPlane? plane;
        /// <summary>Wall clock of the simulation (not changed by a change of the computer's clock) and its last reading (s).</summary>
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private double lastAdvance = double.NaN;
        private Dictionary<string, Aircraft> lastPositions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Last position given to Aurora's rhythm: position every 0.4–0.65 s, altitude every 3 s in steps of 10 ft.</summary>
        private sealed class Given
        {
            public double Latitude = double.NaN;
            public double Longitude = double.NaN;
            public double Altitude = double.NaN;
            public double Track;
            public double Speed;
            public DateTime PositionTime;
            public DateTime AltitudeTime;
        }

        public SimEngine Engine { get; } = new();

        /// <summary>
        /// Reads the performance classes from performance.json in this folder (written with the defaults when
        /// missing). Returns a line for the instructor when something was not a plain read, else null.
        /// </summary>
        public string? LoadPerformance(string folder)
        {
            (List<PerformanceClass> classes, _, string? message) = PerformanceFile.Load(System.IO.Path.Combine(folder, PerformanceFile.FileName));
            Engine.SetPerformanceClasses(classes);
            return message;
        }

        /// <summary>Positions as Aurora gives them (see <see cref="InstructorSettings.AuroraLike"/>).</summary>
        public bool AuroraLike { get; set; } = true;

        /// <summary>The reference runway (copy, heading of the runway file), or null before the Instructor is opened.</summary>
        public Runway? Reference
        {
            get { lock (sync) return reference; }
        }

        /// <summary>Simulated aircraft flying now.</summary>
        public int Count => Engine.Count;

        /// <summary>
        /// Takes this runway as the reference (a copy with the heading of the runway file). The simulated aircraft are
        /// removed: their positions belong to the old reference.
        /// </summary>
        public void SetReference(Runway runway)
        {
            Runway copy = runway.CopyWithFileHeading();
            lock (sync)
            {
                Engine.Clear();
                given.Clear();
                lastPositions = new(StringComparer.OrdinalIgnoreCase);
                reference = copy;
                plane = new LocalPlane(copy.Latitude, copy.Longitude, copy.Heading);
                // The "Normal" rate follows the glide path of this runway.
                Engine.SetGlidePath(copy.Heading, copy.GlideSlope, copy.Elevation, copy.LengthNM);
            }
        }

        /// <summary>Point of the plane on the extended centreline, at this distance from touchdown (NM), offset to the pilot's right (NM, negative left).</summary>
        public (double X, double Y) OnFinal(double distanceFromTouchdownNM, double rightNM = 0)
        {
            Runway runway = Reference ?? throw new InvalidOperationException("No reference runway.");
            // From the threshold: back along the approach (opposite of the runway heading), then to the pilot's right.
            double fromThreshold = distanceFromTouchdownNM - runway.TouchdownNM;
            double back = (runway.Heading + 180) * Math.PI / 180;
            double right = (runway.Heading + 90) * Math.PI / 180;
            return (fromThreshold * Math.Sin(back) + rightNM * Math.Sin(right), fromThreshold * Math.Cos(back) + rightNM * Math.Cos(right));
        }

        /// <summary>
        /// Adds an aircraft (see <see cref="SimEngine.Add"/>). A callsign that a real or test aircraft already has
        /// (<paramref name="taken"/>) gets a letter (SIM1 → SIM1A), once, here: the radar's filters and track IDs
        /// follow the callsign, so it never changes while the aircraft flies.
        /// </summary>
        public string Add(double x, double y, double altitudeFt, double headingTrue, double speedKt, string? callsign, IEnumerable<string> taken, string? performanceClass = null)
        {
            HashSet<string> others = new(taken, StringComparer.OrdinalIgnoreCase);
            // The callsign is chosen before the aircraft exists, so it never shows under a callsign that is already flying.
            return Engine.Add(x, y, altitudeFt, headingTrue, speedKt, callsign, performanceClass, others.Contains);
        }

        /// <summary>Removes all simulated aircraft.</summary>
        public void Clear()
        {
            lock (sync)
            {
                Engine.Clear();
                given.Clear();
                lastPositions = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>The last position given to the radar of an aircraft (for "seen by the PAR"), or null.</summary>
        public Aircraft? LastPosition(string callsign)
        {
            lock (sync) return lastPositions.TryGetValue(callsign, out Aircraft? aircraft) ? aircraft.Copy() : null;
        }

        /// <summary>
        /// Moves the simulation on to <paramref name="now"/> (wall clock, UTC) and gives the simulated aircraft as
        /// radar traffic. Called by the radar's data timer, about every 100 ms (more when Aurora is slow: the
        /// engine makes up the time in its fixed steps).
        /// </summary>
        public List<Aircraft> Snapshot(DateTime now)
        {
            lock (sync)
            {
                double elapsed = clock.Elapsed.TotalSeconds;
                double seconds = double.IsNaN(lastAdvance) ? 0 : elapsed - lastAdvance;
                lastAdvance = elapsed;
                Engine.Advance(seconds);
                List<Aircraft> result = [];
                if (plane == null || reference == null) return result;
                Dictionary<string, Aircraft> positions = new(StringComparer.OrdinalIgnoreCase);
                foreach (AircraftState state in Engine.Snapshot())
                {
                    Aircraft aircraft = ToAircraft(state, plane, now);
                    result.Add(aircraft);
                    positions[state.Callsign] = aircraft;
                }
                // Forget the Aurora-like positions of aircraft that are gone.
                foreach (string callsign in given.Keys.Where(k => !positions.ContainsKey(k)).ToList()) given.Remove(callsign);
                lastPositions = positions;
                return result;
            }
        }

        private Aircraft ToAircraft(AircraftState state, LocalPlane plane, DateTime now)
        {
            (double latitude, double longitude) = plane.ToGeo(state.X, state.Y);
            // Track on the Earth: the direction of flight in the plane, measured at the aircraft (away from the
            // threshold the plane's north is not exactly true north).
            double direction = state.TrackTrue * Math.PI / 180;
            (double aheadLatitude, double aheadLongitude) = plane.ToGeo(state.X + TrackProbe * Math.Sin(direction), state.Y + TrackProbe * Math.Cos(direction));
            double track = Earth.Bearing(latitude, longitude, aheadLatitude, aheadLongitude);
            double altitude = state.AltitudeFt;
            double speed = state.GroundSpeedKt;
            if (AuroraLike)
            {
                if (!given.TryGetValue(state.Callsign, out Given? last))
                {
                    last = new Given();
                    given[state.Callsign] = last;
                }
                // As Aurora: a new position every 0.4–0.65 s, a new altitude every 3 s; in between the last ones.
                if (double.IsNaN(last.Latitude) || now >= last.PositionTime)
                {
                    last.Latitude = latitude;
                    last.Longitude = longitude;
                    // Aurora gives track and ground speed with the position.
                    last.Track = track;
                    last.Speed = speed;
                    last.PositionTime = now.AddSeconds(0.4 + 0.25 * random.NextDouble());
                }
                if (double.IsNaN(last.Altitude) || now >= last.AltitudeTime)
                {
                    last.Altitude = Math.Round(altitude / 10) * 10;
                    last.AltitudeTime = now.AddSeconds(3);
                }
                latitude = last.Latitude;
                longitude = last.Longitude;
                track = last.Track;
                speed = last.Speed;
                altitude = last.Altitude;
            }
            else
            {
                given.Remove(state.Callsign);
            }
            return new Aircraft
            {
                Callsign = state.Callsign,
                Latitude = latitude,
                Longitude = longitude,
                Altitude = altitude,
                Track = track,
                // Over the ground, with the wind (as Aurora gives it).
                Speed = speed,
                IsTest = true,
                IsSim = true
            };
        }
    }
}
