using System.Globalization;
using System.Text.Json;
using ATCRoom.Sim;
using AuroraPAR;

// Self-test of the simulation engine (ATCRoom.Sim). Run by the GitHub build: any failure gives exit code 1.
// Times in the checks follow from the fixed steps of 0.1 s: an instruction is carried out at the start of the next step,
// the turn rate changes by 1.5°/s per second (rate one in 2 s), the vertical speed by the class's vertical acceleration
// (Turboprop, the default class: 400 ft/min per second).

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int failures = 0;
int checks = 0;

void Check(bool ok, string name, string detail = "")
{
    checks++;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
}

bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;
bool Between(double value, double low, double high) => value >= low && value <= high;

// A runway like LIBN 32 (threshold of runways.par, heading for AuroraPAR).
const double ThresholdLatitude = 40.23227088;
const double ThresholdLongitude = 18.13907997;
const double RunwayHeading = 325.80;

// --- Turns -------------------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 90);
    engine.RunFor(1);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.TurnRateDps, 1.5, 1e-9), "Roll-in: half of rate one after 1 s", $"rate {s.TurnRateDps:F3}");
    double highest = 0;
    for (int i = 0; i < 285; i++)
    {
        engine.RunFor(0.1);
        highest = Math.Max(highest, engine.Get(a)!.HeadingTrue);
    }
    s = engine.Get(a)!;
    Check(Near(s.TurnRateDps, 3, 1e-9) && Between(s.HeadingTrue, 85, 86), "Turn right 90° at 3°/s: full rate at 29.5 s", $"heading {s.HeadingTrue:F2}");
    for (int i = 0; i < 15; i++)
    {
        engine.RunFor(0.1);
        highest = Math.Max(highest, engine.Get(a)!.HeadingTrue);
    }
    s = engine.Get(a)!;
    Check(Between(s.HeadingTrue, 88, 90) && s.TurnRateDps > 0 && s.TurnRateDps < 3, "Turn right 90°: rolling out at 31 s", $"heading {s.HeadingTrue:F2} rate {s.TurnRateDps:F2}");
    for (int i = 0; i < 20; i++)
    {
        engine.RunFor(0.1);
        highest = Math.Max(highest, engine.Get(a)!.HeadingTrue);
    }
    s = engine.Get(a)!;
    Check(s.HeadingTrue == 90 && s.TurnRateDps == 0 && highest <= 90, "Turn right 90°: exactly on 090 by 33 s, never past it", $"heading {s.HeadingTrue:R} highest {highest:R}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 90, TurnDirection.Left);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    Check(Between(s.HeadingTrue, 330, 336) && s.TurnRateDps < 0, "Turn to 090 forced left: through 333 after 10 s", $"heading {s.HeadingTrue:F2}");
    engine.RunFor(85);
    s = engine.Get(a)!;
    Check(s.HeadingTrue == 90 && s.TurnRateDps == 0 && s.TurnDirection == TurnDirection.Shortest, "Turn to 090 forced left: on 090 by 95 s, side back to shortest", $"heading {s.HeadingTrue:R}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 350, 150);
    engine.SetHeading(a, 10);
    engine.RunFor(3);
    AircraftState s = engine.Get(a)!;
    Check(Between(s.HeadingTrue, 352, 358) && s.TurnRateDps > 0, "Shortest turn across north: 350 to 010 turns right", $"heading {s.HeadingTrue:F2}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetTurnRateMode(a, TurnRateMode.Half);
    engine.SetHeading(a, 30);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    Check(Between(s.HeadingTrue, 13.5, 15) && Near(s.TurnRateDps, 1.5, 1e-9), "Half rate turn: about 14° in 10 s at 1.5°/s", $"heading {s.HeadingTrue:F2}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 10000, 0, 250, null, "Jet");
    engine.SetHeading(a, 120);
    engine.RunFor(20);
    AircraftState s = engine.Get(a)!;
    double expected = SimEngine.RateForBank(30, 250);
    Check(Near(s.TurnRateDps, expected, 1e-6) && Near(expected, 2.52, 0.01), "Jet at 250 kt asked rate one: bank limit 30°, about 2.5°/s", $"rate {s.TurnRateDps:F3}");
}

// --- Altitude and speed ------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 2000, 0, 150);
    engine.SetAltitude(a, 3000, 1000);
    double highest = 0;
    for (int i = 0; i < 590; i++)
    {
        engine.RunFor(0.1);
        highest = Math.Max(highest, engine.Get(a)!.AltitudeFt);
    }
    AircraftState s = engine.Get(a)!;
    Check(s.AltitudeFt < 3000 && Near(s.VerticalSpeedFpm, 1000, 1e-9), "Climb 1000 ft at 1000 ft/min: still climbing at 59 s", $"altitude {s.AltitudeFt:F1}");
    for (int i = 0; i < 60; i++)
    {
        engine.RunFor(0.1);
        highest = Math.Max(highest, engine.Get(a)!.AltitudeFt);
    }
    s = engine.Get(a)!;
    Check(s.AltitudeFt == 3000 && s.VerticalSpeedFpm == 0 && highest <= 3000, "Climb 1000 ft: smooth level-off, level at 3000 by 65 s, never above", $"highest {highest:R}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 180);
    engine.SetSpeed(a, 140);
    engine.RunFor(39.5);
    AircraftState s = engine.Get(a)!;
    Check(s.SpeedKt > 140 && s.SpeedKt < 141, "Slow down 180 to 140 kt at 1 kt/s: 140.5 at 39.5 s", $"speed {s.SpeedKt:F3}");
    engine.RunFor(0.5);
    s = engine.Get(a)!;
    Check(s.SpeedKt == 140, "Slow down 180 to 140 kt at 1 kt/s: 140 at 40 s", $"speed {s.SpeedKt:R}");
    engine.SetSpeed(a, 170);
    engine.RunFor(20);
    s = engine.Get(a)!;
    Check(s.SpeedKt == 170, "Speed up 140 to 170 kt at 1.5 kt/s: 170 at 20 s", $"speed {s.SpeedKt:R}");
}

// --- Movement and wind -------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string north = engine.Add(0, 0, 3000, 0, 120);
    string east = engine.Add(0, 0, 3000, 90, 120);
    engine.RunFor(60);
    AircraftState n = engine.Get(north)!;
    AircraftState e = engine.Get(east)!;
    Check(Near(n.Y, 2, 1e-9) && Near(n.X, 0, 1e-9), "Straight north at 120 kt: 2 NM in 1 min", $"x {n.X:G6} y {n.Y:G10}");
    Check(Near(e.X, 2, 1e-9) && Near(e.Y, 0, 1e-9), "Straight east at 120 kt: 2 NM in 1 min", $"x {e.X:G10} y {e.Y:G6}");
}

{
    SimEngine engine = new();
    engine.SetWind(270, 20, 0);
    string a = engine.Add(0, 0, 3000, 0, 140);
    engine.RunFor(60);
    AircraftState s = engine.Get(a)!;
    double drift = Math.Atan2(20, 140) * 180 / Math.PI;
    Check(Near(s.DriftDegrees, drift, 1e-6) && Near(s.GroundSpeedKt, Math.Sqrt(140 * 140 + 20 * 20), 1e-6) && Near(s.X, 20.0 / 60, 1e-6) && Near(s.Y, 140.0 / 60, 1e-6),
        "Wind from 270 at 20 kt, heading 360: drifts 8.1° right, 1/3 NM east in 1 min", $"drift {s.DriftDegrees:F3} x {s.X:F4}");
}

{
    SimEngine engine = new();
    engine.SetGlidePath(0, 3);
    engine.SetWind(0, 20, 0);
    string a = engine.Add(0, -10, 3000, 0, 140);
    engine.SetGlidePathRate(a);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    double headwind = -120 * SimEngine.FeetPerNM / 60 * Math.Tan(3 * Math.PI / 180);
    Check(Near(s.VerticalSpeedFpm, headwind, 1e-6) && Near(s.GroundSpeedKt, 120, 1e-6), "Normal rate with 20 kt of headwind: as at 120 kt over the ground", $"VS {s.VerticalSpeedFpm:F1}");
}

{
    IReadOnlyList<AircraftState> Gusty()
    {
        SimEngine engine = new();
        engine.SetWind(250, 15, 10);
        string a = engine.Add(0, 0, 3000, 0, 140);
        engine.RunFor(30);
        return engine.Snapshot();
    }
    IReadOnlyList<AircraftState> first = Gusty();
    IReadOnlyList<AircraftState> second = Gusty();
    Check(first.SequenceEqual(second) && first[0].GroundSpeedKt != 140, "Gusts: random but the same in two runs");
}

// --- Ground, landing ---------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    engine.SetGlidePath(0, 3, 500, 1.5);
    double onPath = 500 + 3 * SimEngine.FeetPerNM * Math.Tan(3 * Math.PI / 180);
    string a = engine.Add(0, -3, onPath, 0, 140);
    engine.SetGlidePathRate(a);
    bool landed = false;
    for (int i = 0; i < 1200 && engine.Get(a) is AircraftState state; i++)
    {
        if (state.Contact == GroundContact.Landed) landed = true;
        engine.RunFor(0.1);
    }
    Check(landed && engine.Get(a) == null, "Normal rate from 3 NM on the glide path: lands on the runway, removed a few seconds later");

    string b = engine.Add(3, -5, 600, 0, 140);
    engine.SetVerticalSpeed(b, -1000);
    engine.RunFor(20);
    AircraftState s = engine.Get(b)!;
    Check(s.Contact == GroundContact.OnGround && s.AltitudeFt == 500 && s.GroundSpeedKt == 0, "Descending 3 NM beside the runway: stops on the ground at the runway elevation (500 ft)");
    engine.RunFor(20);
    Check(engine.Get(b) is AircraftState still && still.X == s.X && still.Y == s.Y, "On the ground outside the runway: stays there until removed");

    string c = engine.Add(-5, 0, 2000, 0, 140);
    engine.SetAltitude(c, 300);
    engine.RunFor(0.1);
    Check(engine.Get(c)!.TargetAltitudeFt == 500, "Altitude below the runway: kept at the runway elevation");
}

{
    SimEngine engine = new();
    HashSet<string> flying = new(StringComparer.OrdinalIgnoreCase) { "SIM1", "AZA123" };
    string a = engine.Add(0, 0, 3000, 0, 140, null, null, flying.Contains);
    string b = engine.Add(0, 0, 3000, 0, 140, "aza123", null, flying.Contains);
    Check(a == "SIM2" && b == "AZA123A", "Callsigns already flying outside the simulation are skipped (SIM2) or get a letter (AZA123A)", $"{a} {b}");
    engine.Clear();
    Check(engine.Add(0, 0, 3000, 0, 140) == "SIM1", "After Remove all the callsigns start again from SIM1");
}

// --- Performance classes -----------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string light = engine.Add(0, 0, 3000, 0, 40, null, "Light");
    AircraftState s = engine.Get(light)!;
    Check(s.SpeedKt == 60 && s.PerformanceClass == "Light", "Light aircraft added at 40 kt: kept at its minimum, 60 kt", $"speed {s.SpeedKt:R}");
    engine.SetSpeed(light, 250);
    engine.SetVerticalSpeed(light, -3000);
    engine.RunFor(0.1);
    s = engine.Get(light)!;
    Check(s.TargetSpeedKt == 160 && s.CommandedVerticalSpeedFpm == -1500, "Light aircraft: speed and descent kept within the class (160 kt, 1500 ft/min)",
        $"speed {s.TargetSpeedKt:R} VS {s.CommandedVerticalSpeedFpm:R}");
    string other = engine.Add(0, 0, 3000, 0, 150, null, "Glider");
    Check(engine.Get(other)!.PerformanceClass == PerformanceClass.DefaultName, "Unknown class: the Turboprop");
}

