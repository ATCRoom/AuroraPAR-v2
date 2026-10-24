# Instructor data files (open format)

The Instructor of AuroraPAR keeps its data in plain JSON files, so they can be read, changed with any text editor,
shared, and used by other programs (for example a future tower circuit simulator built on the same engine,
*ATCRoom Sim*).

## Where they are

In the folder `Instructor` beside the settings file of AuroraPAR:

- normally `%AppData%\AuroraPAR\Instructor\` (Windows);
- in portable mode (a file `AuroraPAR.settings.json` next to `AuroraPAR.exe`), the folder `Instructor` next to the program.

| File | What it holds | Since |
| --- | --- | --- |
| `performance.json` | the performance classes of the simulated aircraft | 2.0.0 |
| `start-positions.json` | the start positions *(planned)* | — |

## Rules common to all the files

- Text in UTF-8, JSON. Comments (`//` and `/* */`) and trailing commas are accepted when reading.
- Every file has a `"FormatVersion"` (a whole number). A file with a **higher** version than the program knows is
  used as far as the program understands it and is **never overwritten**.
- Fields the program does not know are **kept** when it writes the file again.
- A missing file is created with the defaults. A file that cannot be read is kept as `<name>.bad` and the defaults
  are written.
- Values outside sensible limits are brought back inside them when read (the file itself is not changed).
- Units: speeds in knots (true airspeed), vertical speeds in ft/min, accelerations per second, angles in degrees.

## performance.json

What each kind of aircraft can do. In the Trainer it is changed in the window **Aircraft performance…** (Simulation
settings, or "…" beside a Class box): the new values are for the aircraft created from then on; aircraft already
flying keep their class. It can also be changed by hand (then: for the aircraft created after the Instructor window
is opened again).

```json
{
  "FormatVersion": 1,
  "Classes": [
    {
      "Name": "Turboprop",
      "MinSpeedKt": 90,
      "MaxSpeedKt": 280,
      "ApproachSpeedKt": 140,
      "FinalSpeedKt": 110,
      "MaxClimbFpm": 2000,
      "MaxDescentFpm": 3000,
      "AccelerationKtPerSecond": 1.5,
      "DecelerationKtPerSecond": 1,
      "VerticalAccelerationFpmPerSecond": 400,
      "MaxBankDegrees": 30
    }
  ]
}
```

| Field | Meaning | Limits when read |
| --- | --- | --- |
| `Name` | name shown in the Instructor window; unique (not case sensitive) | not empty |
| `MinSpeedKt` / `MaxSpeedKt` | lowest and highest speed an instruction can give | 30–400 / minimum + 10 to 600 |
| `ApproachSpeedKt` | initial speed: default speed of a new aircraft of this class (and of the first point of a new flight plan) | between minimum and maximum |
| `FinalSpeedKt` | speed on the final ("Slow dwn to final"); missing in a file written before 24 Oct 2026: added (Light 70, Turboprop 110, Jet 140, others a third of the way from the minimum to the initial speed), and Turboprop 120 → 140, Jet 140 → 180 where they were never changed | between minimum and initial speed |
| `MaxClimbFpm` / `MaxDescentFpm` | largest climb and descent rates (both positive) | 200–8000 |
| `AccelerationKtPerSecond` / `DecelerationKtPerSecond` | how fast the speed changes | 0.2–10 |
| `VerticalAccelerationFpmPerSecond` | how fast the vertical speed changes (also the smoothness of the level-off) | 50–2000 |
| `MaxBankDegrees` | bank limit of the turns: the turn rate is lower at high speed | 10–60 |

Default classes:

| Name | Speeds (min / final / initial / max) | Climb / descent | Accel. / decel. | Vertical accel. | Bank |
| --- | --- | --- | --- | --- | --- |
| Light | 60 / 70 / 90 / 160 | 800 / 1500 | 2 / 1.5 | 400 | 30 |
| Turboprop | 90 / 110 / 140 / 280 | 2000 / 3000 | 1.5 / 1 | 400 | 30 |
| Jet | 120 / 140 / 180 / 350 | 3000 / 4000 | 1.5 / 1 | 300 | 30 |

A new aircraft gets the class chosen in the window; without one (or with a name that is not in the file) the
`Turboprop`, or the first class of the file. The Instructor+ proposes `Jet` for new aircraft and flight plans.

## How the engine flies (for the people who change the values)

- **Turns**: the turn rate grows and shrinks by 1.5°/s per second (rate one in 2 s) and the roll-out starts in
  time to stop on the heading. The rate is the one chosen (1.5 or 3°/s) but never more than the bank limit allows:
  rate = g · tan(bank) / speed (at 250 kt and 30° about 2.5°/s). With the stick in *Free* the bank limit is 60°.
- **Vertical**: the vertical speed changes by `VerticalAccelerationFpmPerSecond`; towards an altitude the rate
  is reduced in time to level off on it.
- **Wind**: one direction and speed for all heights, plus random gusts; the aircraft flies its heading through the
  air and drifts. The *Normal* rate uses the ground speed along the final.
