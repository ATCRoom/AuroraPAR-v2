# Tower circuit simulator (instructor / student) — idea for a new project

**Status (30 Oct 2026): STARTING NOW, in a new chat (decided by the user).** Work rules: the same as `CLAUDE.md`
(Italian, propose first and write code only after an explicit go, builds only through GitHub Actions, independent
review for big work, checklist with Reject / Frozen). AuroraPAR v2 Trainer 1.0.0 is ready on the branch `trainer` and
waits only for the user's tests and the merge into `master` (followed in the old chat, see `TODO.md`): **do not touch
`master` or the Trainer release from the new project.** First step: answer the questions below and make a few
mock-ups, no code.

## Starting point: what already exists (AuroraPAR v2 Trainer, repository ATCRoom/AuroraPAR-v2, branch `trainer`)

Two libraries without WPF, already separate projects (net9.0) — the natural base:

- **`Sim/Engine` = ATCRoom.Sim** (`SimEngine.cs`, `SimAircraft.cs`, `PerformanceClass.cs`, `LocalPlane.cs`,
  `MetarWind.cs`; links the root `Earth.cs`) with its self-test `Sim/SelfTest` (run by the CI). Flight model in a local
  plane around a reference runway: heading / altitude / speed / rate commands with pilot reaction, turn rates, wind and
  gusts, pause (all or one aircraft), ROUTE (waypoints with altitude and speed, Direct to, Next WPT) and MANUAL modes,
  "Slow dwn to final", performance classes editable by the user (`performance.json`: Light, Turboprop, Jet; min /
  final / initial / max speeds...). The "manual mode" and the "pre-set routes" of this project already exist in it as
  MANUAL / ROUTE; missing: circuit legs built from the runway, landing / touch-and-go / go-around, helicopters (hover,
  slow speeds), ground.
- **`MapCreator/Core` = ATCRoom.Maps** (open map format `docs/MAP-FORMAT.md`, one file per airport, categories
  incl. ATZ / CTR / VRP / FIX; importers IVAO / Aurora, EuroScope `.ese` / `.sct`, KML / KMZ, AIP PDF tables) with
  its self-test, and the program **ATCRoom Map Creator** (`MapCreator/App`). Maps: personal use only, never
  distributed.

WPF parts of the Trainer (folder `Trainer/`, compiled with the root files of AuroraPAR v2 under `#if TRAINER`), to
reuse by copy or by moving them into a shared library (to decide):

- **Top view** `PlanView.cs` (+ `.Labels.cs`, `.RouteEdit.cs`): maps by airport and category with symbols, range
  rings, runway and centreline, tracks with history dots, labels (drag, hide, larger label with clickable / wheel
  fields), CENTRE modes (FREE, MOUSE, AIRPORT, TRAFFIC), routes drawn and edited by click / drag, points snapped to
  VRP / FIX. `MapLibrary.cs`, `MapsWindow.cs`, `CustomSymbols.cs`.
- **Instructor station** `InstructorWindow*.cs` (root `InstructorWindow.cs` + `Trainer/InstructorWindow.*.cs`):
  floating panels (`FloatingPanel.cs`), flight plans and scenarios (`FlightPlans.cs`, `.Fpl.cs`), TRAFFIC LIST, aircraft
  control, commands on the label, keys / macro keyboard (`.Keys.cs`), performance table (`PerformanceWindow.cs`), QNH
  and METAR from the internet (`.Qnh.cs`, `OnlineMetar.cs`). `InstructorBridge.cs` (root): engine → traffic, the
  "Aurora-like" positions.
- **Lesson between two PCs** `LessonLink.cs`, `LessonWindow.cs`, `MainWindow.Lesson.cs`: public MQTT relays
  (`CoordinationLink.Brokers` in `Coordination.cs`), channel from a lesson code, JSON messages both ways.
- Build and release pattern: `.github/workflows/build.yml` (self-tests, single-file publish, releases with their own
  tag prefix and "not latest"), own icon linked as `AuroraPAR.ico`, portable settings file.
- Docs and tutorials style: `docs/TRAINER.md`, `docs/tutorials/`.

## Questions to answer first (before any code)

1. **Name** of the program and **repository**: a new repository in the ATCRoom organisation, or a new folder / branch
   in ATCRoom/AuroraPAR-v2?
2. **Sharing with the Trainer:** move ATCRoom.Sim / ATCRoom.Maps (and the top view) into a shared place that both
   use, or copy them into the new project (simpler, but two copies to keep)?
3. **What the student sees** exactly: the top view of the aerodrome as in Aurora (which map categories, labels, tags),
   anything else (strips, wind, QNH, ATIS)?
4. **The instructor's "two levels"**: what each one shows (mock-ups first).
5. **Circuit routes**: built automatically from the runway in use (left / right circuit, circuit height, downwind
   distance) or drawn by the instructor per aerodrome? Which legs and actions (joining, downwind, base, final,
   touch-and-go, full stop, go-around, departure, orbit, extend downwind, hold)?
6. **Aircraft database**: which types / classes (aeroplanes slow and fast, helicopters), which data, how the user adds
   types (the performance table of the Trainer is a start).
7. **IVAO**: is the simulated traffic ever mixed with IVAO traffic (as in the Trainer: only between instructor and
   student, never on the network)? Does it work without Aurora (as the Trainer)?

## The idea

A simulation environment for training **tower control of traffic in the circuit**: the **instructor** flies the aircraft, the **student** controls them as a tower controller.

- **Base (decided 13 Oct):** the shared engine library of the Instructor (*ATCRoom Sim*: flight model, scenarios, instructor controls, top view, link, open data format) + its own tower view; **not** the PAR radar views. Originally: possibly the same foundation as AuroraPAR and AuroraPAR Instructor (WPF program, test traffic flight model, instructor/student link through the coordination relay with a session code, same PC / two PCs / over the network).
- **Views:**
  - **Student:** only the view "from above", as Aurora for IVAO (radar-like top view of the aerodrome and its circuit).
  - **Instructor:** views on **two levels**, for a wider picture (e.g. the same top view plus a wider area / a second level of detail — to be defined).
- **Aircraft database:** aeroplanes and **helicopters**, **slow and fast** (performance per type: speeds in the circuit, climb / descent rates, turn rates; helicopters with their own manoeuvres such as hover).
- **Pre-set routes:** each aircraft starts with a **pre-set route** (e.g. joining, downwind, base, final, touch-and-go, departure, circuit...) and flies it by itself.
- **Go around key** (idea from the PAR test traffic, not needed there): the aircraft climbs away on the missed approach / circuit.
- **Manual mode:** when needed the instructor **switches an aircraft to manual** and flies it to do manoeuvres not in the pre-set route (orbit, extend downwind, go-around, hold a position, ...), then back to a route.

## To decide when we start (older list, merged into the questions above)

- Name of the project and its repository (probably in the dedicated GitHub organisation, see TODO.md).
- What is shared with AuroraPAR (code library or copy) and what is new.
- What the student sees exactly (labels, data from Aurora-like tags, aerodrome map, runways and circuit drawing).
- How the instructor's two levels look (mockups first).
- The aircraft database: which types, which data, how the user can add types.
- Pre-set routes: how they are defined and edited (per aerodrome and runway in use).
- Whether the simulated traffic is ever mixed with IVAO traffic (as for the PAR Instructor: only between instructor and student, never on the network).