{
    string folder = Path.Combine(Path.GetTempPath(), "atcroom-sim-selftest-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string path = Path.Combine(folder, PerformanceFile.FileName);
    try
    {
        var created = PerformanceFile.Load(path);
        Check(File.Exists(path) && created.Classes.Count == 3 && !created.ReadOnly && created.Message == null, "performance.json missing: written with the 3 default classes");
        var again = PerformanceFile.Load(path);
        Check(again.Classes.Select(c => c.Name).SequenceEqual(new[] { "Light", "Turboprop", "Jet" }), "performance.json read back: the same classes");

        List<PerformanceClass> defaults = PerformanceClass.Defaults();
        Check(defaults[1].ApproachSpeedKt == 140 && defaults[1].FinalSpeedKt == 110 && defaults[2].ApproachSpeedKt == 180 && defaults[2].FinalSpeedKt == 140,
            "Default speeds: Turboprop 140 / final 110, Jet 180 / final 140");

        string old = "{ \"FormatVersion\": 1, \"Classes\": [ { \"Name\": \"Light\", \"MinSpeedKt\": 60, \"MaxSpeedKt\": 160, \"ApproachSpeedKt\": 90 }, "
            + "{ \"Name\": \"Turboprop\", \"MinSpeedKt\": 90, \"MaxSpeedKt\": 280, \"ApproachSpeedKt\": 120 }, "
            + "{ \"Name\": \"Jet\", \"MinSpeedKt\": 120, \"MaxSpeedKt\": 350, \"ApproachSpeedKt\": 150 }, "
            + "{ \"Name\": \"Heavy\", \"MinSpeedKt\": 130, \"MaxSpeedKt\": 340, \"ApproachSpeedKt\": 160 } ] }";
        File.WriteAllText(path, old);
        var upgraded = PerformanceFile.Load(path);
        var reread = PerformanceFile.Load(path);
        Check(upgraded.Classes[0].FinalSpeedKt == 70 && upgraded.Classes[1].ApproachSpeedKt == 140 && upgraded.Classes[1].FinalSpeedKt == 110
            && upgraded.Classes[2].ApproachSpeedKt == 150 && upgraded.Classes[2].FinalSpeedKt == 140
            && Near(upgraded.Classes[3].FinalSpeedKt, 140, 1e-9) && File.ReadAllText(path).Contains("FinalSpeedKt")
            && reread.Classes[1].ApproachSpeedKt == 140 && reread.Classes[2].ApproachSpeedKt == 150,
            "performance.json before the final speed: final speeds added, initial speeds raised only where never changed, file written once",
            string.Join(" ", upgraded.Classes.Select(c => $"{c.Name} {c.ApproachSpeedKt}/{c.FinalSpeedKt}")));

        string newer = "{ \"FormatVersion\": 2, \"Future\": true, \"Classes\": [ { \"Name\": \"Heavy\", \"MinSpeedKt\": 140, \"MaxSpeedKt\": 340, \"ApproachSpeedKt\": 150, \"Wake\": \"H\" } ] }";
        File.WriteAllText(path, newer);
        var future = PerformanceFile.Load(path);
        Check(future.ReadOnly && future.Message != null && future.Classes.Count == 1 && future.Classes[0].Name == "Heavy" && File.ReadAllText(path) == newer,
            "performance.json of a newer version: used, never written");

        PerformanceFile? parsed = JsonSerializer.Deserialize<PerformanceFile>(newer, PerformanceFile.JsonOptions);
        string written = JsonSerializer.Serialize(parsed, PerformanceFile.JsonOptions);
        Check(written.Contains("\"Future\"") && written.Contains("\"Wake\""), "Unknown fields kept when the file is written again");

        File.WriteAllText(path, "{ not json");
        var damaged = PerformanceFile.Load(path);
        Check(File.Exists(path + ".bad") && damaged.Classes.Count == 3 && damaged.Message != null, "Damaged performance.json: kept as .bad, defaults written");
    }
    finally
    {
        try { Directory.Delete(folder, true); } catch (Exception) { }
    }
}

// --- METAR wind ----------------------------------------------------------------------------------------------------

{
    bool ok1 = MetarWind.TryParse("LIBN 181650Z 27015G25KT 9999 FEW030 18/12 Q1015", out double f1, out double s1, out double g1);
    bool ok2 = MetarWind.TryParse("LIRF 181650Z VRB03KT CAVOK", out double f2, out double s2, out double g2);
    bool ok3 = MetarWind.TryParse("UUEE 181650Z 27008MPS 9999", out double f3, out double s3, out double g3);
    bool ok4 = MetarWind.TryParse("LIBN 181650Z 9999 CAVOK", out _, out _, out _);
    Check(ok1 && f1 == 270 && s1 == 15 && g1 == 10 && ok2 && double.IsNaN(f2) && s2 == 3 && g2 == 0 && ok3 && f3 == 270 && Near(s3, 15.55, 0.01) && g3 == 0 && !ok4,
        "METAR wind: knots with gusts, variable, metres per second, none");
}

// --- Clock, pause, freeze, instructions -----------------------------------------------------------------------------

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 120);
    engine.Advance(0.25);
    Check(Near(engine.Time, 0.2, 1e-12), "Advance 0.25 s: two whole steps", $"time {engine.Time:R}");
    engine.Advance(0.05);
    Check(Near(engine.Time, 0.3, 1e-12), "Advance 0.05 s more: the rest makes a third step", $"time {engine.Time:R}");
    engine.Advance(10);
    Check(Near(engine.Time, 2.3, 1e-9), "Advance 10 s at once: at most 2 s made up", $"time {engine.Time:R}");

    engine.Paused = true;
    AircraftState before = engine.Get(a)!;
    engine.SetHeading(a, 180);
    engine.Advance(1);
    AircraftState after = engine.Get(a)!;
    Check(after.X == before.X && after.Y == before.Y && Near(engine.Time, 2.3, 1e-9) && after.TargetHeading == 180,
        "Paused: nothing moves, the instruction is taken", $"time {engine.Time:R} target {after.TargetHeading:R}");
    engine.Paused = false;

    engine.SetFrozen(a, true);
    before = engine.Get(a)!;
    engine.RunFor(5);
    after = engine.Get(a)!;
    Check(after.X == before.X && after.Y == before.Y && after.HeadingTrue == before.HeadingTrue && after.Frozen, "Frozen aircraft: holds position and heading");
    engine.SetFrozen(a, false);
    engine.RunFor(1);
    Check(engine.Get(a)!.HeadingTrue != before.HeadingTrue, "Unfrozen aircraft: goes on with its instruction");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 120);
    engine.SetHeading(a, 90);
    AircraftState s = engine.Get(a)!;
    Check(s.TargetHeading == 0, "Instruction queued: not carried out before the next step");
    engine.RunFor(0.1);
    s = engine.Get(a)!;
    Check(s.TargetHeading == 90 && s.HeadingTrue > 0 && s.HeadingTrue < 0.1, "Instruction queued: carried out at the start of the next step", $"heading {s.HeadingTrue:R}");
}

