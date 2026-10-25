# AuroraPAR v2 Trainer — Instructor+ guide

The Trainer is AuroraPAR v2 with three additions for lessons: the surveillance radar **RDR** (a plan view with
maps), the **Instructor+** (an instructor's station that flies simulated aircraft for the student) and **ATCRoom Map
Creator** (it converts map files for the RDR). Everything about the PAR itself (views, labels, settings, profiles,
runways) is in the AuroraPAR v2 manual, `MANUAL.md`, beside the program.

The simulated aircraft are never sent to IVAO: they exist only in the Trainer. The flight is computed by the
Instructor+ with its own performance table; the student's PAR shows the aircraft as normal traffic, with **SIM
TRAFFIC** at the top of the views.

## Contents

1. [Opening the Instructor+](#1-opening-the-instructor)
2. [The window and the floating panels](#2-the-window-and-the-floating-panels)
3. [The radar of the instructor](#3-the-radar-of-the-instructor)
4. [Commands on the label](#4-commands-on-the-label)
5. [Routes on the radar](#5-routes-on-the-radar)
6. [SIMULATION SETTINGS](#6-simulation-settings)
7. [TRAFFIC GENERATION](#7-traffic-generation)
8. [TRAFFIC LIST](#8-traffic-list)
9. [AIRCRAFT CONTROL](#9-aircraft-control)
10. [TRAINEE PAR](#10-trainee-par)
11. [Aircraft performance](#11-aircraft-performance)
12. [Keys and macro keyboard](#12-keys-and-macro-keyboard)
13. [A lesson on one PC](#13-a-lesson-on-one-pc)

---

## 1. Opening the Instructor+

Button **Instructor+ (⇧I)** in the main window, or **Shift+I**. Closing its window removes the simulated aircraft
(the flight plans are lost too: save a scenario first if you want to keep them).

## 2. The window and the floating panels

The instructor's radar fills the whole window. Over it float five panels:

| Panel | What it is for |
| --- | --- |
| **SIMULATION SETTINGS** | runway, wind, QNH / QFE, pilot reaction, callsign prefix, Aircraft performance… |
| **TRAFFIC GENERATION** | new aircraft: Quick starts, flight plans, scenarios, the route editor |
| **TRAFFIC LIST** | the aircraft flying and those waiting to start; Start all, PAUSE ALL, Remove all aircraft |
| **AIRCRAFT CONTROL** | the commands of the selected aircraft |
| **TRAINEE PAR** | the student's PAR in small, to follow the approach |

- **Move** a panel by dragging its blue bar; **▴ / ▾** closes it to the bar alone or opens it again.
- **Double-click on the bar**: back to its first place.
- A panel always stays inside the window; when its content is taller than the room under it, it scrolls.
- The TRAINEE PAR is **resized** from its bottom right corner.
- Places, open / closed and the size of the TRAINEE PAR are kept for the next time.
- A click on a panel brings it in front of the others.

## 3. The radar of the instructor

- **Wheel**: range. **CENTRE** (top right): **MOUSE** (the wheel zooms at the pointer, the right button drags the
  view, a double-click goes back to the airport), **AIRPORT** (centred on the airport), **TRAFFIC** (the selected
  aircraft always in the middle).
- **FREE** (left of CENTRE): no centre, the view stays where you leave it. Drag it with the left button (the right one
  works too), the wheel zooms at the pointer; no double-click (AIRPORT goes back to the airport). Clicks on tracks,
  labels and route points work as usual; while adding points by click, a still click adds a point and a drag moves
  the view. The RDR of the student has FREE too.
- **BEAM** (beside CENTRE, on / off, kept): where the student's PAR sees the aircraft, seen from above. Filled: the
  azimuth beam; dashed: the azimuth scan limits; from the antenna (middle of the runway) to the end of the range. It
  follows the antenna when the student tilts it. Useful to place the aircraft of a scenario just outside the beam, so
  the student sees them come in. The height limits are not in a plan view: see them in the TRAINEE PAR.
- **Click on a track** (or on its label): selects the aircraft.
- **Labels**: drag them with the left button (a line joins them to the track); double-click: back to their place.
- Range rings around the airport, the maps chosen with **Maps…** of the RDR, the other airports of `runways.par`
  in grey.
- Aircraft **waiting** in the TRAFFIC LIST: a grey dashed square where each will start, with its name, and its route
  dim. The student does not see them.

## 4. Commands on the label

The selected aircraft has a larger label, with the values given by the instructor in blue:

```
PONY01 J  ROUTE
025↓ 020  220 kt
H243  →ALFA  -650
```

Line 1: callsign, class, ROUTE / MANUAL. Line 2: altitude now (hundreds of ft) with ↑↓, cleared altitude, speed.
Line 3: heading, the next point, the rate.

- **Click on a value** (heading, cleared altitude, speed, point, rate): a list opens beside it; a click (or the arrows
  and Enter) gives the value; or type it and press **Go**; **Esc** closes. The lists have their buttons too:
  ◀5 / 5▶, STOP TURN, Final CRS (heading); STOP CLIMB / DESCENT (altitude); Normal, STOP (rate); **Final (n)** (speed:
  the final speed of the class); Next WPT, MANUAL, Edit route… (point).
- **Wheel over a value**: one step (heading 5°, altitude 100 ft, speed 5 kt, rate 100 ft/min).
- **Right click on an aircraft** (label or track): Pause / Unpause, Reactivate, ROUTE / MANUAL, Edit route…, Rename…,
  Hide label, Remove.

All commands are carried out after the **pilot reaction** (SIMULATION SETTINGS), as real pilots are not instantaneous.

## 5. Routes on the radar

- **Add by click** (route editor): a click on the radar adds a point when the button is let go. Near a point of the
  maps (VRP, FIX, VOR, NDB, airport) a menu asks: that point (it takes its exact place and name) or a **GPS point**
  (bearing / distance from the threshold). Far from everything: the GPS point at once.
- **Drag a point** of the selected route (the flight plan being made, or the selected aircraft, also while it flies):
  the cursor becomes ✥ over a point. The drop has the same menu near a map point. Altitude and speed of the point
  stay. If the aircraft is flying to that point, it turns at once to the new place.
- **Right click on a point**: **Remove WPT**; for an aircraft also **Direct to this point** and **Edit route…**.

## 6. SIMULATION SETTINGS

On its bar: a summary (runway, wind, QNH / QFE, reaction). Inside:

- **Runway**: the one of the radar (while no simulated aircraft fly; while they fly it stays, so they stay where they
  are if the student changes runway).
- **QNH / QFE** (the altitudes typed and shown), **Aurora-like data** (positions every half second and altitude in
  steps, as from Aurora), **Callsign prefix** (SIM → SIM1, SIM2…).
- **Wind**: from (degrees true, as in the METAR; the magnetic value beside it), speed, gusts; **From METAR** copies
  the wind of the METAR received from Aurora.
- **Pilot reaction**: 0–5 s.
- **Aircraft performance…**: see [11](#11-aircraft-performance).

## 7. TRAFFIC GENERATION

**Quick (starts)**: an aircraft at once from a start: **On final**, **30° / 45° / 90°** from the left or the right
(aimed at the point of the centreline at the distance), **Downwind L / R**. Distance from touchdown, distance off the
centreline, altitude (empty: on the glide path), speed, class (**…** opens Aircraft performance), callsign (empty: the
next free one). Then:

- **Create & start**: it starts flying now, level, in MANUAL;
- **Move to TFC list**: it waits in the TRAFFIC LIST until you start it.

**Advanced (flight plans, scenarios)**:

- **New FPL** makes a plan (callsign, class); its route in the **route editor** below: points by click on the radar,
  by the name of a map point, or by bearing and distance from the threshold; each with an altitude and a speed
  (empty: kept). The first point needs both: the aircraft starts there.
- **Duplicate** copies the selected plan with the next number (AZA101 → AZA102); **Remove FPL**; **Remove all FPLs**
  (after a question; also those waiting in the TRAFFIC LIST; aircraft already flying go on).
- **TFC LIST** (beside each plan) puts it in the TRAFFIC LIST, waiting; **ALL TO TFC LIST** all of them.
- **Save scenario… / Load scenario…**: all the plans in a file (folder `Scenarios` beside the settings). A scenario
  keeps the name of the class of each plan: a class that is missing on another computer becomes the Jet.

## 8. TRAFFIC LIST

- **Flying**: callsign, altitude (↑↓), speed, distance from touchdown, **P** (● seen by the student's PAR, ○ not
  seen), navigation. A click selects the aircraft for AIRCRAFT CONTROL. On each row: **II / ▶** (pause / unpause only
  this aircraft), **↺** (an aircraft of a flight plan starts again from its first point), **✕** (remove).
- **WAITING**: the aircraft sent with TFC LIST or Move to TFC list. **▶** starts one, **✕** takes it out (a flight
  plan waits again in TRAFFIC GENERATION). A click on a flight plan shows its route in the editor.
- **Start all**: all the waiting aircraft start.
- **PAUSE ALL / RESUME ALL**: the whole simulation stops and goes on.
- **Remove all aircraft**: flying and waiting, after a question (the flight plans wait again); a PAUSE ALL is ended,
  so the next aircraft started move at once.

## 9. AIRCRAFT CONTROL

The selected aircraft's callsign is on the bar. The information (class, distance, ground speed, altitude, what is
waiting for the pilot) and the readouts **BEST VS** (the vertical speed of the glide path at its ground speed — what
Normal gives), **VS**, **TURN**, **HDG**, **DRIFT**. Then a row for each command:

| Row | Commands |
| --- | --- |
| **HDG** | ◀ / ▶ (1°, held: repeats), the heading box, **Final CRS**; turn rate **3° / FREE / 1.5°**, **STOP TURN** |
| **ALT** | altitude **at** a rate (ft/min), **Go** |
| **RATE** | ▲ / ▼, **Normal** (the glide path), **STOP** (levels off where it is) |
| **SPD** | speed, **Go**, **Slow dwn to final** (the final speed of the class) |
| **NAV** | **MANUAL / ROUTE**, **Next WPT**; → a point of the route or of the maps, **Go**, **Route…** (Edit route) |
| **STICK** | **Show / Hide** the stick, **Joystick…** (a real joystick or gamepad) |
| **NAME** | **Rename** |

- **MANUAL**: the aircraft flies the headings you give (and the stick); a route, if any, is kept.
- **ROUTE**: it flies the points of its route; the route's altitudes and speeds are flown until you give one.
  A heading (or the stick sideways) puts it back in MANUAL at once.
- **Keys…** (on the bar): see [12](#12-keys-and-macro-keyboard).

## 10. TRAINEE PAR

The student's PAR in small: the same runway, glide path and range, linear scales, the tracks with a few dots, the
selected aircraft with a yellow square; a click on a track selects it. Aircraft that the student's PAR **does not see**
(outside its beams) are **grey** with "not seen"; when they come into the beam they become green, at the same moment
they appear to the student.

## 11. Aircraft performance

**Aircraft performance…** (SIMULATION SETTINGS) or **…** beside a Class box: a table with a row for each class.

| Column | Meaning |
| --- | --- |
| Min / Max speed | lowest and highest speed an instruction can give (kt) |
| **Initial speed** | put in the Speed box when the class is chosen (and in the first point of a new flight plan) |
| **Final speed** | the speed of **Slow dwn to final** |
| Max climb / descent | ft/min |
| Accel. / Decel. | how fast the speed changes (kt per second) |
| Vert. accel. | how fast the vertical speed changes (ft/min per second) |
| Max bank | bank limit of the turns (degrees) |

Type in a cell, or turn the **wheel** over it (Shift: bigger steps). **Add class**, **Duplicate** (e.g. a slower Jet),
**Remove**, **Reset to defaults**. **Save** checks the values (min ≤ final ≤ initial ≤ max, names not repeated): a
wrong value is said and nothing is saved. The values are saved in `performance.json` beside the program and are used
for the aircraft created from then on; those flying keep theirs.

Default classes (the Instructor+ proposes the **Jet**):

| Class | Min | Final | Initial | Max |
| --- | --- | --- | --- | --- |
| Light | 60 | 70 | 90 | 160 |
| Turboprop | 90 | 110 | 140 | 280 |
| Jet | 120 | 140 | 180 | 350 |

## 12. Keys and macro keyboard

**Keys…** gives a key to each command of the selected aircraft (heading left / right by a step, Final CRS, rate up /
down by a step, Normal rate, Stop turn, Stop climb / descent, MANUAL / ROUTE, Next WPT, Slow dwn to final, Next
aircraft, PAUSE ALL) and chooses the steps of the heading and rate keys. Click **Assign**, then press the key (or turn
the knob of a macro keyboard). These keys work while the Instructor+ window is in front, also while typing in a field:
choose keys that are not used for typing. The defaults are **F13–F21**, made for a macro keyboard of 6 keys and a knob
(program the macro keyboard with its own software to send those keys).

Fixed keys: **N** next aircraft, **Shift+N** previous aircraft (not while typing in a field).

## 13. A lesson on one PC

With two monitors: the student's AuroraPAR on one (PAR or RDR, key **R**), the Instructor+ on the other. One mouse
and one keyboard for two people: the instructor works with the mouse on the Instructor+, the student gives the
instructions by voice. A lesson between two computers (instructor and student in different places) will come with the
network link.
