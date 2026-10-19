namespace AuroraPAR
{
    /// <summary>How the test aircraft turn with the joystick (<see cref="TestTraffic.TurnMode"/>).</summary>
    internal enum TestTurnMode
    {
        /// <summary>Half rate: 1.5°/s at full stick.</summary>
        Half,
        /// <summary>Rate one: 3°/s at full stick.</summary>
        Standard,
        /// <summary>The longer the stick is held at full deflection, the faster the turn.</summary>
        Free
    }

    /// <summary>
    /// Test traffic: virtual aircraft for testing the radar without waiting for real traffic (also without Aurora).
    ///
    /// Each aircraft has a heading (relative to the final course of the runway in use), a vertical speed and a speed,
    /// and keeps them until changed, as a real one: the joystick of <see cref="TestTrafficWindow"/> turns it (at the
    /// selected turn rate) and changes its vertical speed; released, heading and vertical speed stay. Buttons turn it
    /// to a heading (or back to the final course) or set the vertical speed of the glide path (the "best" one for its
    /// ground speed), or move the vertical speed in steps aligned on that best value. In Auto it intercepts the
    /// centreline and the glide path by itself.
    ///
    /// Wind (rough model, the same at all heights, optional gusts): the aircraft flies its heading through the air,
    /// so a crosswind makes it drift off the centreline unless the heading is corrected, and a headwind lowers the
    /// ground speed and so the rate of descent needed on the glide path.
    ///
    /// Positions are given as if they came from Aurora (merged into the traffic list), optionally with Aurora's
    /// irregular rhythm (positions every 0.5 s, altitude in steps) to test the track smoothing.
    /// Used from the UI thread (window) and from the data timer: everything is under a lock.
    /// </summary>
    internal sealed class TestTraffic
    {
        public sealed class Plane
        {
            public string Callsign = "";
            /// <summary>Distance from the touchdown point along the centreline (NM).</summary>
            public double Distance;
            /// <summary>Lateral offset from the centreline (NM, positive right as seen by the pilot).</summary>
            public double Lateral;
            /// <summary>Height above the threshold (ft).</summary>
            public double Height;
            public double Speed = 140;
            public string? Squawk;
            /// <summary>Heading relative to the final course (degrees, −180..180, positive to the right).</summary>
            public double Heading;
            /// <summary>Vertical speed (ft/min, negative descending).</summary>
            public double VerticalSpeed;
            /// <summary>Turn rate now (°/s, positive to the right).</summary>
            public double TurnRate;
            /// <summary>Keeps the vertical speed of the glide path (Optimal GP), also when the speed or course change.</summary>
            public bool HoldGlidePath = true;
            /// <summary>Heading to turn to (relative to the final course; NaN: none) and the side (−1 left, 1 right, 0 shortest).</summary>
            public double TargetHeading = double.NaN;
            public int TargetTurn;
            /// <summary>Intercepts the centreline and the glide path by itself.</summary>
            public bool Auto;
            /// <summary>Joystick deflection (−1..1, X right, Y up) and for how long X has been at its edge (Free).</summary>
            public double StickX;
            public double StickY;
            public double StickHeld;
            /// <summary>For the window: vertical speed of the glide path for this aircraft now, and the GP angle.</summary>
            public double BestVerticalSpeed;
            public double GlideSlope = 3;
            /// <summary>For the window: true heading, true final course, drift (track − heading), ground speed, and the
            /// heading (relative to the final course) that holds the final course with this wind.</summary>
            public double HeadingTrue;
            public double FinalTrue;
            public double Drift;
            public double GroundSpeed;
            public double Track;
            public double CrabHeading;
            // Aurora-like data: last positions given and when.
            public double GivenLatitude = double.NaN;
            public double GivenLongitude = double.NaN;
            public double GivenAltitude = double.NaN;
            public DateTime GivenPositionTime;
            public DateTime GivenAltitudeTime;

            /// <summary>Started level (below the glide path, on an intercept heading): the height offset is measured at <see cref="AimDistance"/>.</summary>
            public bool StartLevel;
            public double AimDistance;
            /// <summary>Start at this height above the threshold (ft), level (NaN: on the glide path plus the height offset).</summary>
            public double StartHeight = double.NaN;
            /// <summary>Level below the glide path: NM and seconds left before it meets the glide path (NaN: not applicable).</summary>
            public double GpRemainingNM = double.NaN;
            public double GpSeconds = double.NaN;

            public Plane Copy() => (Plane)MemberwiseClone();
        }

        /// <summary>Distance from the centreline of an intercept start (NM), at most; less when the radar's beam is narrower there.</summary>
        public const double DefaultStartOffset = 3;

        /// <summary>Vertical speed limits (ft/min) and how fast the full stick changes it (ft/min per second).</summary>
        public const double MaxClimb = 2000;
        public const double MaxDescent = -3000;
        private const double StickVerticalRate = 600;
        /// <summary>Step of the vertical speed buttons (ft/min), aligned on the best vertical speed.</summary>
        public const double VerticalStep = 100;
        /// <summary>Free turn: rate at full stick at once, growth per second held at the edge, maximum (°/s).</summary>
        private const double FreeStartRate = 1.5;
        private const double FreeGrowth = 1.5;
        private const double FreeMaxRate = 10;
        /// <summary>Feet per minute of vertical speed for each knot along the track and each unit of tan(GP).</summary>
        private const double FeetPerMinutePerKnot = Runway.FeetPerNM / 60;

        private readonly object sync = new();
        private readonly List<Plane> planes = [];
        private readonly Random random = new();
        private DateTime lastTime = DateTime.MinValue;
        private int number;

        public bool Paused { get; set; }
        /// <summary>Positions every about 0.5 s (a little irregular) and the altitude every 3 s, as with Aurora.</summary>
        public bool AuroraLike { get; set; }
        public TestTurnMode TurnMode { get; set; } = TestTurnMode.Standard;
        /// <summary>Wind: direction it blows from (degrees true), speed and gusts above it (kt).</summary>
        public double WindFrom { get; set; }
        public double WindSpeed { get; set; }
        public double WindGust { get; set; }
        // Gusts: the extra wind now, the one it moves to, and when a new one is drawn.
        private double gustNow;
        private double gustTarget;
        private DateTime gustNext = DateTime.MinValue;

        /// <summary>Angle in −180..180.</summary>
        public static double Wrap(double degrees) => ((degrees + 180) % 360 + 360) % 360 - 180;

        public int Count
        {
            get { lock (sync) return planes.Count; }
        }

        /// <summary>Copies of the aircraft, for the window.</summary>
        public List<Plane> List()
        {
            lock (sync) return planes.Select(p => p.Copy()).ToList();
        }

        /// <summary>
        /// Adds an aircraft on the final course, at a lateral offset (m, + right) and a height offset from the glide
        /// path (ft, + above), descending at the glide path rate; returns its callsign. The height is set at the
        /// next <see cref="Snapshot"/> (it needs the runway).
        /// </summary>
        public string Add(double distanceNM, double speedKt, string? squawk, double lateralM, double heightOffsetFt, double interceptDegrees = 0, int side = 0,
            double startHeightFt = double.NaN, double startOffsetNM = DefaultStartOffset)
        {
            lock (sync)
            {
                // The first one after a pause of the list: no jump for the time with no test traffic.
                if (planes.Count == 0) lastTime = DateTime.MinValue;
                number++;
                Plane plane = new()
                {
                    Callsign = $"TEST{number}",
                    Distance = distanceNM,
                    Lateral = lateralM / 1852,
                    Height = double.NaN,
                    VerticalSpeed = heightOffsetFt,
                    Speed = speedKt,
                    Squawk = squawk
                };
                if (interceptDegrees > 0 && side != 0)
                {
                    // Aimed at the point of the centreline at distanceNM, with this intercept angle: the start is back along
                    // that track, startOffsetNM from the centreline (left: side −1, right: +1); level, below the glide path.
                    double offset = double.IsFinite(startOffsetNM) && startOffsetNM > 0 ? startOffsetNM : DefaultStartOffset;
                    double angle = interceptDegrees * Math.PI / 180;
                    double back = offset / Math.Sin(angle);
                    plane.AimDistance = distanceNM;
                    plane.Distance = distanceNM + back * Math.Cos(angle);
                    plane.Lateral = side * offset;
                    plane.Heading = -side * interceptDegrees;
                    plane.StartLevel = true;
                    plane.HoldGlidePath = false;
                }
                if (double.IsFinite(startHeightFt))
                {
                    // A height chosen by the instructor: level there; Normal (or a rate) starts the descent.
                    plane.StartHeight = Math.Max(0, startHeightFt);
                    plane.HoldGlidePath = false;
                }
                planes.Add(plane);
                return plane.Callsign;
            }
        }

        /// <summary>Changes an aircraft (under the lock).</summary>
        public void Change(string callsign, Action<Plane> change)
        {
            lock (sync)
            {
                Plane? plane = planes.FirstOrDefault(p => p.Callsign == callsign);
                if (plane != null) change(plane);
            }
        }

        public void Remove(string callsign)
        {
            lock (sync) planes.RemoveAll(p => p.Callsign == callsign);
        }

        public void Clear()
        {
            lock (sync) planes.Clear();
        }

        /// <summary>Next vertical speed of the step buttons: on the grid of <see cref="VerticalStep"/> through the best one.</summary>
        public static double StepVerticalSpeed(double current, double best, bool up)
        {
            double k = (current - best) / VerticalStep;
            double next = up ? Math.Floor(k + 1e-6) + 1 : Math.Ceiling(k - 1e-6) - 1;
            return Math.Clamp(best + next * VerticalStep, MaxDescent, MaxClimb);
        }

        /// <summary>Vertical speed that keeps the glide path at this heading and wind (ft/min, negative): from the ground speed along the centreline.</summary>
        private static double BestVerticalSpeed(Plane plane, Runway runway, double windAlong)
        {
            double alongKt = Math.Max(0, plane.Speed * Math.Cos(plane.Heading * Math.PI / 180) + windAlong);
            return -alongKt * FeetPerMinutePerKnot * Math.Tan(runway.GlideSlope * Math.PI / 180);
        }

        /// <summary>
        /// Moves the aircraft on to <paramref name="now"/> and gives them as Aurora traffic for the runway in use.
        /// Aircraft beyond the touchdown point are removed (landed).
        /// </summary>
        public List<Aircraft> Snapshot(Runway runway, DateTime now)
        {
            lock (sync)
            {
                double seconds = lastTime == DateTime.MinValue ? 0 : Math.Clamp((now - lastTime).TotalSeconds, 0, 1);
                lastTime = now;
                if (Paused) seconds = 0;
                // Wind towards the final course (along, + towards touchdown) and to its right; gusts drawn every few seconds.
                if (WindGust > 0)
                {
                    if (now >= gustNext)
                    {
                        gustTarget = random.NextDouble() * WindGust;
                        gustNext = now.AddSeconds(2 + 3 * random.NextDouble());
                    }
                    gustNow += (gustTarget - gustNow) * Math.Min(1, seconds);
                }
                else
                {
                    gustNow = 0;
                }
                double wind = Math.Max(0, WindSpeed + gustNow);
                double towards = (WindFrom + 180 - runway.Heading) * Math.PI / 180;
                double windAlong = wind * Math.Cos(towards);
                double windRight = wind * Math.Sin(towards);
                List<Aircraft> result = [];
                foreach (Plane plane in planes.ToList())
                {
                    // New aircraft: on the glide path plus its height offset (kept in VerticalSpeed by Add).
                    if (double.IsNaN(plane.Height))
                    {
                        if (!double.IsNaN(plane.StartHeight))
                        {
                            plane.Height = plane.StartHeight;
                            plane.VerticalSpeed = 0;
                        }
                        else
                        {
                            plane.Height = Math.Max(0, runway.GlidePathHeight(plane.StartLevel ? plane.AimDistance : plane.Distance) + plane.VerticalSpeed);
                            plane.VerticalSpeed = plane.StartLevel ? 0 : BestVerticalSpeed(plane, runway, windAlong);
                        }
                    }
                    Fly(plane, runway, seconds, windAlong, windRight);
                    // Landed (past the touchdown point near the centreline), or far away: removed.
                    bool landed = plane.Distance < 0 && Math.Abs(plane.Lateral) < 0.3 && plane.Height < 100;
                    if (landed || plane.Distance < -10 || plane.Distance > 80 || Math.Abs(plane.Lateral) > 40)
                    {
                        planes.Remove(plane);
                        continue;
                    }
                    result.Add(ToAircraft(plane, runway, now));
                }
                return result;
            }
        }

        private void Fly(Plane plane, Runway runway, double seconds, double windAlong, double windRight)
        {
            // Heading that holds the final course with this crosswind (wind correction angle into the wind).
            plane.CrabHeading = -Math.Asin(Math.Clamp(windRight / Math.Max(plane.Speed, 1), -0.9, 0.9)) * 180 / Math.PI;
            // Turn.
            double stickRate = TurnMode switch
            {
                TestTurnMode.Half => 1.5,
                TestTurnMode.Free => Math.Min(FreeMaxRate, FreeStartRate + FreeGrowth * plane.StickHeld),
                _ => 3
            };
            if (Math.Abs(plane.StickX) > 0.95) plane.StickHeld += seconds; else plane.StickHeld = 0;
            if (plane.StickX != 0)
            {
                plane.TurnRate = plane.StickX * stickRate;
                plane.TargetHeading = double.NaN;
            }
            else if (plane.Auto)
            {
                // Intercept the centreline: a track towards it, the closer the smaller (at most 20°), with the heading
                // corrected for the crosswind.
                double target = Math.Clamp(-plane.Lateral * 150, -20, 20) + plane.CrabHeading;
                plane.TurnRate = Math.Clamp(Wrap(target - plane.Heading) / 2, -3, 3);
            }
            else if (!double.IsNaN(plane.TargetHeading))
            {
                // Turning to a heading (or back to the final course) at the turn rate chosen (3°/s with Free).
                double rate = TurnMode == TestTurnMode.Half ? 1.5 : 3;
                double difference = Wrap(plane.TargetHeading - plane.Heading);
                int side = plane.TargetTurn != 0 ? plane.TargetTurn : Math.Sign(difference);
                // Degrees still to turn on that side.
                double remaining = side > 0 ? (difference + 360) % 360 : (-difference + 360) % 360;
                // Reached (also a heading within a degree on the other side: no full circle for a rounded readout).
                if (side == 0 || remaining <= rate * seconds + 1e-6 || remaining > 359)
                {
                    if (seconds > 0 || side == 0)
                    {
                        plane.Heading = Wrap(plane.TargetHeading);
                        plane.TargetHeading = double.NaN;
                    }
                    plane.TurnRate = 0;
                }
                else
                {
                    plane.TurnRate = side * rate;
                }
            }
            else
            {
                // Stick released: the heading reached stays.
                plane.TurnRate = 0;
            }
            plane.Heading = Wrap(plane.Heading + plane.TurnRate * seconds);

            // Vertical speed.
            double best = BestVerticalSpeed(plane, runway, windAlong);
            plane.BestVerticalSpeed = best;
            plane.GlideSlope = runway.GlideSlope;
            if (plane.StickY != 0)
            {
                plane.VerticalSpeed = Math.Clamp(plane.VerticalSpeed + plane.StickY * StickVerticalRate * seconds, MaxDescent, MaxClimb);
            }
            else if (plane.Auto)
            {
                // Intercept the glide path: the best vertical speed, corrected by the height off the glide path.
                double off = plane.Height - runway.GlidePathHeight(plane.Distance);
                plane.VerticalSpeed = Math.Clamp(best - Math.Clamp(off * 3, -500, 500), MaxDescent, MaxClimb);
            }
            else if (plane.HoldGlidePath)
            {
                plane.VerticalSpeed = best;
            }

            // Move: the speed along the heading plus the wind.
            double heading = plane.Heading * Math.PI / 180;
            double alongKt = plane.Speed * Math.Cos(heading) + windAlong;
            double rightKt = plane.Speed * Math.Sin(heading) + windRight;
            plane.Distance -= alongKt * seconds / 3600;
            plane.Lateral += rightKt * seconds / 3600;
            plane.GroundSpeed = Math.Sqrt(alongKt * alongKt + rightKt * rightKt);
            plane.Track = plane.GroundSpeed > 0.5 ? Math.Atan2(rightKt, alongKt) * 180 / Math.PI : plane.Heading;
            plane.Drift = Wrap(plane.Track - plane.Heading);
            plane.FinalTrue = runway.Heading;
            plane.HeadingTrue = (runway.Heading + plane.Heading + 720) % 360;
            plane.Height = Math.Max(0, plane.Height + plane.VerticalSpeed * seconds / 60);
            // Level below the glide path: where it will meet it, to choose the moment of the descent instruction.
            double perNM = runway.GlidePathHeight(1);
            if (!plane.HoldGlidePath && Math.Abs(plane.VerticalSpeed) < 50 && perNM > 0 && alongKt > 1 && plane.Height < runway.GlidePathHeight(plane.Distance) - 10)
            {
                plane.GpRemainingNM = plane.Distance - plane.Height / perNM;
                plane.GpSeconds = plane.GpRemainingNM / alongKt * 3600;
            }
            else
            {
                plane.GpRemainingNM = plane.GpSeconds = double.NaN;
            }
        }

        private Aircraft ToAircraft(Plane plane, Runway runway, DateTime now)
        {
            // From the threshold: back along the approach (opposite of the runway heading), then to the pilot's right.
            double along = plane.Distance - runway.TouchdownNM;
            // On the same Earth as the radar (Earth.Radius): on the centreline the radar shows exactly the distance given.
            double radius = Earth.Radius(runway);
            (double onLine, double onLineLon) = Earth.Destination(runway.Latitude, runway.Longitude, runway.Heading + 180, along, radius);
            // Direction of the centreline at that point (it turns a little away from the threshold: meridian convergence),
            // so the lateral step is square to it and the track is relative to it.
            double toThreshold = Earth.Bearing(onLine, onLineLon, runway.Latitude, runway.Longitude);
            double course = along > 0.001 ? toThreshold : along < -0.001 ? toThreshold + 180 : runway.Heading;
            (double latitude, double longitude) = Earth.Destination(onLine, onLineLon, course + 90, plane.Lateral, radius);
            double altitude = runway.Elevation + plane.Height;
            double track = (course + plane.Track + 720) % 360;
            if (AuroraLike)
            {
                // As Aurora: a new position every 0.4–0.65 s, a new altitude every 3 s; in between the last ones.
                if (double.IsNaN(plane.GivenLatitude) || now >= plane.GivenPositionTime)
                {
                    plane.GivenLatitude = latitude;
                    plane.GivenLongitude = longitude;
                    plane.GivenPositionTime = now.AddSeconds(0.4 + 0.25 * random.NextDouble());
                }
                if (double.IsNaN(plane.GivenAltitude) || now >= plane.GivenAltitudeTime)
                {
                    plane.GivenAltitude = Math.Round(altitude / 10) * 10;
                    plane.GivenAltitudeTime = now.AddSeconds(3);
                }
                latitude = plane.GivenLatitude;
                longitude = plane.GivenLongitude;
                altitude = plane.GivenAltitude;
            }
            else
            {
                plane.GivenLatitude = plane.GivenLongitude = plane.GivenAltitude = double.NaN;
            }
            return new Aircraft
            {
                Callsign = plane.Callsign,
                Latitude = latitude,
                Longitude = longitude,
                Altitude = altitude,
                Track = track,
                Speed = plane.GroundSpeed,
                Squawk = plane.Squawk,
                IsTest = true
            };
        }
    }
}