// --- Callsigns ---------------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 120);
    string b = engine.Add(0, 0, 3000, 0, 120);
    engine.CallsignPrefix = "atc";
    string c = engine.Add(0, 0, 3000, 0, 120);
    Check(a == "SIM1" && b == "SIM2" && c == "ATC3", "Callsigns given in order, prefix configurable", $"{a} {b} {c}");
    engine.SetSpeed(b, 200);
    bool renamed = engine.Rename(b, "TST22");
    engine.RunFor(0.1);
    AircraftState? renamedState = engine.Get("TST22");
    Check(renamed && renamedState != null && renamedState.TargetSpeedKt == 200 && engine.Get(b) == null,
        "Rename: new callsign, queued instruction follows the aircraft");
    Check(!engine.Rename(a, "ATC3"), "Rename refused when the callsign is taken");
    bool refused = false;
    try { engine.Add(0, 0, 3000, 0, 120, "sim1"); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "Add refuses a callsign already in the simulation (not case sensitive)");
    engine.Remove(a);
    Check(engine.Count == 2 && !engine.IsCallsignTaken("SIM1"), "Remove");
}

// --- Pilot reaction ----------------------------------------------------------------------------------------------

{
    SimEngine engine = new() { ReactionSeconds = 1 };
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 90);
    engine.RunFor(0.9);
    AircraftState s = engine.Get(a)!;
    Check(s.TargetHeading == 0 && s.HeadingTrue == 0 && s.PendingCommands == 1, "Reaction 1 s: nothing carried out after 0.9 s", $"pending {s.PendingCommands}");
    engine.RunFor(0.2);
    s = engine.Get(a)!;
    Check(s.TargetHeading == 90 && s.HeadingTrue > 0 && s.PendingCommands == 0, "Reaction 1 s: carried out by 1.1 s", $"heading {s.HeadingTrue:F2}");
}

