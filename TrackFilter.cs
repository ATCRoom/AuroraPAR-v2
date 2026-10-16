namespace AuroraPAR
{
    /// <summary>How much the tracks of the modern display are smoothed (see <see cref="TrackFilter"/>).</summary>
    internal enum TrackSmoothing
    {
        Off,
        /// <summary>On (the name is kept for the saved profiles).</summary>
        Light,
        /// <summary>No longer offered (16 Oct: little difference from Light); profiles with it get Light.</summary>
        Strong
    }

    /// <summary>
    /// Track filter of the modern display, as the computer of a radar: the tracks move smoothly instead of jumping
    /// at every position received from Aurora (and the altitude without its steps).
    ///
    /// For each aircraft the filter keeps an estimated position and moves it on at every redraw with the ground
    /// speed and track given by Aurora and the vertical speed (as the coasting track). When Aurora gives a new
    /// position, the estimate is pulled towards it by a part of the difference (alpha filter): the larger the part,
    /// the closer and the less smooth the track. So the track stays on the real aircraft and moves smoothly.
    ///
    /// The history dots (plots) and the beam check use the real positions; only the symbol and the label of the
    /// track use the estimate (<see cref="Aircraft.Filtered"/>).
    /// </summary>
    internal sealed class TrackFilter
    {
        /// <summary>The estimate is not moved on longer than this without a new position (data stopped).</summary>
        private const double MaxPredictSeconds = 6;
        /// <summary>Beyond this difference (NM, ft) the estimate jumps to the real position (new or teleported aircraft).</summary>
        private const double ResetDistanceNM = 1;
        private const double ResetAltitudeFt = 1500;

        private sealed class State
        {
            public double Latitude;
            public double Longitude;
            public double Altitude;
            /// <summary>Time of the estimate above.</summary>
            public DateTime Time;
            /// <summary>When the last new position was received.</summary>
            public DateTime RawTime;
            /// <summary>Last position received, to tell a new one.</summary>
            public double RawLatitude = double.NaN;
            public double RawLongitude = double.NaN;
            public double RawAltitude = double.NaN;
            /// <summary>
            /// Correction still owed (NM) because the estimate is ahead of the real aircraft along its track (alpha of
            /// the gap at the last position): never made by moving back, but by moving on more slowly.
            /// </summary>
            public double Ahead;
        }

        private readonly Dictionary<string, State> states = [];

        /// <summary>
        /// Sets <see cref="Aircraft.Filtered"/> of each aircraft to its estimated position at <paramref name="now"/>
        /// (null with <see cref="TrackSmoothing.Off"/>). Call at every redraw with the last positions received.
        /// </summary>
        public void Apply(IReadOnlyList<Aircraft> aircrafts, DateTime now, TrackSmoothing smoothing)
        {
            if (smoothing == TrackSmoothing.Off)
            {
                foreach (Aircraft aircraft in aircrafts) aircraft.Filtered = null;
                states.Clear();
                return;
            }
            // Part of the difference corrected at each new position: horizontal (Aurora sends a position every
            // 0.5 s) and altitude (it changes in steps, so it is corrected less).
            (double alpha, double altitudeAlpha) = smoothing == TrackSmoothing.Strong ? (0.25, 0.10) : (0.5, 0.25);
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                present.Add(aircraft.Callsign);
                if (!states.TryGetValue(aircraft.Callsign, out State? state))
                {
                    state = Reset(new State(), aircraft, now);
                    states[aircraft.Callsign] = state;
                }
                else
                {
                    Predict(state, aircraft, now);
                    bool newPosition = aircraft.Latitude != state.RawLatitude || aircraft.Longitude != state.RawLongitude
                        || aircraft.Altitude != state.RawAltitude;
                    if (newPosition)
                    {
                        double northNM = (aircraft.Latitude - state.Latitude) * 60;
                        double eastNM = (aircraft.Longitude - state.Longitude) * 60 * Math.Cos(aircraft.Latitude * Math.PI / 180);
                        if (Math.Sqrt(northNM * northNM + eastNM * eastNM) > ResetDistanceNM
                            || Math.Abs(aircraft.Altitude - state.Altitude) > ResetAltitudeFt)
                        {
                            Reset(state, aircraft, now);
                        }
                        else
                        {
                            Correct(state, aircraft, northNM, eastNM, alpha);
                            state.Altitude += altitudeAlpha * (aircraft.Altitude - state.Altitude);
                            state.RawLatitude = aircraft.Latitude;
                            state.RawLongitude = aircraft.Longitude;
                            state.RawAltitude = aircraft.Altitude;
                            state.RawTime = now;
                        }
                    }
                    // No new position for a while (data late, simulator paused): the estimate stops where it is (Predict
                    // moves it on at most MaxPredictSeconds) and never goes back; the next position corrects it.
                    // Vertical speed not known yet (first seconds of a track): the real altitude.
                    if (aircraft.VerticalSpeedFpm == null) state.Altitude = aircraft.Altitude;
                }
                Aircraft filtered = aircraft.Copy();
                filtered.Latitude = state.Latitude;
                filtered.Longitude = state.Longitude;
                filtered.Altitude = state.Altitude;
                aircraft.Filtered = filtered;
            }
            foreach (string callsign in states.Keys.Where(c => !present.Contains(c)).ToList())
            {
                states.Remove(callsign);
            }
        }

        private static State Reset(State state, Aircraft aircraft, DateTime now)
        {
            state.Latitude = state.RawLatitude = aircraft.Latitude;
            state.Longitude = state.RawLongitude = aircraft.Longitude;
            state.Altitude = state.RawAltitude = aircraft.Altitude;
            state.Time = now;
            state.RawTime = now;
            state.Ahead = 0;
            return state;
        }

        /// <summary>
        /// Pulls the estimate towards a new real position by <paramref name="alpha"/> of the difference
        /// (<paramref name="northNM"/>, <paramref name="eastNM"/>: real minus estimate), but never backwards along the
        /// track: across the track and forwards it is corrected at once; when the real position is behind (it arrived
        /// late, the estimate went too far) the difference is kept in <see cref="State.Ahead"/> and taken back by
        /// moving on more slowly. So the track always moves forwards, without jumps back.
        /// </summary>
        private static void Correct(State state, Aircraft aircraft, double northNM, double eastNM, double alpha)
        {
            double cosLatitude = Math.Max(0.01, Math.Cos(state.Latitude * Math.PI / 180));
            if (aircraft.Speed >= 30)
            {
                double track = aircraft.Track * Math.PI / 180;
                (double unitNorth, double unitEast) = (Math.Cos(track), Math.Sin(track));
                double alongNM = northNM * unitNorth + eastNM * unitEast;
                // Across the track: corrected as before.
                northNM -= alongNM * unitNorth;
                eastNM -= alongNM * unitEast;
                // Along the track: forwards at once; backwards later, by moving on more slowly. The new position
                // tells the whole gap at this moment, so it replaces what was still owed.
                double along = alpha * alongNM;
                state.Ahead = along < 0 ? -along : 0;
                along = Math.Max(0, along);
                northNM = alpha * northNM + along * unitNorth;
                eastNM = alpha * eastNM + along * unitEast;
            }
            else
            {
                // Slow or still (on the ground): no track to keep, plain correction.
                northNM *= alpha;
                eastNM *= alpha;
                state.Ahead = 0;
            }
            state.Latitude += northNM / 60;
            state.Longitude += eastNM / (60 * cosLatitude);
        }

        /// <summary>
        /// Moves the estimate on to <paramref name="now"/> with the speed, track and vertical speed of the aircraft;
        /// not more than <see cref="MaxPredictSeconds"/> after the last new position.
        /// </summary>
        private static void Predict(State state, Aircraft aircraft, DateTime now)
        {
            DateTime end = now < state.RawTime.AddSeconds(MaxPredictSeconds) ? now : state.RawTime.AddSeconds(MaxPredictSeconds);
            double seconds = (end - state.Time).TotalSeconds;
            state.Time = now;
            if (seconds <= 0) return;
            double nm = aircraft.Speed * seconds / 3600;
            // Ahead of the real aircraft: move on at half speed until it has caught up (never backwards); the
            // altitude slows down in the same way, so the track stays on its glide path.
            double hold = Math.Min(state.Ahead, nm / 2);
            double rate = nm > 0 ? (nm - hold) / nm : 1;
            nm -= hold;
            state.Ahead -= hold;
            double track = aircraft.Track * Math.PI / 180;
            double cosLatitude = Math.Max(0.01, Math.Cos(state.Latitude * Math.PI / 180));
            state.Latitude += nm * Math.Cos(track) / 60;
            state.Longitude += nm * Math.Sin(track) / (60 * cosLatitude);
            state.Altitude += (aircraft.VerticalSpeedFpm ?? 0) * seconds * rate / 60;
        }
    }
}
