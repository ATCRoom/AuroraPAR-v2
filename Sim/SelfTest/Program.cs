using System.Globalization;
using ATCRoom.Sim;
using AuroraPAR;

// Self-test of the simulation engine (ATCRoom.Sim). Run by the GitHub build: any failure gives exit code 1.
// Times in the checks follow from the fixed steps of 0.1 s: a command is carried out at the start of the next step.

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

// A runway like LIBN 32 (threshold of runways.par, heading for AuroraPAR).
const double ThresholdLatitude = 40.23227088;
const double ThresholdLongitude = 18.13907997;
const double RunwayHeading = 325.80;

// --- Turns -------------------------------------------------------------------------------------------------------

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 90);
    engine.RunFor(29.5);
    AircraftState s = engine.Get(a)!;
    Check(s.HeadingTrue > 88 && s.HeadingTrue < 89 && Near(s.TurnRateDps, 3, 1e-9), "Turn right 90° at 3°/s: still turning at 29.5 s", $"heading {s.HeadingTrue:F3}");
    engine.RunFor(0.5);
    s = engine.Get(a)!;
    Check(s.HeadingTrue == 90 && s.TurnRateDps == 0, "Turn right 90° at 3°/s: exactly on 090 at 30 s", $"heading {s.HeadingTrue:R}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetHeading(a, 90, TurnDirection.Left);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.HeadingTrue, 330, 1e-6) && s.TurnRateDps < 0, "Turn to 090 forced left: through 330 after 10 s", $"heading {s.HeadingTrue:F3}");
    engine.RunFor(80);
    s = engine.Get(a)!;
    Check(s.HeadingTrue == 90 && s.TurnRateDps == 0 && s.TurnDirection == TurnDirection.Shortest, "Turn to 090 forced left: on 090 after 90 s, side back to shortest", $"heading {s.HeadingTrue:R}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 350, 150);
    engine.SetHeading(a, 10);
    engine.RunFor(1);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.HeadingTrue, 353, 1e-6), "Shortest turn across north: 350 to 010 turns right", $"heading {s.HeadingTrue:F3}");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 150);
    engine.SetTurnRate(a, 1.5);
    engine.SetHeading(a, 30);
    engine.RunFor(10);
    AircraftState s = engine.Get(a)!;
    Check(Near(s.HeadingTrue, 15, 1e-6), "Half rate turn: 15° in 10 s", $"heading {s.HeadingTrue:F3}");
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
    for (int i = 0; i < 30; i++)
    {
        engine.RunFor(0.1);
        highest = Math.Max(highest, engine.Get(a)!.AltitudeFt);
    }
    s = engine.Get(a)!;
    Check(s.AltitudeFt == 3000 && s.VerticalSpeedFpm == 0 && highest <= 3000, "Climb 1000 ft at 1000 ft/min: level at 3000 by 62 s, never above", $"highest {highest:R}");
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

// --- Movement ----------------------------------------------------------------------------------------------------

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

// --- Clock, pause, freeze, commands -------------------------------------------------------------------------------

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
        "Paused: nothing moves, the command is taken", $"time {engine.Time:R} target {after.TargetHeading:R}");
    engine.Paused = false;

    engine.SetFrozen(a, true);
    before = engine.Get(a)!;
    engine.RunFor(5);
    after = engine.Get(a)!;
    Check(after.X == before.X && after.Y == before.Y && after.HeadingTrue == before.HeadingTrue && after.Frozen, "Frozen aircraft: holds position and heading");
    engine.SetFrozen(a, false);
    engine.RunFor(1);
    Check(engine.Get(a)!.HeadingTrue != before.HeadingTrue, "Unfrozen aircraft: goes on with its command");
}

{
    SimEngine engine = new();
    string a = engine.Add(0, 0, 3000, 0, 120);
    engine.SetHeading(a, 90);
    AircraftState s = engine.Get(a)!;
    Check(s.TargetHeading == 0, "Command queued: not carried out before the next step");
    engine.RunFor(0.1);
    s = engine.Get(a)!;
    Check(s.TargetHeading == 90 && Near(s.HeadingTrue, 0.3, 1e-9), "Command queued: carried out at the start of the next step", $"heading {s.HeadingTrue:R}");
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
        "Rename: new callsign, queued command follows the aircraft");
    Check(!engine.Rename(a, "ATC3"), "Rename refused when the callsign is taken");
    bool refused = false;
    try { engine.Add(0, 0, 3000, 0, 120, "sim1"); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "Add refuses a callsign already in the simulation (not case sensitive)");
    engine.Remove(a);
    Check(engine.Count == 2 && !engine.IsCallsignTaken("SIM1"), "Remove");
}

// --- Same commands, same flight ----------------------------------------------------------------------------------

{
    IReadOnlyList<AircraftState> Run()
    {
        SimEngine engine = new();
        string a = engine.Add(-3, -8, 2500, 45, 160);
        string b = engine.Add(4, -6, 3000, 300, 140);
        engine.RunFor(12.3);
        engine.SetHeading(a, 200, TurnDirection.Right);
        engine.SetAltitude(b, 1800, 700);
        engine.RunFor(40);
        engine.SetSpeed(a, 120);
        engine.SetTurnRate(b, 1.5);
        engine.SetHeading(b, 150);
        for (int i = 0; i < 300; i++) engine.Advance(0.137);
        return engine.Snapshot();
    }
    IReadOnlyList<AircraftState> first = Run();
    IReadOnlyList<AircraftState> second = Run();
    Check(first.SequenceEqual(second), "Same commands twice: identical result, bit for bit");
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

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"All {checks} checks passed." : $"{failures} of {checks} checks FAILED.");
return failures == 0 ? 0 : 1;