{
    SimEngine engine = new() { ReactionSeconds = 1 };
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 10);
    engine.RunFor(0.5);
    engine.SetHeading(a, 20);
    engine.SetSpeed(a, 160);
    engine.RunFor(0.9);
    AircraftState s = engine.Get(a)!;
    Check(s.TargetHeading == 0 && s.PendingCommands == 2, "Reaction: a new heading replaces the one still waiting (wheel steps)", $"target {s.TargetHeading:R} pending {s.PendingCommands}");
    engine.RunFor(0.2);
    s = engine.Get(a)!;
    Check(s.TargetHeading == 20 && s.TargetSpeedKt == 160 && s.PendingCommands == 0, "Reaction: the last heading and the speed carried out 1 s after they were given");
}

// --- Vertical modes ----------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetVerticalSpeed(a, -700);
    engine.RunFor(60);
    AircraftState s = engine.Get(a)!;
    Check(Between(s.AltitudeFt, 2305, 2315) && s.VerticalMode == VerticalMode.Rate && s.VerticalSpeedFpm == -700, "Rate −700 ft/min kept: about 690 ft lower after 1 min (2 s to reach the rate)", $"altitude {s.AltitudeFt:F1}");
    engine.SetAltitude(a, 2500, 500);
    engine.RunFor(40);
    s = engine.Get(a)!;
    Check(s.AltitudeFt == 2500 && s.VerticalSpeedFpm == 0 && s.VerticalMode == VerticalMode.Altitude, "Then altitude 2500 at 500 ft/min: level by 40 s", $"altitude {s.AltitudeFt:R}");
}

{
    SimEngine engine = new();
    engine.SetGlidePath(0, 3);
    string a = engine.Add(0, -10, 3000, 0, 140);
    double best = -140 * SimEngine.FeetPerNM / 60 * Math.Tan(3 * Math.PI / 180);
    engine.SetGlidePathRate(a);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.VerticalSpeedFpm, best, 1e-9) && Near(s.BestVerticalSpeedFpm, best, 1e-9) && s.VerticalMode == VerticalMode.GlidePath,
        "Normal rate on a 3° glide path at 140 kt: about −743 ft/min", $"VS {s.VerticalSpeedFpm:F1}");
    engine.SetSpeed(a, 120);
    engine.RunFor(30);
    s = engine.Get(a)!;
    double slower = -120 * SimEngine.FeetPerNM / 60 * Math.Tan(3 * Math.PI / 180);
    Check(Near(s.VerticalSpeedFpm, slower, 1e-9), "Normal rate follows the speed: about −637 ft/min at 120 kt", $"VS {s.VerticalSpeedFpm:F1}");
    engine.SetHeading(a, 90);
    engine.RunFor(40);
    s = engine.Get(a)!;
    Check(Near(s.BestVerticalSpeedFpm, 0, 1e-6), "Normal rate flying across the final: no descent needed", $"best {s.BestVerticalSpeedFpm:F3}");
}

