using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: the log of the session (the user, 9 Nov: as TWR Trainer's, to make the two programs alike). One
    /// CSV file per session (START SESSION ... END SESSION) in the Logs folder ("sep=;" first: Excel in any language), with
    /// the same layout and columns as TWR Trainer: on top the session (airport, runway, start, network, the instructor's and
    /// the student's callsign and VID), then a row per event (ADDED, APPROACH STARTED, ON GLIDE PATH, AT MINIMUMS with the
    /// deviation, LANDING / GO AROUND / MISSED APPROACH, REMOVED, the instructor's commands, PHOTO), at the end a summary per
    /// aircraft. For each approach a picture of its trace (elevation with the glide path, azimuth with the centreline, the
    /// approach limits and the safety limits of the profile, the point at the minimums) in the folder of the session.
    /// Fed once a second with the simulated aircraft (the snapshots compared); on the user interface thread.
    /// </summary>
    internal sealed class ApproachLog
    {
        /// <summary>A point of the trace: distance from the touchdown (NM), height above the threshold (ft), offset right (NM).</summary>
        private readonly record struct TracePoint(double Distance, double Height, double Lateral);

        private sealed class Seen
        {
            public AircraftState State = null!;
            public string Class = "";
            public int Approaches, Landings, GoArounds, Missed;
            public string LastMinimums = "";
            // The approach going on.
            public bool OnApproach, OnGlidePath, PastMinimums;
            public int Climbing;
            public double LastDistance = double.NaN, LastDeviation = double.NaN;
            public string MinimumsText = "";
            public double StartedAt;
            public List<TracePoint> Trace = [];
        }

        private StreamWriter? writer;
        private bool inSession;
        private double sessionStart;
        private string fileName = "", folderName = "";
        private int traceCount;
        private bool paused;
        private readonly Dictionary<string, Seen> seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Seen> flights = new(StringComparer.OrdinalIgnoreCase);

        public static string LogsFolder => Path.Combine(Path.GetDirectoryName(SettingsStore.FilePath) ?? AppContext.BaseDirectory, "Logs");

        public bool InSession => inSession;
        public double SessionStart => sessionStart;
        public string FileName => fileName;
        /// <summary>The folder of the session (its traces), named as its log.</summary>
        public string SessionFolder => Path.Combine(LogsFolder, folderName);
        public string? Error { get; private set; }

        public string Airport { get; set; } = "";
        public string Runway { get; set; } = "";
        public bool Network { get; set; }
        public string InstructorCallsign { get; set; } = "";
        public string InstructorVid { get; set; } = "";
        public string StudentCallsign { get; set; } = "";
        public string StudentVid { get; set; } = "";

        private static string Field(string text) =>
            text.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

        private string Clock(double seconds)
        {
            seconds = Math.Max(0, seconds - sessionStart);
            return string.Create(CultureInfo.InvariantCulture, $"{(int)(seconds / 3600)}:{(int)(seconds % 3600 / 60):00}:{(int)(seconds % 60):00}");
        }

        private void Line(params string[] fields)
        {
            if (Error != null || !inSession) return;
            try
            {
                if (writer == null)
                {
                    Directory.CreateDirectory(LogsFolder);
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                    folderName = $"Log_{stamp}";
                    fileName = Path.Combine(LogsFolder, folderName + ".csv");
                    writer = new StreamWriter(fileName, false, new UTF8Encoding(true)) { AutoFlush = true };
                    writer.WriteLine("sep=;");
                    writer.WriteLine("AuroraPAR v2 Trainer;log of the approaches");
                    writer.WriteLine($"Aerodrome;{Field(Airport)}");
                    writer.WriteLine($"Runway;{Field(Runway)}");
                    writer.WriteLine($"Session started (UTC);{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"Network;{(Network ? "IVAO" : "")}");
                    writer.WriteLine($"Instructor callsign;{Field(InstructorCallsign)}");
                    writer.WriteLine($"Instructor VID;{Field(InstructorVid)}");
                    writer.WriteLine($"Student callsign;{Field(StudentCallsign)}");
                    writer.WriteLine($"Student VID;{Field(StudentVid)}");
                    writer.WriteLine();
                    writer.WriteLine("Sim time;UTC;Source;Callsign;ICAO type;Rules;Runway;Pattern;Event;Detail");
                }
                writer.WriteLine(string.Join(';', fields.Select(Field)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error = $"The log could not be written ({ex.Message}).";
                try { writer?.Dispose(); } catch (Exception) { }
                writer = null;
            }
        }

        /// <summary>A row: when (simulated seconds), what, of which aircraft (empty: none).</summary>
        public void Event(double time, string what, string callsign = "", string detail = "", string source = "SIM")
        {
            string type = callsign.Length > 0 && flights.TryGetValue(callsign, out Seen? known) ? known.Class : "";
            Line(Clock(time), DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture), source, callsign, type, "", callsign.Length > 0 ? Runway : "",
                callsign.Length > 0 ? "PAR" : "", what, detail);
        }

        /// <summary>START SESSION: a new file, the time from 0:00:00, the aircraft already flying written AT START.</summary>
        public void StartSession(double time, IReadOnlyList<AircraftState> traffic)
        {
            Close(time);
            inSession = true;
            sessionStart = time;
            traceCount = 0;
            Event(time, "SESSION START", source: "INSTRUCTOR");
            foreach (AircraftState s in traffic)
            {
                Seen was = new() { State = s, Class = s.PerformanceClass };
                seen[s.Callsign] = was;
                flights[s.Callsign] = was;
                Event(time, "AT START", s.Callsign, string.Create(CultureInfo.InvariantCulture, $"{s.PerformanceClass}, {Math.Round(s.AltitudeFt):0} ft"));
            }
        }

        /// <summary>END SESSION (or the program closing): the row, the summary, the file closed.</summary>
        public void EndSession(double time, string why = "")
        {
            if (!inSession) return;
            Event(time, "SESSION END", detail: why, source: "INSTRUCTOR");
            Close(time);
        }

        /// <summary>The summary per aircraft and the file closed.</summary>
        public void Close(double time)
        {
            if (writer != null)
            {
                try
                {
                    writer.WriteLine();
                    writer.WriteLine($"Ended (UTC);{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)};sim time {Clock(time)}");
                    writer.WriteLine($"Network;{(Network ? "IVAO" : "")};Instructor;{Field(InstructorCallsign)};{Field(InstructorVid)};Student;{Field(StudentCallsign)};{Field(StudentVid)}");
                    writer.WriteLine();
                    writer.WriteLine("Summary;Class;Approaches;Landings;Go-arounds;Missed approaches;Last deviation at minimums");
                    foreach ((string callsign, Seen f) in flights.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
                        writer.WriteLine(string.Join(';', Field(callsign), Field(f.Class), f.Approaches, f.Landings, f.GoArounds, f.Missed, Field(f.LastMinimums)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                try { writer.Dispose(); } catch (Exception) { }
            }
            writer = null;
            inSession = false;
            fileName = "";
            seen.Clear();
            flights.Clear();
            Error = null;
        }

        /// <summary>
        /// Once a second: the simulated aircraft now, the runway in use (the student's in a lesson), the radar (its approach
        /// and safety limits), where the approach limits end (NM, 0: the range), the magnetic variation (headings written
        /// magnetic) and where an aircraft is (lat / lon of the plane of the engine).
        /// </summary>
        public void Step(double time, bool enginePaused, IReadOnlyList<AircraftState> states, Runway runway, Radar radar, double limitsTo, bool azimuthLimits,
            double variation, Func<double, double, (double Latitude, double Longitude)?> toGeo)
        {
            if (!inSession) return;
            if (enginePaused != paused)
            {
                paused = enginePaused;
                Event(time, paused ? "SIMULATION PAUSED" : "SIMULATION RESUMED", source: "INSTRUCTOR");
            }
            HashSet<string> now = new(StringComparer.OrdinalIgnoreCase);
            foreach (AircraftState s in states)
            {
                now.Add(s.Callsign);
                if (!seen.TryGetValue(s.Callsign, out Seen? was))
                {
                    was = new Seen { State = s, Class = s.PerformanceClass };
                    seen[s.Callsign] = was;
                    flights[s.Callsign] = was;
                    Event(time, "ADDED", s.Callsign, string.Create(CultureInfo.InvariantCulture, $"{s.PerformanceClass}, {Math.Round(s.AltitudeFt):0} ft, {Math.Round(s.SpeedKt):0} kt"));
                }
                Commands(time, was.State, s, variation);
                Approach(time, was, s, runway, radar, limitsTo, azimuthLimits, toGeo);
                was.State = s;
            }
            foreach (string gone in seen.Keys.Where(k => !now.Contains(k)).ToList())
            {
                Seen was = seen[gone];
                if (was.OnApproach) EndApproach(time, gone, was, "REMOVED during the approach", runway, radar, limitsTo, azimuthLimits);
                if (was.State.Contact != GroundContact.Landed) Event(time, "REMOVED", gone);
                seen.Remove(gone);
            }
        }

        /// <summary>The instructor's commands that change the flight, seen from the values commanded (second by second).</summary>
        private void Commands(double time, AircraftState was, AircraftState s, double variation)
        {
            string cs = s.Callsign;
            void Say(string what) => Event(time, what, cs, source: "INSTRUCTOR");
            if (s.Frozen != was.Frozen) Say(s.Frozen ? "PAUSE" : "UNPAUSE");
            if (s.Navigation != was.Navigation) Say(s.Navigation == NavigationMode.Route ? "NAVIGATION ROUTE" : "NAVIGATION MANUAL");
            if (s.StickActive && !was.StickActive) Say("STICK");
            if (s.Navigation == NavigationMode.Manual && !s.StickActive && Math.Abs(Angle(s.TargetHeading - was.TargetHeading)) >= 0.5)
            {
                double magnetic = ((s.TargetHeading - variation) % 360 + 360) % 360;
                int shown = (int)Math.Round(magnetic) % 360;
                Say(string.Create(CultureInfo.InvariantCulture, $"HEADING {(shown == 0 ? 360 : shown):000}"));
            }
            if (s.VerticalMode != was.VerticalMode || s.TargetAltitudeFt != was.TargetAltitudeFt || s.CommandedVerticalSpeedFpm != was.CommandedVerticalSpeedFpm)
            {
                if (s.VerticalMode == VerticalMode.GlidePath && was.VerticalMode != VerticalMode.GlidePath) Say("RATE NORMAL");
                else if (s.VerticalMode == VerticalMode.Rate && Math.Abs(s.CommandedVerticalSpeedFpm - was.CommandedVerticalSpeedFpm) >= 1)
                    Say(string.Create(CultureInfo.InvariantCulture, $"RATE {Math.Round(s.CommandedVerticalSpeedFpm):+0;-0;0} fpm"));
                else if (s.VerticalMode == VerticalMode.Altitude && !s.RouteAltitude && Math.Abs(s.TargetAltitudeFt - was.TargetAltitudeFt) >= 1)
                    Say(string.Create(CultureInfo.InvariantCulture, $"ALTITUDE {Math.Round(s.TargetAltitudeFt):0} ft"));
            }
            if (!s.RouteSpeed && Math.Abs(s.TargetSpeedKt - was.TargetSpeedKt) >= 1) Say(string.Create(CultureInfo.InvariantCulture, $"SPEED {Math.Round(s.TargetSpeedKt):0} kt"));
        }

        private static double Angle(double a) => ((a + 540) % 360) - 180;

        /// <summary>
        /// The approach of an aircraft: started when it is inside the range of the PAR in front of the runway, flying towards
        /// it; ON GLIDE PATH the first time it reaches it; AT MINIMUMS where the glide path reaches the decision height (the
        /// deviation then); ended by the landing, a climb (GO AROUND, or MISSED APPROACH past the minimums), leaving the range,
        /// or its removal. Its trace kept meanwhile, its picture made at the end.
        /// </summary>
        private void Approach(double time, Seen was, AircraftState s, Runway runway, Radar radar, double limitsTo, bool azimuthLimits,
            Func<double, double, (double Latitude, double Longitude)?> toGeo)
        {
            string cs = s.Callsign;
            if (toGeo(s.X, s.Y) is not (double latitude, double longitude)) return;
            Aircraft a = new() { Callsign = cs, Latitude = latitude, Longitude = longitude, Altitude = s.AltitudeFt };
            double distance = a.DistanceFromTouchdown(runway), lateral = a.LateralOffset(runway), height = s.AltitudeFt - runway.Elevation;
            double deviation = height - runway.GlidePathHeight(distance);
            bool towards = Math.Abs(Angle(s.TrackTrue - runway.Heading)) < 90;
            if (!was.OnApproach)
            {
                bool inRange = s.Contact == GroundContact.Airborne && distance > 0.3 && distance <= runway.Distance && towards
                    && Math.Abs(lateral) <= distance * Math.Tan(20 * Math.PI / 180) + 0.3;
                if (!inRange) return;
                was.OnApproach = true;
                was.OnGlidePath = was.PastMinimums = false;
                was.Climbing = 0;
                was.MinimumsText = "";
                was.StartedAt = time;
                was.Trace = [];
                was.Approaches++;
                Event(time, "APPROACH STARTED", cs, string.Create(CultureInfo.InvariantCulture, $"approach {was.Approaches}, {distance:0.0} NM, {Math.Round(height):0} ft above the threshold"));
            }
            if (s.Contact == GroundContact.Airborne) was.Trace.Add(new TracePoint(distance, height, lateral));
            // ON GLIDE PATH: the first time within 30 ft of it, or crossing it.
            if (!was.OnGlidePath && s.Contact == GroundContact.Airborne
                && (Math.Abs(deviation) <= 30 || (double.IsFinite(was.LastDeviation) && Math.Sign(deviation) != Math.Sign(was.LastDeviation))))
            {
                was.OnGlidePath = true;
                Event(time, "ON GLIDE PATH", cs, string.Create(CultureInfo.InvariantCulture, $"{distance:0.0} NM"));
            }
            // AT MINIMUMS: where the glide path reaches the decision height.
            double minimums = runway.MissedApproachPointNM;
            if (!was.PastMinimums && double.IsFinite(minimums) && double.IsFinite(was.LastDistance) && was.LastDistance > minimums && distance <= minimums)
            {
                was.PastMinimums = true;
                double feetLateral = lateral * Runway.FeetPerNM;
                string text = string.Create(CultureInfo.InvariantCulture,
                    $"{Math.Abs(Math.Round(deviation)):0} ft {(deviation >= 0 ? "above" : "below")} the glide path, {Math.Abs(Math.Round(feetLateral)):0} ft {(feetLateral >= 0 ? "right" : "left")} of the centreline");
                was.MinimumsText = text;
                was.LastMinimums = text;
                Event(time, "AT MINIMUMS", cs, text);
            }
            was.LastDistance = distance;
            was.LastDeviation = deviation;
            // The end.
            if (s.Contact == GroundContact.Landed)
            {
                was.Landings++;
                EndApproach(time, cs, was, "LANDING", runway, radar, limitsTo, azimuthLimits);
                return;
            }
            if (s.Contact == GroundContact.OnGround)
            {
                EndApproach(time, cs, was, "ON THE GROUND (not on the runway)", runway, radar, limitsTo, azimuthLimits);
                return;
            }
            was.Climbing = s.VerticalSpeedFpm >= 300 ? was.Climbing + 1 : 0;
            if (was.Climbing >= 3)
            {
                if (was.PastMinimums) was.Missed++;
                else was.GoArounds++;
                EndApproach(time, cs, was, was.PastMinimums ? "MISSED APPROACH" : "GO AROUND", runway, radar, limitsTo, azimuthLimits);
                return;
            }
            if (distance > runway.Distance + 1 || distance < -0.5 || !towards && distance > 1)
                EndApproach(time, cs, was, "APPROACH ENDED (left the range)", runway, radar, limitsTo, azimuthLimits);
        }

        /// <summary>The end of an approach: its row (with the file of its trace), the picture made.</summary>
        private void EndApproach(double time, string callsign, Seen was, string what, Runway runway, Radar radar, double limitsTo, bool azimuthLimits)
        {
            was.OnApproach = false;
            was.LastDistance = double.NaN;
            was.LastDeviation = double.NaN;
            string picture = "";
            if (was.Trace.Count >= 2)
            {
                traceCount++;
                picture = string.Create(CultureInfo.InvariantCulture, $"Trace-{traceCount:00}_{Safe(callsign)}.png");
                SaveTrace(Path.Combine(SessionFolder, picture), callsign, was, what, time, runway, radar, limitsTo, azimuthLimits);
            }
            Event(time, what, callsign, string.Join(", ", new[] { was.MinimumsText.Length > 0 ? $"at minimums {was.MinimumsText}" : "", picture.Length > 0 ? $"trace {picture}" : "" }.Where(t => t.Length > 0)));
            was.Trace = [];
        }

        private static string Safe(string name) => new(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

        // ----- The picture of the trace ------------------------------------------------------------------------------

        private static readonly Brush Back = Frozen(new SolidColorBrush(Colors.Black));
        private static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromRgb(0x7C, 0xFC, 0x9A)));
        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xAC)));
        private static readonly Pen Grid = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x2A)), 1));
        private static readonly Pen Axis = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x7A, 0x3E)), 1));
        private static readonly Pen Ideal = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0xD2, 0x4A)), 1.6));
        private static readonly Pen Limits = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0xD2, 0x4A)), 1) { DashStyle = new DashStyle([4, 4], 0) });
        private static readonly Pen Safety = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0x50, 0x50)), 1) { DashStyle = new DashStyle([2, 4], 0) });
        private static readonly Pen Track = Frozen(new Pen(Brushes.White, 1.8));
        private static readonly Pen Mark = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)), 1.8));
        private static readonly Typeface Face = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static T Frozen<T>(T item) where T : Freezable
        {
            item.Freeze();
            return item;
        }

        /// <summary>
        /// The trace as a picture (the user, 9 Nov; as the PAR scope): elevation (height above the threshold, the glide path and
        /// its approach limits; the safety limits where the profile shows them), azimuth (offset from the centreline, its
        /// approach limits where the profile draws them), the point at the minimums. Drawn here, written on another thread.
        /// </summary>
        private void SaveTrace(string path, string callsign, Seen was, string result, double time, Runway runway, Radar radar, double limitsTo, bool azimuthLimits)
        {
            const double width = 900, height = 600, left = 70, right = 20;
            double range = Math.Max(1, runway.Distance);
            double limitsEnd = limitsTo > 0 ? Math.Min(limitsTo, range) : range;
            List<TracePoint> trace = was.Trace.Where(p => p.Distance >= 0 && p.Distance <= range + 0.5).ToList();
            if (trace.Count < 2) return;
            double X(double d) => left + (width - left - right) * Math.Clamp(d, 0, range) / range;
            double maxHeight = Math.Max(runway.GlidePathHeight(range, radar.UpperApproachLimit ? radar.ApproachAbove : 0), trace.Max(p => p.Height)) * 1.1;
            double elevTop = 60, elevBottom = 280, azTop = 330, azBottom = 560;
            double YE(double h) => elevBottom - (elevBottom - elevTop) * Math.Clamp(h, 0, maxHeight) / maxHeight;
            double maxLateral = Math.Max(Math.Max(radar.ApproachHalfWidth(range, true), radar.ApproachHalfWidth(range, false)), trace.Max(p => Math.Abs(p.Lateral))) * 1.2;
            maxLateral = Math.Max(maxLateral, 0.2);
            double azMiddle = (azTop + azBottom) / 2;
            // (Right of the centreline, as the pilot sees it, down: as on the azimuth view of the PAR.)
            double YA(double lateral) => azMiddle + (azBottom - azTop) / 2 * Math.Clamp(lateral, -maxLateral, maxLateral) / maxLateral;
            double dip = 1;
            FormattedText Write(string text, Brush brush, double size = 12) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, dip);

            DrawingVisual picture = new();
            using (DrawingContext dc = picture.RenderOpen())
            {
                dc.DrawRectangle(Back, null, new Rect(0, 0, width, height));
                string minutes = string.Create(CultureInfo.InvariantCulture, $"T+{(int)(Math.Max(0, was.StartedAt - sessionStart) / 60):00}:{(int)(Math.Max(0, was.StartedAt - sessionStart) % 60):00}");
                dc.DrawText(Write(string.Create(CultureInfo.InvariantCulture,
                    $"{callsign}  {was.Class}  {runway.ICAO} RWY {runway.Designator}  GP {runway.GlideSlope:0.0}°  approach {was.Approaches}  {minutes}  result: {result}"), Text, 13), new Point(14, 12));
                if (was.MinimumsText.Length > 0) dc.DrawText(Write($"At minimums: {was.MinimumsText}", Mark.Brush, 12), new Point(14, 32));
                // Grids and distances.
                for (int nm = 0; nm <= (int)range; nm++)
                {
                    double x = X(nm);
                    dc.DrawLine(Grid, new Point(x, elevTop), new Point(x, elevBottom));
                    dc.DrawLine(Grid, new Point(x, azTop), new Point(x, azBottom));
                    FormattedText label = Write(nm == 0 ? "TD" : $"{nm}NM", Text, 11);
                    dc.DrawText(label, new Point(x - label.Width / 2, elevBottom + 4));
                    dc.DrawText(label, new Point(x - label.Width / 2, azBottom + 4));
                }
                dc.DrawText(Write("ELEVATION (ft above the threshold)", Dim, 11), new Point(left, elevTop - 16));
                dc.DrawText(Write("AZIMUTH (L / R of the centreline)", Dim, 11), new Point(left, azTop - 16));
                dc.DrawLine(Axis, new Point(X(0), elevBottom), new Point(X(range), elevBottom));
                dc.DrawText(Write(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(maxHeight / 1.1):0} ft"), Dim, 11), new Point(6, YE(maxHeight / 1.1) - 7));
                dc.DrawText(Write("L", Dim, 12), new Point(left - 20, azTop + 2));
                dc.DrawText(Write("R", Dim, 12), new Point(left - 20, azBottom - 16));
                // Elevation: the glide path, its approach limits (to where they end), the safety limits, the decision height.
                void Curve(Pen pen, Func<double, double> y, double from, double to, Func<double, double> X2)
                {
                    StreamGeometry line = new();
                    using (StreamGeometryContext c = line.Open())
                    {
                        const int steps = 60;
                        for (int i = 0; i <= steps; i++)
                        {
                            double d = from + (to - from) * i / steps;
                            Point p = new(X2(d), y(d));
                            if (i == 0) c.BeginFigure(p, false, false);
                            else c.LineTo(p, true, false);
                        }
                    }
                    dc.DrawGeometry(null, pen, line);
                }
                Curve(Ideal, d => YE(runway.GlidePathHeight(d)), 0, range, X);
                Curve(Limits, d => YE(runway.GlidePathHeight(d, -radar.ApproachBelow)), 0, limitsEnd, X);
                if (radar.UpperApproachLimit) Curve(Limits, d => YE(runway.GlidePathHeight(d, radar.ApproachAbove)), 0, limitsEnd, X);
                if (radar.SafetyLimitsElevation)
                {
                    Curve(Safety, d => YE(runway.GlidePathHeight(d, radar.SafetyAbove)), 0, range, X);
                    Curve(Safety, d => YE(runway.GlidePathHeight(d, -radar.SafetyBelow)), 0, range, X);
                }
                double mapt = runway.MissedApproachPointNM;
                if (double.IsFinite(mapt) && mapt <= range)
                {
                    dc.DrawLine(Limits, new Point(X(0), YE(runway.MDH)), new Point(X(Math.Min(range, mapt + 1)), YE(runway.MDH)));
                    dc.DrawText(Write("DH", Dim, 11), new Point(X(Math.Min(range, mapt + 1)) + 3, YE(runway.MDH) - 7));
                }
                // Azimuth: the centreline, its approach limits (where the profile draws them), the safety limits.
                dc.DrawLine(Ideal, new Point(X(0), YA(0)), new Point(X(range), YA(0)));
                if (azimuthLimits)
                {
                    Curve(Limits, d => YA(radar.ApproachHalfWidth(d, true)), 0, limitsEnd, X);
                    Curve(Limits, d => YA(-radar.ApproachHalfWidth(d, false)), 0, limitsEnd, X);
                }
                if (radar.SafetyLimitsAzimuth)
                {
                    Curve(Safety, d => YA(d * Math.Tan(radar.SafetyRight * Math.PI / 180)), 0, range, X);
                    Curve(Safety, d => YA(-d * Math.Tan(radar.SafetyLeft * Math.PI / 180)), 0, range, X);
                }
                // The trace.
                StreamGeometry elevation = new(), azimuth = new();
                using (StreamGeometryContext e = elevation.Open())
                using (StreamGeometryContext z = azimuth.Open())
                {
                    for (int i = 0; i < trace.Count; i++)
                    {
                        Point pe = new(X(trace[i].Distance), YE(trace[i].Height)), pa = new(X(trace[i].Distance), YA(trace[i].Lateral));
                        if (i == 0)
                        {
                            e.BeginFigure(pe, false, false);
                            z.BeginFigure(pa, false, false);
                        }
                        else
                        {
                            e.LineTo(pe, true, false);
                            z.LineTo(pa, true, false);
                        }
                    }
                }
                dc.DrawGeometry(null, Track, elevation);
                dc.DrawGeometry(null, Track, azimuth);
                // The point at the minimums.
                if (double.IsFinite(mapt) && trace.Count > 0 && trace.Min(p => p.Distance) <= mapt)
                {
                    TracePoint at = trace.OrderBy(p => Math.Abs(p.Distance - mapt)).First();
                    dc.DrawEllipse(null, Mark, new Point(X(at.Distance), YE(at.Height)), 5, 5);
                    dc.DrawEllipse(null, Mark, new Point(X(at.Distance), YA(at.Lateral)), 5, 5);
                }
            }
            RenderTargetBitmap bitmap = new((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(picture);
            bitmap.Freeze();
            string folder = SessionFolder;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using FileStream file = File.Create(path);
                    encoder.Save(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
                {
                    // (Not written: the row says its name anyway.)
                }
            });
        }
    }
}