// --- Stick -------------------------------------------------------------------------------------------------------

{
    SimEngine engine = new() { ReactionSeconds = 2 };
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 300);
    engine.SetStick(a, 1, 0);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    Check(Between(s.HeadingTrue, 26, 28) && s.StickActive && s.PendingCommands == 0, "Stick right for 10 s: about 27° right (2 s to bank), the waiting heading cancelled", $"heading {s.HeadingTrue:F3}");
    engine.SetStick(a, 0, 0);
    engine.RunFor(10);
    s = engine.Get(a)!;
    Check(Between(s.HeadingTrue, 29.5, 30.5) && s.TurnRateDps == 0 && !s.StickActive, "Stick released: rolls out about 3° further and stays", $"heading {s.HeadingTrue:F3}");
    engine.SetStick(a, 0, -1);
    engine.RunFor(1);
    engine.SetStick(a, 0, 0);
    engine.RunFor(5);
    s = engine.Get(a)!;
    Check(Near(s.VerticalSpeedFpm, -600, 1e-6) && s.VerticalMode == VerticalMode.Rate, "Stick down 1 s: −600 ft/min, kept when released", $"VS {s.VerticalSpeedFpm:F3}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetTurnRateMode(a, TurnRateMode.Free);
    engine.SetStick(a, 1, 0);
    engine.RunFor(4);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.TurnRateDps, 6, 0.16), "Free turn rate: 6°/s after 4 s held at the edge (more than rate one)", $"rate {s.TurnRateDps:F2}");
    engine.SetTurnRateMode(a, TurnRateMode.Half);
    Check(engine.Get(a)!.SelectedTurnRateDps == 1.5, "Half rate selected: 1.5°/s for the instructions");
}

// --- Same instructions, same flight --------------------------------------------------------------------------------

{
    IReadOnlyList<AircraftState> Run()
    {
        SimEngine engine = new() { ReactionSeconds = 0.7 };
        engine.SetGlidePath(325, 3);
        engine.SetWind(290, 12, 8);
        string a = engine.Add(-3, -8, 2500, 45, 160);
        string b = engine.Add(4, -6, 3000, 300, 140, null, "Jet");
        engine.RunFor(12.3);
        engine.SetHeading(a, 200, TurnDirection.Right);
        engine.SetAltitude(b, 1800, 700);
        engine.RunFor(40);
        engine.SetSpeed(a, 120);
        engine.SetTurnRate(b, 1.5);
        engine.SetHeading(b, 150);
        engine.SetGlidePathRate(a);
        for (int i = 0; i < 300; i++) engine.Advance(0.137);
        return engine.Snapshot();
    }
    IReadOnlyList<AircraftState> first = Run();
    IReadOnlyList<AircraftState> second = Run();
    Check(first.SequenceEqual(second), "Same instructions twice (with wind and gusts): identical result, bit for bit");
}

// --- Local plane on the radar's Earth ----------------------------------------------------------------------------

{
    LocalPlane plane = new(ThresholdLatitude, ThresholdLongitude, RunwayHeading);
    double worst = 0;
    for (int i = 0; i < 16; i++)
    {
        double bearing = i * 22.5 * Math.PI / 180;
        foreach (double distance in new[] { 0.5, 5, 15, 30 })
        {
            double x = distance * Math.Sin(bearing);
            double y = distance * Math.Cos(bearing);
            (double latitude, double longitude) = plane.ToGeo(x, y);
            (double x2, double y2) = plane.FromGeo(latitude, longitude);
            worst = Math.Max(worst, Math.Sqrt((x2 - x) * (x2 - x) + (y2 - y) * (y2 - y)) * 1852);
        }
    }
    Check(worst < 0.01, "Plane to latitude/longitude and back: same point within 1 cm up to 30 NM", $"worst {worst:G3} m");

    // On the extended centreline of the plane: what the radar measures (Aircraft.LateralOffset and Distance in Data.cs).
    double approach = (RunwayHeading + 180) * Math.PI / 180;
    double worstLateral = 0;
    double worstAlong = 0;
    foreach (double distance in new[] { 0.5, 2, 5, 10, 20, 30 })
    {
        (double latitude, double longitude) = plane.ToGeo(distance * Math.Sin(approach), distance * Math.Cos(approach));
        double radius = Earth.Radius(ThresholdLatitude, RunwayHeading);
        double bearing = Earth.Bearing(ThresholdLatitude, ThresholdLongitude, latitude, longitude) * Math.PI / 180;
        double measured = Earth.DistanceNM(ThresholdLatitude, ThresholdLongitude, latitude, longitude, radius);
        double lateral = Math.Asin(Math.Sin(measured * 1852 / radius) * Math.Sin(bearing - RunwayHeading * Math.PI / 180)) * radius;
        double along = Math.Sqrt(Math.Max(0, measured * measured - lateral / 1852 * lateral / 1852));
        worstLateral = Math.Max(worstLateral, Math.Abs(lateral));
        worstAlong = Math.Max(worstAlong, Math.Abs(along - distance) * 1852);
    }
    Check(worstLateral < 0.001, "On the extended centreline: lateral offset measured by the radar is zero", $"worst {worstLateral:G3} m");
    Check(worstAlong < 0.01, "On the extended centreline: distance measured by the radar is the same", $"worst {worstAlong:G3} m");
}

// --- Routes ------------------------------------------------------------------------------------------------------

double Distance(AircraftState s, double x, double y) => Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));

{
    // Three points: north 5 NM, then east 5 NM descending and slowing, then south 5 NM.
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 120);
    engine.SetRoute(a, [new RoutePoint(0, 5, 3000, 140, "ONE"), new RoutePoint(5, 5, 2000, 120, "TWO"), new RoutePoint(5, 0, 2000, 120, "THREE")]);
    AircraftState s = engine.Get(a)!;
    Check(s.Route?.Count == 3 && s.RouteNext == 0 && s.RouteAltitude && s.RouteSpeed && s.TargetSpeedKt == 140 && s.TargetAltitudeFt == 3000,
        "Route given: three points, flying to the first, its speed at once", $"next {s.RouteNext} speed {s.TargetSpeedKt}");
    double[] nearest = [double.MaxValue, double.MaxValue, double.MaxValue];
    List<int> order = [0];
    bool descendingAtOnce = false;
    double secondsToTwo = double.NaN;
    for (int i = 0; i < 6000 && engine.Get(a)!.Navigation == NavigationMode.Route; i++)
    {
        engine.RunFor(0.1);
        s = engine.Get(a)!;
        nearest[0] = Math.Min(nearest[0], Distance(s, 0, 5));
        nearest[1] = Math.Min(nearest[1], Distance(s, 5, 5));
        nearest[2] = Math.Min(nearest[2], Distance(s, 5, 0));
        if (s.Navigation == NavigationMode.Route && s.RouteNext != order[^1]) order.Add(s.RouteNext);
        if (s.Navigation == NavigationMode.Route && s.RouteNext == 1 && double.IsNaN(secondsToTwo)) secondsToTwo = 0;
        if (!double.IsNaN(secondsToTwo) && secondsToTwo < 5)
        {
            secondsToTwo += 0.1;
            if (s.TargetAltitudeFt == 2000 && s.VerticalSpeedFpm < -100 && s.TargetSpeedKt == 120) descendingAtOnce = true;
        }
    }
    s = engine.Get(a)!;
    Check(string.Join(",", order) == "0,1,2" && s.Navigation == NavigationMode.Manual && s.RouteNext == 3, "Route: points flown in order, then MANUAL (the route kept)", $"order {string.Join(",", order)}");
    Check(nearest.All(d => d < 0.1), "Route: every point flown over (within 0.1 NM)", $"nearest {string.Join(" ", nearest.Select(d => d.ToString("F3")))}");
    Check(descendingAtOnce, "Route: past a point, descent and new speed for the next one start at once");
    engine.RunFor(120);
    s = engine.Get(a)!;
    Check(Near(s.HeadingTrue, 180, 0.01) && Near(s.AltitudeFt, 2000, 0.01) && Near(s.SpeedKt, 120, 0.01) && Near(s.X, 5, 0.05) && s.Y < -1,
        "After the last point: course of the last leg (180) kept, with its altitude and speed", $"heading {s.HeadingTrue:F2} x {s.X:F3} y {s.Y:F2} alt {s.AltitudeFt:F0}");
}

{
    // A point behind: it turns back to it, passes it (no circling) and keeps the course from where the route was given.
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetRoute(a, [new RoutePoint(0, -3, 3000, 150)]);
    double nearest = double.MaxValue;
    double seconds = 0;
    while (engine.Get(a)!.Navigation == NavigationMode.Route && seconds < 600)
    {
        engine.RunFor(0.1);
        seconds += 0.1;
        nearest = Math.Min(nearest, Distance(engine.Get(a)!, 0, -3));
    }
    engine.RunFor(60);
    AircraftState s = engine.Get(a)!;
    Check(seconds < 300 && nearest < 0.1 && Near(s.HeadingTrue, 180, 0.01), "Route: a point behind is reached after turning back, then course 180 kept",
        $"{seconds:F0} s nearest {nearest:F3} heading {s.HeadingTrue:F2}");
}

{
    // A point inside the turn: left behind flying straight, then turned to and flown over (never circled).
    SimEngine engine = new();
    string a = engine.Add(0, 0, 5000, 0, 250, null, "Jet");
    engine.SetRoute(a, [new RoutePoint(0.6, 0.2, 5000, 250)]);
    double seconds = 0;
    double nearest = double.MaxValue;
    while (engine.Get(a)!.Navigation == NavigationMode.Route && seconds < 600)
    {
        engine.RunFor(0.1);
        seconds += 0.1;
        nearest = Math.Min(nearest, Distance(engine.Get(a)!, 0.6, 0.2));
    }
    Check(seconds < 200 && nearest < 0.1, "Route: a point inside the turn radius is flown over, not circled", $"{seconds:F0} s nearest {nearest:F3}");
}

{
    // Crosswind from the west: the track is pointed at the point, then kept along the leg.
    SimEngine engine = new();
    engine.SetWind(270, 20, 0);
    string a = engine.Add(0, 0, 3000, 0, 120);
    engine.SetRoute(a, [new RoutePoint(0, 10, 3000, 120)]);
    double nearest = double.MaxValue;
    for (int i = 0; i < 4000 && engine.Get(a)!.Navigation == NavigationMode.Route; i++)
    {
        engine.RunFor(0.1);
        nearest = Math.Min(nearest, Distance(engine.Get(a)!, 0, 10));
    }
    engine.RunFor(120);
    AircraftState s = engine.Get(a)!;
    double expected = SimEngine.Normalize(-Math.Asin(20.0 / 120) * 180 / Math.PI);
    Check(nearest < 0.1 && Near(s.HeadingTrue, expected, 0.05) && Near(SimEngine.Wrap(s.TrackTrue), 0, 0.05) && Near(s.X, 0, 0.1),
        "Route in a crosswind: point reached, then track 360 kept heading into the wind", $"nearest {nearest:F3} heading {s.HeadingTrue:F2} track {s.TrackTrue:F2} x {s.X:F3}");
}

{
    // Last two points on the extended centreline (final course 360): the aircraft goes on along it, no other capture.
    SimEngine engine = new();
    engine.SetGlidePath(360, 3, 0, 1.5);
    string a = engine.Add(-4, -12, 3000, 90, 140);
    engine.SetRoute(a, [new RoutePoint(0, -10, 3000, 140), new RoutePoint(0, -6, 2000, 130)]);
    for (int i = 0; i < 4000 && engine.Get(a)!.Navigation == NavigationMode.Route; i++) engine.RunFor(0.1);
    engine.RunFor(60);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.HeadingTrue, 0, 0.01) || Near(s.HeadingTrue, 360, 0.01), "Route ending on the extended centreline: on along the final course", $"heading {s.HeadingTrue:F2}");
    Check(Near(s.X, 0, 0.1) && Near(s.AltitudeFt, 2000, 0.01) && s.VerticalMode == VerticalMode.Altitude,
        "Route ending on the extended centreline: on the centreline, level at the last altitude (no glide path capture)", $"x {s.X:F3} alt {s.AltitudeFt:F0}");
}

{
    // The route edited while flown, the same point next: the leg keeps its start, so the last course is the one of the leg.
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 45, 120);
    engine.SetRoute(a, [new RoutePoint(0, 10, 3000, 120, "END")]);
    engine.RunFor(20);
    engine.SetRoute(a, [engine.Get(a)!.Route![0]]);
    for (int i = 0; i < 4000 && engine.Get(a)!.Navigation == NavigationMode.Route; i++) engine.RunFor(0.1);
    engine.RunFor(60);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.HeadingTrue, 0, 0.01) || Near(s.HeadingTrue, 360, 0.01), "Route edited while flown, same point next: course of the leg from where it started", $"heading {s.HeadingTrue:F2}");

    // Values of the next point changed while flown: same leg; a point passed meanwhile and given again is not flown again.
    string b = engine.Add(10, 0, 3000, 0, 120);
    RoutePoint one = new(10, 2, 3000, 120, "ONE");
    RoutePoint two = new(10, 6, 3000, 120, "TWO");
    engine.SetRoute(b, [one, two]);
    engine.RunFor(10);
    engine.SetRoute(b, [one with { AltitudeFt = 2500 }, two]);
    s = engine.Get(b)!;
    Check(s.Route != null && s.RouteNext == 0 && s.TargetAltitudeFt == 2500, "Next point's altitude changed while flown: given at once", $"target {s.TargetAltitudeFt:F0}");
    for (int i = 0; i < 2000 && engine.Get(b)!.RouteNext == 0; i++) engine.RunFor(0.1);
    IReadOnlyList<RoutePoint> flown = engine.Get(b)!.Route!;
    engine.SetRoute(b, flown);
    s = engine.Get(b)!;
    Check(s.Route?.Count == 1 && s.Route[0].Name == "TWO", "Route given again with a point already passed: that point is left out", $"points {s.Route?.Count}");
    engine.SetHeading(b, 90);
    engine.SetRoute(b, [s.Route![0]]);
    engine.RunFor(0.2);
    Check(engine.Get(b)!.Navigation == NavigationMode.Manual && engine.Get(b)!.Route != null, "Route edited with the same place next: an instruction still waiting is kept (here a heading: MANUAL)");
}

{
    // Instructions and the route.
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 120);
    engine.SetRoute(a, [new RoutePoint(0, 3, 4000, 150), new RoutePoint(3, 3, 5000, 160)]);
    engine.SetAltitude(a, 2500);
    engine.SetSpeed(a, 110);
    engine.RunFor(0.2);
    AircraftState s = engine.Get(a)!;
    Check(s.Route != null && !s.RouteAltitude && !s.RouteSpeed && s.TargetAltitudeFt == 2500 && s.TargetSpeedKt == 110,
        "Altitude and speed instructions take them over, the route goes on", $"target {s.TargetAltitudeFt:F0} ft {s.TargetSpeedKt:F0} kt");
    for (int i = 0; i < 3000 && engine.Get(a)!.RouteNext == 0; i++) engine.RunFor(0.1);
    engine.RunFor(1);
    s = engine.Get(a)!;
    Check(s.Route != null && s.RouteNext == 1 && s.TargetAltitudeFt == 2500 && s.TargetSpeedKt == 110,
        "Past a point, an altitude and speed taken over are kept", $"target {s.TargetAltitudeFt:F0} ft {s.TargetSpeedKt:F0} kt");
    engine.SetHeading(a, 270);
    engine.RunFor(0.2);
    s = engine.Get(a)!;
    Check(s.Navigation == NavigationMode.Manual && s.Route != null && s.TargetHeading == 270, "A heading instruction: MANUAL, the route kept", $"target {s.TargetHeading:F0}");

    string b = engine.Add(10, 0, 3000, 0, 120);
    engine.SetRoute(b, [new RoutePoint(10, 5, double.NaN, double.NaN)]);
    s = engine.Get(b)!;
    Check(s.TargetAltitudeFt == 3000 && s.TargetSpeedKt == 120, "Route point without altitude and speed: they are kept");
    engine.SetStick(b, 0.5, 0);
    engine.RunFor(0.1);
    engine.SetStick(b, 0, 0);
    Check(engine.Get(b)!.Navigation == NavigationMode.Manual && engine.Get(b)!.Route != null, "The stick sideways: MANUAL, the route kept");

    string c = engine.Add(20, 0, 3000, 0, 120);
    engine.SetHeading(c, 90);
    engine.SetRoute(c, [new RoutePoint(20, 5, 3000, 120)]);
    engine.RunFor(5);
    s = engine.Get(c)!;
    Check(s.Route != null && s.PendingCommands == 0 && Near(s.HeadingTrue, 0, 0.01), "A route drops the instructions still waiting", $"heading {s.HeadingTrue:F2}");
    engine.SetFrozen(c, true);
    double y = engine.Get(c)!.Y;
    engine.RunFor(10);
    Check(engine.Get(c)!.Y == y && engine.Get(c)!.Route != null, "Frozen: the route waits");
    engine.SetRoute(c, []);
    Check(engine.Get(c)!.Route == null && engine.Get(c)!.RouteNext == -1, "Empty route: no route");
}

{
    // MANUAL / ROUTE, direct to, next point.
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    RoutePoint p1 = new(0, 5, 3000, 150, "P1");
    RoutePoint p2 = new(5, 10, 3000, 150, "P2");
    RoutePoint p3 = new(5, 15, 3000, 150, "P3");
    engine.SetRoute(a, [p1, p2, p3], follow: null);
    AircraftState s = engine.Get(a)!;
    Check(s.Navigation == NavigationMode.Manual && s.Route?.Count == 3, "A route given with follow null to an aircraft without a route: MANUAL, route kept", $"{s.Navigation}");
    Check(engine.SetNavigation(a, true) && engine.Get(a)!.Navigation == NavigationMode.RouteArmed, "ROUTE chosen: waiting for a point");
    engine.SetHeading(a, 0);
    engine.RunFor(0.2);
    Check(engine.Get(a)!.Navigation == NavigationMode.Manual, "A heading in ROUTE waiting: MANUAL");
    engine.SetNavigation(a, true);
    engine.RunFor(20);
    s = engine.Get(a)!;
    Check(s.Navigation == NavigationMode.RouteArmed && Near(s.HeadingTrue, 0, 1e-9), "ROUTE waiting for a point: the heading is kept", $"heading {s.HeadingTrue:F2}");
    engine.NextWaypoint(a);
    engine.RunFor(0.2);
    s = engine.Get(a)!;
    Check(s.Navigation == NavigationMode.Route && s.RouteNext == 0, "Next WPT while waiting: the next point not passed (P1)", $"next {s.RouteNext}");
    engine.NextWaypoint(a);
    engine.RunFor(0.2);
    s = engine.Get(a)!;
    Check(s.RouteNext == 1, "Next WPT while flying the route: the point after (P2)", $"next {s.RouteNext}");
    engine.DirectTo(a, 2);
    engine.RunFor(0.2);
    Check(engine.Get(a)!.RouteNext == 2, "Direct to a point of the route: P3");
    RoutePoint off = new(-5, 10, double.NaN, double.NaN, "OFF");
    engine.DirectToPoint(a, off);
    engine.RunFor(0.2);
    s = engine.Get(a)!;
    Check(s.Route?.Count == 4 && s.RouteNext == 2 && s.Route[2].Name == "OFF" && s.Route[3].Name == "P3", "Direct to a point off the route: it becomes the next point, then the rest", $"next {s.RouteNext}");
    double nearest = double.MaxValue;
    for (int i = 0; i < 9000 && engine.Get(a)!.Navigation == NavigationMode.Route; i++)
    {
        engine.RunFor(0.1);
        nearest = Math.Min(nearest, Distance(engine.Get(a)!, -5, 10));
    }
    s = engine.Get(a)!;
    Check(nearest < 0.1 && s.Navigation == NavigationMode.Manual && s.RouteNext == 4, "Off-route point flown over, then the rest, then MANUAL", $"nearest {nearest:F3} next {s.RouteNext}");
    engine.DirectTo(a, 0);
    engine.RunFor(0.2);
    Check(engine.Get(a)!.Navigation == NavigationMode.Route && engine.Get(a)!.RouteNext == 0, "Direct to a point already passed: flown again");
    Check(engine.SetNavigation(a, false) && engine.Get(a)!.Navigation == NavigationMode.Manual && engine.Get(a)!.Route?.Count == 4, "MANUAL chosen: the route kept");

    // The instructor's altitude rules from then on, also when the route is edited.
    string b = engine.Add(20, 0, 3000, 0, 150);
    engine.SetRoute(b, [new RoutePoint(20, 5, 5000, 150), new RoutePoint(20, 10, 6000, 150)]);
    engine.SetAltitude(b, 4000);
    engine.RunFor(0.2);
    engine.SetRoute(b, engine.Get(b)!.Route!.Select(p => p with { AltitudeFt = 7000 }).ToList());
    s = engine.Get(b)!;
    Check(!s.RouteAltitude && s.TargetAltitudeFt == 4000, "Altitude given: the route's altitudes no more, also after the route is changed", $"target {s.TargetAltitudeFt:F0}");
    Check(!engine.SetNavigation(engine.Add(30, 0, 3000, 0, 150), true), "ROUTE without a route: not possible");
}

{
    // Stop turn, stop climb / descent.
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 90);
    engine.RunFor(10);
    double atStop = engine.Get(a)!.HeadingTrue;
    engine.StopTurn(a);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    double settled = s.HeadingTrue;
    engine.RunFor(10);
    Check(s.TurnRateDps == 0 && settled > atStop && settled - atStop < 5 && engine.Get(a)!.HeadingTrue == settled && s.Navigation == NavigationMode.Manual,
        "Stop turn: rolls out a few degrees after, then holds that heading", $"stop at {atStop:F1}, holds {settled:F1}");

    string b = engine.Add(10, 0, 3000, 0, 150);
    engine.SetAltitude(b, 6000, 1000);
    engine.RunFor(30);
    double altitudeAtStop = engine.Get(b)!.AltitudeFt;
    engine.StopVertical(b);
    engine.RunFor(20);
    s = engine.Get(b)!;
    double level = s.AltitudeFt;
    engine.RunFor(20);
    Check(s.VerticalSpeedFpm == 0 && level > altitudeAtStop && level - altitudeAtStop < 60 && engine.Get(b)!.AltitudeFt == level,
        "Stop climb: levels off just above where it was, then holds it", $"stop at {altitudeAtStop:F0}, holds {level:F0}");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"All {checks} checks passed." : $"{failures} of {checks} checks FAILED.");
return failures == 0 ? 0 : 1;
