# AuroraPAR v2 — User Manual

AuroraPAR is a **Precision Approach Radar (PAR)** display for the IVAO **Aurora** ATC client. It reads the traffic from Aurora and shows, for the selected runway, the two classic PAR views: **elevation** (glide path) above and **azimuth** (centreline) below. It is meant for controllers giving PAR / talk-down approaches on IVAO.

*The pictures are illustrations of the program, not screenshots.*

This manual describes AuroraPAR v2, a fork of [AuroraPAR](https://github.com/bornac1/AuroraPAR) by bornac1, a third-party add-on for IVAO's Aurora ATC software. All credit for the original program goes to bornac1.

> **Third-party add-on.** AuroraPAR is a free program made by IVAO members for their own use. It is **not made, endorsed or supported by IVAO**. For simulation only: never use it for real-world navigation or air traffic control.

---

## Contents

1. [Installation and first start](#1-installation-and-first-start)
2. [Connecting to Aurora](#2-connecting-to-aurora)
3. [The main window](#3-the-main-window)
4. [Reading the display](#4-reading-the-display)
5. [Tracks, history and labels](#5-tracks-history-and-labels)
6. [Controls and keyboard](#6-controls-and-keyboard)
7. [Antenna tilt](#7-antenna-tilt)
8. [Decision height](#8-decision-height)
   - [8b. Glide path: several approaches and unpublished angle](#8b-glide-path-several-approaches-and-unpublished-angle)
9. [Analog mode](#9-analog-mode)
   - [9b. Coordination panel](#9b-coordination-panel)
10. [Settings and profiles](#10-settings-and-profiles)
11. [Range marks, colours, reminders](#11-range-marks-colours-reminders)
12. [Runways and the runway editor](#12-runways-and-the-runway-editor)
    - [12.4 CRSCalculator: runway heading and magnetic variation](#124-crscalculator-runway-heading-and-magnetic-variation)
13. [Where the settings are stored](#13-where-the-settings-are-stored)
14. [Troubleshooting](#14-troubleshooting)
15. [Quick reference](#15-quick-reference)

---

## 1. Installation and first start

1. Open the **[Test build](https://github.com/altanico/AuroraPAR-v2/releases/tag/test-build)** page on GitHub (Releases; no GitHub account needed) and download **AuroraPAR-win-x64.zip**. With a GitHub account the same files are also in the *Artifacts* of the latest successful **Build** run (*Actions* tab).
2. Unzip it into a folder of your choice. It contains:
   - `AuroraPAR.exe` — the program (Windows 64-bit, nothing else to install);
   - `runways.par` — the runway database (keep it **next to** `AuroraPAR.exe`);
   - this manual.
3. Run `AuroraPAR.exe`. The program is not signed with a paid certificate, so the first time Windows may show **"Windows protected your PC"** (SmartScreen): click **More info → Run anyway**. The SHA-256 of each zip is on the download page (`SHA256SUMS.txt`), to check that the file is the original one.

To update, download the new build and replace the files. Your settings are stored elsewhere (see [section 13](#13-where-the-settings-are-stored)) and are kept. If you edited `runways.par`, keep your copy.

---

## 2. Connecting to Aurora

AuroraPAR connects automatically to Aurora on the same PC (local port 1130). Start Aurora and AuroraPAR in any order: the connection is retried every few seconds and restored automatically if it drops.

**Important — allow third-party programs in Aurora.** Aurora accepts programs like AuroraPAR only when this option is on: **Aurora → Settings → Other → Software → 3rd Party software access**. Without it AuroraPAR stays on **STS FAIL** (bottom of the information area) and shows no traffic. If there is still no connection one minute after the start, AuroraPAR reminds you of this option with a message (once per session).

**Important — Aurora traffic refresh rate.** By default Aurora updates the traffic every 3 seconds, which is too slow for a PAR (tracks move in jumps). In Aurora set the **traffic refresh rate to 0.5 s**. AuroraPAR checks it for you (see *DATA* in [section 4.4](#44-information-area)).

The QNH is taken from the METAR of the airport in Aurora and refreshed every minute.

---

## 3. The main window

![Main window](images/main-window.svg)

| | |
|---|---|
| **1** | Information area ([4.4](#44-information-area)) |
| **2** | Elevation view ([4.2](#42-elevation-view-top)) |
| **3** | Azimuth view ([4.3](#43-azimuth-view-bottom)) |

**Right column, from the top (4–11).** The selectors "on the fly" are at the top; **Hide labels** (a quick switch of the display) comes after them, a little apart from the antenna tilt; the buttons Analog, Coordination, Test traffic (T) and Settings... are fixed at the bottom edge of the window, apart from them. In the analog mode the buttons are square keys like those of a real console (big capital letters; the whole key lights up in warm white when active: the runway and approach in use, COORD and TEST while their windows are open) and the text fields are readout windows. The bottom keys are **MODERN** (back to the modern display), **COORD**, **TEST** (test traffic, as the key T) and **SETUP** (Settings):

| | Control | Use |
|---|---|---|
| **4** | **ICAO** and **Runway** keys | The ICAO box shows the airport in use. Type another ICAO: with the fourth letter (or **Enter**, when the letters typed fit a single airport) it is set and its first runway is selected. Red: no airport in `runways.par` starts with these letters. **Esc** or leaving the box shows the airport in use again. Below, one key per runway of the airport (two columns, the one in use in blue; none when the airport has a single runway); the approaches of the same runway are one key. When you connect with a callsign of an airport in the file (LIPC_APP → LIPC) that airport is set at the connection; without such a callsign the last runway stays. There is no drop-down list of the runways. |
| **5** | Range list | Display range: the ranges ticked in *Settings → Range and scale → Ranges offered* (1, 2.5, 5, 10, 15, 20, 30, 40 NM; up to 20 NM by default). The mouse wheel over the display does the same. |
| **6** | **Settings...** | Profiles and options ([section 10](#10-settings-and-profiles)). |
| | **GP (°)** | Glide path of the approach: one key per published angle of the runway (when it has more than one), and below a box with the angle in use, where a free angle can be typed (Enter; unpublished approach, orange) ([section 8b](#8b-glide-path-several-approaches-and-unpublished-angle)). |
| **8** | **DH** box with **−** / **+** (hidden by default: *Settings → Approach and DH → DH selector*) | Decision height for this session ([section 8](#8-decision-height)). |
| | **BRT** with **−** / **+** | Brightness of the radar picture, 10–150% (also with the mouse wheel over it); saved in the profile. Up to 100% the picture is dimmed; above 100% the colours are made brighter and lighter, for dim monitors. |
| **9** | **Antenna tilt** | EL ▲ / EL ▼, AZ L / AZ R, Neutral ([section 7](#7-antenna-tilt)). |
| **10** | **Hide labels (L)** | Hides / shows all labels. |
| **11** | **Analog (A)** | Switches to the analog scope ([section 9](#9-analog-mode)). |

The window remembers its size and position, the runway and the range for the next start. If the column is too short for all the controls (small window), it shrinks to fit.

---

## 4. Reading the display

### 4.1 Common elements

Both views are drawn as seen from the side of the runway, with the **antenna** (small square) near the edge of the view and the approach extending away from it. The runway can be on the left or on the right of the screen ([section 10](#10-settings-and-profiles)).

- **Touchdown point** (small yellow mark on the runway): all distances are measured **from the touchdown point**, as controllers give them on final. It is where the glide path meets the runway (about 290 m from the threshold for 50 ft TCH and 3°), or a value set for the runway.
- **Range marks**: vertical lines at fixed distances from touchdown, with the distance written below them in the elevation view. Which marks are drawn at each range is configurable ([section 11.1](#111-range-marks)).
- **Scan limits** (blue lines from the antenna): the physical limits of the antenna, fixed.
- **Antenna beam**: the part the antenna is looking at now, moved by the antenna tilt inside the scan limits. **A track is seen only inside the beam**, and each view has its own antenna, as on a real PAR: the elevation view shows it inside the elevation beam, the azimuth view inside the azimuth beam — leaving one beam it disappears only from that view. It is shown by the range marks, **thicker inside the beam**; its edges can also be drawn as lines (*Settings → Radar → Draw the edges of the beam*, off by default) ([section 7](#7-antenna-tilt)).
- **Approach limits** (red lines from the touchdown point): the tolerance around the glide path / centreline. Inside them a track is **green**, outside **red**.
- The glide path, centreline and approach limits are **solid** down to the touchdown point; they can be dashed or dotted from the threshold or from the DH (*Settings → Approach and DH → Glide path near the runway*, advanced).
- Distances and offsets are computed on the Earth's shape at the runway (WGS84 radius of curvature in the direction of the runway): along the final they are right within a few metres up to 40 NM.

The antenna always stays at the same place: zooming in enlarges the approach, it does not move the picture. The part of the runway behind the antenna is outside the scan and is not drawn.

### 4.2 Elevation view (top)

![Elevation view](images/elevation-view.svg)

| | | | |
|---|---|---|---|
| **1** Antenna | **4** Approach limits | **7** Label | **10** Range marks |
| **2** Upper scan limit | **5** Track (red: outside the limits) | **8** Decision height | **11** Touchdown point |
| **3** Glide path | **6** Plots (history) | **9** DH meets the glide path | **12** Threshold / runway |


- **Horizon line** (ground at the threshold elevation) and the runway.
- **Glide path** (yellow) from the touchdown point at the runway's glide slope angle, with its **approach limits** above and below (default ±0.5°).
- **Decision height**: a red horizontal line at the DH and a dashed vertical line, both drawn **from the point where the glide path reaches the DH**. Both can be customised: colour, line style and width of each (*Settings → Colours and lines*: *Decision height line* and *Decision height drop line*, which can also be hidden); the horizontal line goes to the **left, right or both sides** of the point on the screen (or *none*: no line), with a length of 0.25–5 NM on each side or **to the edge of the view** (with *both*: a line across the whole screen, as on some scopes) (*Settings → Approach and DH → Horizontal line*); the vertical line goes **down, up or both** (or *none*), as long as the DH (down to the ground, the default) or 50–1000 ft (*Settings → Approach and DH → Vertical line*); and an optional **symbol** on the DH point, with the colour of the *Decision height mark* (*Settings → Approach and DH → DH point symbol*, none by default). In the azimuth view (from above) the DH line crosses the centreline at the same distance, always on both sides: it has its own style (*Decision height line (azimuth view)*), a length on each side of the centreline (up to the scan limits, the default, or 50–1000 m: *Settings → Approach and DH → Line in the azimuth view*) and an optional symbol on the centreline (*Settings → Approach and DH → DH point symbol (azimuth view)*, with the colour of the *Decision height mark*).
- **Glide path near the runway** (*Settings → Approach and DH*, advanced, only when the glide path is drawn solid): the last part of the glide path, approach limits and centreline, down to the touchdown point, is **solid** (the default), **dashed from the DH** (from the point where the glide path reaches the DH) or **dashed from the threshold** (as in the versions before 11 Oct), with a *dashed* or *dotted* line. A line drawn dashed in *Colours and lines* stays dashed down to the runway.
- **Altitude scale** (optional) on the runway side: altitudes with QNH, heights with QFE, in feet or metres.

### 4.3 Azimuth view (bottom)

![Azimuth view](images/azimuth-view.svg)

| | | |
|---|---|---|
| **1** Antenna | **4** Approach limits | **7** Touchdown point |
| **2** Scan limits | **5** Track (green: inside the limits) | |
| **3** Extended centreline | **6** Distance of the decision height | |


- **Extended centreline** (yellow) with its **approach limits** left and right (default ±1.5°).
- A red vertical line at the distance where the glide path reaches the DH.
- Left/right are always **as seen by the pilot on the approach**. With the runway on the right of the screen the azimuth view is rotated by 180°, so the pilot's right stays on the same side as the direction of flight.

### 4.4 Information area

In the top corner of the elevation view, on the runway side:

| Line | Meaning |
|---|---|
| `RWY 16L` | Selected runway. |
| `CRS 163` | **Final course, magnetic.** Calculated, *not* the published value: runway true heading corrected with the magnetic variation, rounded to the degree. Check it against the approach chart (tooltip). |
| `GP 3.0°` | Glide path angle. With a typed angle that is not published: `GP 3.4° UNPUBLISHED APPROACH` in orange. |
| `MAPt DIST 0.98 NM` | **Missed approach point**: distance from the touchdown point where the glide path reaches the DH/OCH in use. Compare it with the chart (MAPt / RPI DIST) to check `runways.par`; it is the distance for "approach terminating at …". Orange together with an unpublished angle (it is then the new MAPt). |
| `QNH 1013` / `QFE 1001` | Pressure setting (QFE computed for the threshold elevation), in hPa or inHg. |
| `DA 392 ft` | Minimum: DA/DH, OCA/OCH or MDA/MDH; altitude with QNH, height with QFE. |
| `EL TILT 2.0 UP` (orange) | Shown only while the antenna is tilted. |
| `STS OK` / `STS FAIL` | Connection to Aurora. |
| `DATA 0.5s` | How often the traffic really changes. Red with `SET AURORA TRAFFIC REFRESH TO 0.5s` if it is too slow. Shown when there is moving traffic (10–20 s needed). |

---

## 5. Tracks, history and labels

### 5.1 Tracks and plots

Each aircraft inside the antenna beam is shown with a **track symbol** (default: circle with cross), green inside the approach limits and red outside. Behind it the **history tail** (the "plots") shows its previous positions:

- one dot every **2 s** by default (0.5–10 s), up to **50 dots** (3–100);
- dots only where the aircraft was inside the beam;
- colours of tracks and plots, inside and outside the limits, are configurable.

**Coasting track** (modern display). When an aircraft leaves the beam, the radar computer keeps it for a few seconds at an **estimated position**: straight on from the last position seen in the beam, at the same ground speed and vertical speed (the real position is not used). The symbol changes to the **coasting symbol** (default: diamond) so you know it is no longer seen; the label stays, with the estimated values; no new history dots. After **8 s** by default (*Settings → Tracks and labels → Coasting tracks*, advanced, 0–30 s, 0 = hidden at once) track, label and tail disappear. If the aircraft comes back into the beam it is shown again at its real position (it may jump a little). Symbol and colour of the coasting track: *Settings → Tracks and labels → Symbols* and *Settings → Colours and lines → Coasting tracks*. On the analog scope there is no estimate: out of the beam the echo goes dark and its afterglow fades (about 6 s).

**Test traffic** (key **T**, or *Settings → Test traffic...*). Virtual aircraft to test the radar without waiting for real traffic, also without Aurora. While test aircraft are flying, **TEST TRAFFIC** is shown at the top of the views; closing the window removes them, nothing is saved. The window has:

- **New aircraft**: distance from touchdown, speed, SSR code and the initial offsets (m right + / left −, ft above + / below − the glide path), then **Add**. It appears as TEST1, TEST2... on the final course, descending at the glide path rate, and flies towards touchdown (it disappears when it lands, or when it goes far away).
  **Start**: *On final* (as above) or an **intercept start** (30°, 45°, 90°, from the left or from the right): the aircraft is aimed at the point of the centreline at the **Distance**, with that intercept angle, starting about 3 NM from the centreline and **level, below the glide path** (the height offset, default −300 ft, is measured at that point; the lateral offset is not used). It is for practising the turn onto the final and the moment of the descent: when it reaches the glide path (the aircraft information shows *GP in … NM (about … s)*) give **Normal** and it starts to descend after the pilot reaction.
- **Wind** (rough model: the same at all heights): direction it blows from in degrees true (as in the METAR; the magnetic value is shown beside it), speed, and **gusts** (the wind grows at random up to this much more; 0 = none). **From METAR** copies the wind of the METAR of the airport in use received from Aurora. The aircraft fly their **heading** through the air: with a **crosswind** they drift off the centreline unless the heading is corrected into the wind; with a **headwind** the ground speed is lower and so is the rate of descent needed on the glide path (BEST VS follows the ground speed).
- **Aircraft**: the list (the selected one is the one you command) with distance, speed and code.
- **Control**, everything needed while flying: the readouts **BEST VS** (vertical speed of the glide path for the ground speed of the aircraft and the GP angle in use), **ACTUAL VS**, **TURN** (actual turn rate, useful in Free), **HDG** (magnetic heading) and **DRIFT** (degrees the wind pushes the track off the heading, L / R); the **turn rate** 1.5°/s, 3°/s or **Free** (the longer the stick is held at its edge, the faster the turn, up to 10°/s); two blocks:
  - **Instructions (controller)**, carried out after the **pilot reaction** (default 0.5 s, 0–5 s; with the wheel or held keys, after the last step), as real pilots are not instantaneous. **HEADING °M**: ◀ L / R ▶ change it by 1° (held: repeat), the **mouse wheel** over the box by 1° (+Shift 10°), Enter carries out what is typed; the aircraft turns the shortest way at the chosen turn rate (3°/s with Free) and keeps the heading (whole circle). **Final CRS (xxx)** puts the final course in the box (with a crosswind the aircraft then drifts: correct the heading). **RATE ft/min**: ▲ UP / ▼ DN change it by 50 ft/min (held: repeat), the **wheel** by 100 (+Shift 500); a step that would cross the best rate **stops once on it**. **Normal (−xxx)**: the best rate, kept also if the speed, heading or wind change. The fields start from the heading and rate of the selected aircraft and follow it after the stick is released.
  - **Manual (pilot flying)**: the turn rate 1.5°/s, 3°/s or **Free** (the longer the stick is held at its edge, the faster the turn, up to 10°/s) and the **stick** (left/right turns, up/down changes the vertical speed, at once; released, the heading and the vertical speed reached stay, as with a real aircraft; the knob also follows a real joystick).
- **On GP / CL** (exactly on the centreline and the glide path at once, with the heading that holds the final course in this wind), **Auto** (intercepts the centreline and the glide path by itself, correcting the drift), **Pause**, **Remove**, **Remove all**, **Joystick...**, and **Aurora-like data** (positions every about 0.5 s and the altitude in steps, as the real data, to test the track smoothing).

**Joystick or gamepad** (*Settings → Joystick...* or *Test traffic → Joystick...*). Any USB joystick or gamepad seen by Windows works, without a driver, also while AuroraPAR is not the window in front. The window shows the device in use (or *First found*), a **live test** of the X/Y axes and of the buttons pressed, and the settings, saved at once:

- **The stick moves**: **Automatic** (default: the selected test aircraft while the test traffic window is open, otherwise the antenna), **Always the antenna tilt** or **Always the test aircraft**. Test aircraft: as the stick of the window (left/right turns, back/forward changes the vertical speed; released, course and vertical speed stay); the stick of the window follows the real one. With *pull back to climb* (default) the stick works as in an aircraft. Antenna: forward/back tilts up/down, left/right tilts left/right, one step and then repeated while held. **Dead zone** around the centre: default 10 %.
- **Buttons and hat**: each command can have a button or a hat direction, chosen in the list or with **Press...** and then the button (within 5 s); a button belongs to one command only. Defaults: button 1 Final CRS, 2 normal rate of descent (=), 3 next test aircraft, 4 switch the stick between aircraft and antenna (shown for 2 s), hat = antenna tilt. Other commands: − / + rate of descent, On GP / CL, pause, next turn rate, antenna neutral.

**Track smoothing** (modern display). Aurora sends the positions a little irregularly and the altitude in steps, so a raw track moves in small jumps. As the computer of a modern radar, AuroraPAR moves the track smoothly with the speed, direction and vertical speed of the aircraft (as the coasting track) and at every new position from Aurora pulls it part of the way towards the real one: the track stays on the aircraft and moves smoothly, and the label values are steadier. The history dots stay at the real positions (the "plots"). The estimated position is moved on at every frame of the screen, so the track glides instead of stepping. *Settings → Tracks and labels → Track smoothing*: **Off** (raw positions), **Light** (default), **Strong** (smoother, a little late in turns and rate changes). The analog scope always shows the raw echo.

> Aurora interpolates the horizontal position between real network updates but the altitude changes only when a real update arrives (every few seconds). In the elevation view this can make the tail look like steps. A dot interval of about 3 s makes it less visible.

### 5.2 Labels

Each track has a **label** in each view. Its content is configurable (*Settings → Edit labels...*): rows and columns, and in each cell one of:

| Field | Example |
|---|---|
| Callsign | `AZA123` |
| Distance from touchdown | `6.2 NM` |
| Altitude (QNH) / height (QFE) | `A 2060 ft` / `H 1900 ft` |
| Ground speed | `140 Kts` |
| Vertical speed | `-750 ft/min` |
| Deviation from glide path | `U 85 ft` (up) / `D 40 ft` (down) |
| Deviation from centreline | `L 35 ft` / `R 35 ft` (pilot's left/right) |
| SSR code | `A2201` — the transponder code given by Aurora (PAR+SSR radars); nothing when there is no valid code (or 0000) |
| Track ID (fictitious) | `42` — for radars that receive neither the callsign nor the code (see below) |

**Frame and field outside the frame** (in the same window, for each label): choose which **sides of a frame** to draw around the label — top, right, bottom, left, any combination (none by default; only the left side gives the vertical bar of some scopes). Colour, width and line style: *Settings → Colours and lines → Label frame*. **Outside the frame:** the first row (above the frame, like a title) or the last row (below it) can be kept outside, with its own text colour (*Label text (field outside the frame)*, default yellow). The preview in the window shows the result.

A label with no fields shows only the track symbol.

**Track ID.** With the option *give each new track a random two-digit ID* (in *Edit labels...*, on by default), a track gets a random number from 01 to 99 when it enters the scan. It keeps it while it is seen; a number is never given twice in the same session, and a track out of the beam for more than 10 seconds (or the coasting time + 2 s, if longer) loses its ID (it gets a new one when it comes back). To give a track an ID of your own (up to 7 characters, e.g. `X1`): **right click on its label → Set ID...**, type it and press **Enter** (Esc cancels; an empty ID goes back to the random one; *Clear ID* too). Your IDs are kept for the session, not saved. When the label has a Track ID field, the right click on the label opens this small menu (with *Hide label*) instead of hiding it at once.

**Moving and hiding labels:**

- **Drag** a label with the mouse: a leader line joins it to its track: it has its own style (*Settings → Colours and lines → Label leader line*: colour, line, width, show / hide; default a pale yellow, not the colour of the track). **Double click** puts it back.
- **Right click on a label** hides it (with a Track ID field: a menu with *Hide label* and *Set ID...*).
- **Right click near a track** hides/shows its label. Where several tracks are close (formation), or labels are hidden elsewhere, a menu lists them by callsign, with *Show all hidden labels*.
- **L** or **Hide labels** hides/shows all labels; showing them again also brings back the ones hidden one by one.

---

## 6. Controls and keyboard

| Key / mouse | Action |
|---|---|
| Mouse wheel over the display | Range up / down |
| ↑ / ↓ | Antenna elevation tilt up / down |
| ← / → | Antenna azimuth tilt left / right |
| Home | Antenna back to neutral |
| Page up / Page down | Range larger / smaller (also the mouse wheel); End = preferred range |
| Shift+↑ / Shift+↓ | Decision height higher / lower; Shift+Home = runway value |
| Ctrl+↑ / Ctrl+↓ | Brightness up / down; Ctrl+Home = 100% |
| L | Hide / show all labels |
| A | Modern / Analog display |
| P / Shift+P | Next / previous profile (its name is shown for 2 s; runway and traffic stay, the antenna goes back to neutral) |
| T | Test traffic window (virtual aircraft, see below) |
| C | Linear / log distance scale (LIN / LOG keys; saved in the profile, see 10.5) |
| Joystick / gamepad | Test aircraft and antenna tilt (see *Joystick* in 5.1) |

Keys are ignored while you are typing in a text box (e.g. the DH box).

---

## 7. Antenna tilt

On the old PAR the antenna beam was narrow, and the antenna was tilted to point it where the aircraft was. This is an **advanced function**, off in a new profile: the radar then sees everything inside the scan limits (default −1° to +10° in elevation, ±15° in azimuth) and the tilt controls only show a hint on how to turn it on. Turn it on with *Settings → Radar → Narrow antenna beam moved by the tilt*: the beam is then set a few degrees narrower than the scan limits (4° in elevation, 6° in azimuth), so the tilt works at once; then set it as you like. With the beam on, AuroraPAR has two sets of lines:

- the **scan limits**: the physical limits, as far as the antenna can look; they never move;
- the **antenna beam**: what the antenna looks at now, with its own width in elevation and azimuth (*Settings → Radar → Antenna beam*). Only the traffic inside the beam is seen (each view with its own beam: leaving the elevation beam a track disappears from the elevation view only, and the other way round), and the range marks are thicker inside it (as on the real scopes).

The tilt moves the **beam** inside the scan limits; the glide path, centreline and approach limits do not move.

- Buttons **EL ▲ / EL ▼ / AZ L / AZ R / Neutral**, or the arrow keys and Home.
- Step **2°** by default (*Settings → Radar*). The beam stops where it reaches a scan limit: the narrower the beam, the more it can be tilted; a beam as wide as the scan limits cannot be tilted at all. The range can differ up and down (and left and right): the EL/AZ TILT knobs have their 0 where the neutral position is.
- The neutral position is the beam centre set in the profile (*Elevation / Azimuth centre in neutral*). The elevation centre is **automatic** by default: the glide path angle of the approach in use (published or free), so the beam is centred on the glide path by itself, also when the approach changes. (The glide path starts at the touchdown point and the beam at the antenna, so seen from the antenna the glide path is a little steeper near the runway; the GP angle is a good centre for the whole approach.)
- Profiles from older versions keep their scan limits (the old ones; the old tilt is gone) with the beam off; turn the beam on to use the tilt.
- While tilted, an orange reminder is shown (`EL TILT 2.0 UP`, `AZ TILT 2.0 L`).
- Left/right of the azimuth tilt are **as seen by the pilot** flying the approach. *Settings → Radar → Azimuth: swap left and right* makes them **as seen from the runway** looking at the approach (R = the pilot's left): AZ buttons, ← / → keys, AZ TILT knob and the L/R readouts all follow it.
- The tilt goes back to neutral when the runway changes, and is never saved.

---

## 8. Decision height

The DH of each runway comes from `runways.par`. During the session it can be changed **on the fly** with **−** / **+** (10 ft steps) or by typing a value in the box. It is not saved: it goes back to the file value when the runway is selected again.

The DH selector (box with **−** / **+** in the modern display, knob or keys in the analog one) is an advanced function and is **hidden by default**: tick *Settings → Approach and DH → DH selector* to show it (per profile; existing profiles start with it hidden). Hidden, the runway value is used, and **Shift+↑ / ↓ / Home** still change it.

The name shown (DA/DH, OCA/OCH, MDA/MDH) is chosen in *Settings → Units and references*.

---

## 8b. Glide path: several approaches and unpublished angle

Some runways have approaches with different glide path angles. In `runways.par` simply write **one line per approach**, with the same airport and designator and its own angle (and its own DH, touchdown…). The designator may also end with the angle, e.g. `LIPC;11 2.8;…` and `LIPC;11 2.5;…`: both are runway `LIPC 11`. AuroraPAR groups them: the runway has **one** key, and **GP (°)** has a key for each published angle (the box below shows the angle in use and takes a free angle). Choosing one changes only the approach: range, antenna tilt and traffic history stay.

- **Modern display:** besides the published angles, any angle from 1.0° to 7.0° can be **typed** in the GP box and confirmed with **Enter** (Esc: back). It is an **unpublished approach**: the angle and `UNPUBLISHED APPROACH` are shown in orange. The **DH does not change** (it comes from the obstacles); the **missed approach point** moves with the angle, and the new **MAPt DIST** is shown in orange next to the warning. Not saved: selecting the runway again goes back to the file.
- **Analog mode:** only the published approaches, as keys with a lamp under **GP DEG** (shown when the runway has more than one) — see [9.4](#94-airport-runway-and-approach-keys).
- **MAPt DIST** (information area, and the console in the analog mode) is shown for every approach: `DH / (tan(GP) × 6076 ft)`. Compare it with the chart to check the file.

---

## 9. Analog mode

![Analog console](images/analog-console.svg)


**Analog (A)** turns the window into an old PAR console. Press it again (**Modern (A)**) to go back. The choice is saved in the profile. *Settings → Display → Lock the display mode in this profile* removes the button and the key A for that profile (useful with one Modern and one Analog profile: change profile with P instead).

### 9.1 The scope

- One **round screen** in a metal ring with the elevation view above and the azimuth view below, in **phosphor** colour (yellow-green by default, other colours in *Settings → Colours and lines*).
- The **antenna beam** sweeps the views in turn. Each aircraft is an **echo** that brightens as the beam arrives, is brightest at its centre and then fades until the next pass. Its position is always the latest one from Aurora: the beam changes only the brightness.
- No labels and no altitude scale, as on the real scopes. The history tail fades with age.
- The tooltips (help bubbles) are dark with amber text, like the console.
- **Afterglow**: like the phosphor of the old screens, a faint trail of the echo stays where the aircraft was in the last seconds, and the history dots glow slightly when the beam passes over them.
- **Rain clutter** (*Settings → Display → Rain clutter*, advanced, per profile, **off by default**): irregular green patches alternating with dots, only **inside the antenna beam** (it moves with the tilt; a short fade at its edges, no sharp cut), stronger towards the far end of the range. The intensity follows the **precipitation of the METAR** of the airport in use (received from Aurora): drizzle or light rain = a few dots and small patches, rain = patches, heavy rain or thunderstorm = large bright patches. Like the echoes, the clutter **lights up when the scan beam passes over it** and fades; the dots stay in place (their light changes with the beam) and the **patches drift with the wind** (METAR wind, or the wind set in Test traffic): real speed, in scale with the range, so slow with a light wind (about half a pixel per second at 15 kt and 10 NM) and faster at short ranges. When the **tilt** moves, the picture stays a moment where the beam was and fades out (about 2 s). The tracks are not hidden: they are drawn in the same green and **get lost in the clutter**. The **CLUTTER knob** (appears with the option) reduces the clutter, but dims the tracks too (OFF = no filter). To try it without waiting for a METAR with rain: *Test traffic → Rain* (From METAR / None / Light / Moderate / Heavy / Thunderstorm, works also with the option off, and the filter slider). Modern display: no clutter.
- **Readouts**: the windows of the left column (airport, runway, course, glide path, MAPt, DH, QNH, range, tilt) are 14-segment displays (default) or **mechanical drum counters** (*Settings → Analog controls → Readouts*): off-white digits and letters on black wheels behind a window; when a value changes the wheels roll to the new character, and each wheel is a little off, as on worn counters. Same data, only the look changes. The digits are in the Carlito font (SIL Open Font License, included).
- **Edge of the antenna beam**: near the edge of the beam (the last half degree) the echo gets weaker; out of the beam it goes dark, and its afterglow and dots fade in about 6 s.
- In a low window the screen is cut at the top and bottom (only frame and glass), so the views stay large.

### 9.2 Console panel (left)

14-segment amber readouts (or drum counters, see *Readouts* in 9.1):

| Readout | Meaning |
|---|---|
| APT | Airport ICAO |
| RWY | Runway |
| CRS | Final course, magnetic (calculated — see tooltip) |
| GP DEG | Glide path angle |
| MAPt DIST | Missed approach point distance from touchdown, NM (see [8b](#8b-glide-path-several-approaches-and-unpublished-angle)) |
| DA FT (or OCA, DH…) | Minimum |
| QNH / QFE | Pressure setting |
| RANGE NM | Displayed range |
| EL TILT / AZ TILT | Antenna tilt (`U`/`D`, `L`/`R`) |

Status lamps:

| Lamp | Meaning |
|---|---|
| **STS** | Green: connected to Aurora. Red: not connected. |
| **ANT. R/R** | Antenna refresh rate (Aurora's traffic refresh). Green: good. **Flashing red: too slow — in Aurora set the traffic refresh rate to 0.5 s.** Off: not measured yet. Tooltip shows the measured value. |
| **TILT** | Amber while the antenna is tilted. |

### 9.3 Knobs (right)

**RANGE NM**, **EL TILT**, **AZ TILT**, **DH** and **BRT** (brightness of the scope only — frame, glass and console panel are not dimmed; 10–150%, saved in the profile, separately from the modern display; above 100% the faint elements — range marks, limits, DH — get closer to full intensity and the phosphor gets lighter, for dim monitors):

- **click on the right half** of the knob: one step clockwise; **left half**: one step counter-clockwise. The mouse pointer becomes a curved arrow showing the direction. Every click counts (two quick clicks = two steps);
- **mouse wheel** over the knob;
- **centre** of the knob (up/down arrow pointer): **drag** up/down to turn; **double click**: tilt back to neutral, DH back to the runway value, BRT back to 100%.

The knobs always follow the real state, also when it is changed with the keyboard.

**Knobs or keys** (*Settings → Analog console controls*, per profile). Each group can be a knob or a row of console keys (lit key = the state in use):

- **Range:** knob, **one key per range** (the ranges ticked in *Range and scale → Ranges offered*; the one in use is lit), **`<` `DEF` `>`**: smaller / back to the preferred range (*Range and scale*) / larger, or **Range panel (FIAR keys)**: large square backlit keys in two columns in a gold frame, as on the range panel of the FIAR PAR console (number over NM; all dimly lit, the range in use bright white; a small dot on the preferred range), with the key **PREF** back to the preferred range under them and, under it in the same frame, the double key **LIN | LOG** of the distance scale. With the console keys the text of the key back to the preferred range is free (at most 5 characters, default DEF); with the FIAR keys it is always PREF. Every range control has the **LIN / LOG** keys of the distance scale (console keys: in the RANGE group, or a SCALE group under the knob; FIAR keys: the double key in the same gold frame).
- **Antenna tilt:** two knobs (EL, AZ), keys **UP 0 DN** and **L 0 R** (0 = neutral of that axis, lit when neutral), or a **small 4-way joystick** as on some old American PAR consoles: push it (drag, or click on a direction) up/down for the elevation, left/right for the azimuth; one step, repeated while held; released it springs back; a click on the centre = neutral.
- **DH:** knob or keys **− RWY +** (10 ft steps; RWY = the runway value, lit when in use).
- Tilt, DH and BRT keys **repeat while held** (0, RWY, 100 do not); the range keys step only once per press.
- **BRT:** knob or keys **− 100 +** (100 lit at 100%).
- **Keys:** the look of all the keys of the console (groups, RWY, GP DEG, MODERN / COORD / SETUP): **Console** (grey keys, default) or **FIAR** (backlit keys as on the FIAR console: all dimly lit, the one in use bright white; the groups in a thin gold frame).

### 9.4 Airport, runway and approach keys

In the analog mode the column on the right has no drop-down lists and no DH field (the DH is the console readout and the knob):

- **APT** — a readout window like those of the console. It shows the airport in use. Click it and type the ICAO (the last character blinks); with four characters, or **Enter**, the airport is set and its first runway is selected. **Esc** cancels, **Ctrl+V** pastes. If the airport is not in `runways.par` the window flashes and shows the airport in use again. With a callsign such as `LIPC_APP` connected in Aurora its airport is set at the connection.
- **RWY** — one square key for each runway of the airport (two columns); the key of the runway in use is lit. An airport with a single runway has no keys (the runway is in the RWY readout of the console). The approaches of the same runway (several glide path angles) are one key.
- **GP DEG** — one key for each published approach (glide path angle) of the runway in use, with the one in use lit. A runway with a single approach has no keys. The free angle is only in the modern display.

The airport is the same as in the modern display (ICAO box and runway keys there): what you set here is already there when you switch back.

---

## 9b. Coordination panel

The **Coordination** button opens a small panel for **voiceless coordination** between the radar (PAR / approach) and the tower, as on the light panels of real PAR rooms.

**For the tower: AuroraCoord.** The tower controller does not need the PAR display: download **AuroraCoord-win-x64.zip** (same *Test build* page as AuroraPAR) and run `AuroraCoord.exe`, a small program with only this panel. It works with the panel inside AuroraPAR (and two AuroraCoord can also work together). It needs Aurora running on the same PC, like AuroraPAR. Its options are saved in `%AppData%\AuroraPAR\AuroraCoord.settings.json` (or in a file with that name next to `AuroraCoord.exe`, portable mode).

| | |
|---|---|
| Lights 1–5 | Square lit buttons on a black plate, as the light panel of the FIAR PAR console (default blue, white, yellow, red, green, as on the real panel). No text on the lights: each unit gives them its own meaning, e.g. *12 NM*, *8 NM*, *landing clearance requested / given*, *not authorised*. |
| Light 6 | **Reset** (default black, set apart from the others): switches all the lights off on both panels. |

**How it works — the same rule for every light:**

1. The first side that presses a light makes it **flash** on both panels, with a **sound**: the buzzer of a real PAR light panel (see *Sound* in Options).
2. When the other side presses the **same light**, it becomes **steady** on both panels: received.
3. Pressing again a light you called yourself, while it still flashes, cancels the call.
4. The lights stay on until one of the two presses **Reset** (usually at the end of the approach).

Example: at 12 NM the radar presses white (flashing, alert in the tower), the tower presses white (steady). At 3 NM the radar presses yellow to request the landing clearance; the tower presses yellow to give it, or red to refuse it.

**Linking the two panels — automatic:** AuroraPAR asks Aurora which callsign you are connected with. Panels of the same airport are linked: `XXXX_TWR` is the tower, any other callsign of the airport (`_APP`, `_F_APP`, `_DEP`…) the radar. The status line shows the airport, your side and whether the panel of the other side is linked (*linked with the tower* / *linked with the radar*, green dot; *waiting for the …* in orange). It is about the two panels, not about IVAO: with airport and role chosen in *Options* the panels link also offline (e.g. to test them on one PC).

**Connected as a controller**, the callsign always decides airport and side, in AuroraPAR as in AuroraCoord (an airport or role chosen earlier in *Options* is not used). **As observer** (`_OBS`) or not connected: open **Options**, type the **airport** (ICAO) and choose the **role**.

**Panel code (optional).** The channel of an airport can be found by anyone who knows the program, so a stranger could in theory press your lights. To keep the group private, type the same **panel code** in *Options* on every panel of the group (radar, tower, phones), or press **New code** on one panel and type that code on the others: only panels with the same code are linked, and **🔒** is shown in the status line. The QR code for phones already contains it. Empty code (default) = the open channel of the airport, as before. Letters and digits, up to 12; it stays saved until changed.

**Monitor (instructor):** an instructor connected as observer chooses the airport and the role **Monitor** in *Options*. The monitor panel shows the same lights in real time and whether the radar and the tower are online, but it is **read-only**: it cannot press the lights or reset them, and it does not sound.

**Sound** (Options): **at every press, on both panels** (default: every call, acknowledge, cancel and reset sounds on both sides, also your own press) or **only for the presses of the other side**. The monitor never sounds.

**Options:** airport, role (from callsign, Radar, Tower, Monitor), panel code, sound, **colour** and **engraved text** of each button (optional, up to 10 characters, shown under the button as on a radio panel; only on your panel; default: *RESET* under the reset button, nothing elsewhere), *Default colours and texts*, always on top.

**Connection:** the panels talk through a free public relay on the internet (MQTT, encrypted connection), so there is nothing to install and no port to open. Only the state of the lights is sent: no names, no IVAO data. Being a public service it is best-effort. If the network blocks the MQTT port (8883), the panel reaches the same relay through a secure WebSocket (ports 8084 / 8884), as the phone panel. If the status line keeps saying *connecting...*, check that a firewall or antivirus does not block AuroraPAR / AuroraCoord from the internet (try the phone panel link in a browser on the same PC: if that connects, the network is fine).

**On a phone or tablet:** the same panel also runs in the browser of a phone or tablet, with nothing to install: <https://altanico.github.io/AuroraPAR-v2/coord/>. Use it instead of the window on the PC, or as an **extra panel** next to it (same role as the PC: pressing on the phone or on the PC is the same).

- **Quickest:** in the panel on the PC, *Options* → **Open on phone / tablet...** shows a **QR code**. Scan it with the phone camera: the phone panel opens already set with the airport, the role, the colours and the texts of the PC panel. *Copy link* gives the same link, to send it by message.
- **By hand:** open the address above; the first time it asks for the airport and the role (Radar, Tower, Monitor), and the panel code if your group uses one. The **⚙** button in the top right corner changes them, with the colour and the engraved text of each button.
- The buttons **fill the screen**: two columns with reset below in portrait, one row in landscape; it follows the rotation.
- Tap **Tap to start** when it opens: it allows the **alert sound** and keeps the **screen on** while the panel is open. On Android the phone also **vibrates** at an alert (iPhones do not allow it from a web page). *Test sound* in ⚙ plays the alert.
- Add it to the **home screen** (browser menu → *Add to Home screen*) to open it full screen like an app. On iPhone the home screen copy keeps its own settings: open it once from the QR code link, or set it with ⚙.
- If the phone locks or you switch app, the panel reconnects when you come back and shows the current lights at once.
- The phone needs an internet connection (Wi-Fi or mobile data). It uses the same public relays as the PC, through their secure web port.

> A **shout line** (always-open intercom) may be added in a future version.

---

## 10. Settings and profiles

**Settings...** opens the options of the **active profile**. Every change is applied and saved immediately.

The window has the **profile** bar at the top (everything below belongs to that profile), the **sections** on the left — *Display*, *Range and scale*, *Approach and DH*, *Tracks and labels*, *Radar*, *Units*, *Analog console*, *Colours and lines*, *Reminders* — and the tools at the bottom (runway editor, test traffic, joystick). One section is shown at a time; it scrolls only on a small screen.

**Show advanced** (above the sections, off by default, remembered): shows the options that are seldom changed — lock of the display mode, rain clutter, marks per range, DH vertical and azimuth lines and DH symbols, history interval, track smoothing, coasting tracks and the symbols other than track and history, everything of the radar except the approach limits, magnetic variation; the glide path near the runway; in the range marks, the table of the marks per range, *Distance text on all ranges* and its *Default* button. Off, they are only hidden: their values stay as set.

### 10.1 Profiles

A profile holds all the display options, so that different controllers or different radar types can have their own set-up.

**Profiles that come with the program:** the `Profiles` folder next to AuroraPAR.exe holds the profiles **PAR2080** (analog, narrow beam, console keys) and **PAR2090** (modern). At the first start (no settings file yet) they become your profiles, PAR2080 active. If you already have settings, add them with **Import...** from that folder.

- Select the active profile from the list; **Rename**, **Duplicate**, **Delete**.
- **Export...** saves the profile as a `.json` file to share with other controllers; **Import...** loads one.

### 10.2 Display and Range and scale

| Option | Description |
|---|---|
| Display mode | Modern or Analog. |
| Runway position on screen | Left or right. |
| Preferred range | A range you like to use (one of the ranges offered). |
| Ranges offered | Tick boxes 1, 2.5, 5, 10, 15, 20, 30, 40 NM: the range list, knob and keys, Page up / Page down and the mouse wheel step only through the ticked ranges (at least one). New and older profiles: up to 20 NM. If the preferred range is not ticked, the closest ticked one is used. |
| Range at start | Last used, runway default (from `runways.par`) or preferred. |
| Range when the runway changes | Keep current, runway default or preferred. |
| Antenna scan effect, speed | Sweeping beam drawn over the modern display (graphic only); slow, normal, fast. Always on in Analog mode. Colour, line and width of the beam (default 1 px): *Settings → Colours and lines → Antenna scan effect*. |

### 10.3 Tracks and labels

- **History tails**: on/off, number of dots (3–100), one dot every *n* seconds (0.5–10).
- **Symbols** (shape and size) of the track, history dots, threshold, touchdown point and antenna: circle, filled circle, circle with cross, square, diamond, triangle, inverted triangle, +, ×, capsule, line, none, or **Custom...**: the ✎ key (or choosing Custom in the list) opens the symbol editor: draw on a grid of 15 × 15 cells (click or drag; a drawn cell clears it) or type / correct the vector path (SVG syntax, box of 15 units centred on 0,0 = the position of the aircraft), filled or outline, with a preview at 12, 24 and 48 px. Colour and size stay those of the symbol; the custom symbol is saved in the profile (also in Export). The shapes are for the modern display: on the analog scope the track is the **phosphor echo**, a thin vertical bar as on the real PAR scopes, as tall as the **Track** size (12 px by default; e.g. 30 px for the long echoes of some scopes). *Settings → Colours and lines → Echo length at the far end* (1× to 4×, 1× = off) makes the echo grow with the distance, as the beam widens on the real scopes: the echo is the Track size at the touchdown and up to 2×, 3× or 4× that size at the end of the range; the afterglow follows.
- **Edit labels...**: label layouts of the two views ([section 5.2](#52-labels)).

### 10.4 Radar (angles in degrees)

Set once for your radar type:

| Group | Fields | Default |
|---|---|---|
| Approach limits | above / below the glide path, left / right of the centreline | 0.5 / 0.5 / 1.5 / 1.5 |
| Scan limits (physical) | up, down (negative = below the horizon), left, right | new profile: 10 / −1 / 15 / 15 |
| Antenna beam | *Narrow antenna beam moved by the tilt* (on/off); elevation width, azimuth width, elevation centre in neutral (or *automatic: the glide path angle in use*, the default), azimuth centre in neutral (+ right); *Draw the edges of the beam* | new profile: off; when turned on: 7 / 24 / automatic (GP) / 0 |
| Antenna tilt | step; *Azimuth: swap left and right* ([section 7](#7-antenna-tilt)) | 2 / not ticked |

Profiles of the versions before the antenna beam keep their scan limits (e.g. 8 / −1 / 10 / 10), with the beam off; profiles converted by the first test builds with the beam go back to their old scan limits (or to the defaults above when they cannot be found). The scale of the views does not change with the scan limits or the beam: they move the lines, the rest keeps its size.

### 10.5 Units (and Approach and DH)

| Option | Choices |
|---|---|
| Pressure reference (all runways) | QNH (altitudes) or QFE (heights above the threshold) |
| Pressure unit | hPa or inHg |
| Name of the minimum | DA/DH, OCA/OCH, MDA/MDH (section *Approach and DH*) |
| Heights, deviations and vertical speed | ft and ft/min, or m and m/s |
| Altitude scale | shown or not (Modern mode) |
| Altitude grid | horizontal lines every 1000 (ft or m, as the scale) in the vertical view, inside the scan limits; off by default; colour and style: *Altitude lines* in the display colours |
| Default magnetic variation | e.g. `3E`, `2W`: used for the final course of runways without their own variation |
| Log distance scale (*Range and scale*) | how much the log scale compresses: knee 1–10 NM (default 3). LIN / LOG is switched on the main window (see below) or with key **C** |

**Log (compressed) distance scale.** Switched with **LIN | LOG** under the range list (modern display), on the analog console with the range controls (console keys: **LIN / LOG** in the RANGE group, or a SCALE group under the range knob; FIAR keys: the double key **LIN | LOG** in the gold frame of the range, under **PREF**), or with key **C**; saved in the profile. The last miles before the runway get more room and the far ones less, so the traffic stays in view without changing the range. Within the *knee* (distance from the touchdown point) the scale is almost linear, beyond it the miles get closer and closer (a smaller knee compresses more). Heights (elevation view) and lateral offsets (azimuth view) shrink with the distance by the same amount, so every line starting at the touchdown point stays **straight**: the glide path (whatever its angle), the approach limits and the centreline look as with the linear scale, and an aircraft on the glide path stays on its line; only the distances along them change. As a consequence:

- range marks, reminders and the DH lines move to their compressed distance;
- the lines from the antenna (scan limits, antenna beam, scan effect) bend slightly near the runway (the antenna is not at the touchdown point);
- a constant altitude is a curve going down towards the far end: the altitude grid lines bend, and the **altitude scale** is not drawn on the side (it would be right only at the touchdown) but written as values where the altitude lines start, on the upper scan limit;
- deviations from the glide path and the centreline are shown as with the linear scale near the runway, smaller far away (as all the distances there).

---

## 11. Range marks, colours, reminders

These are in the Settings window too: the range marks at the bottom of *Range and scale*, then the sections *Colours and lines* and *Reminders*. Everything is saved in the profile.

### 11.1 Range marks

*Marks per range* (advanced): a table with one row per display range (all of them, also the ranges not ticked in *Ranges offered*). For each range choose:

- which marks are drawn: every **5, 2, 1, ½ or ¼ NM** (marks longer than the range are disabled);
- on which marks the **distance is written** (or none).

Defaults: every 5 NM at 40 and 30 NM; every 2 NM at 20 NM; every NM at 15 and 10 NM; every NM plus dashed half miles without text at 5 NM; 1 NM, ½ and ¼ NM at 2.5 and 1 NM, distance written every ¼ NM. Each ticked box draws those lines with their own style (*Colours and lines*); where ticked boxes overlap (1 NM is also a ½ and a ¼ mile) the largest one wins. Example: to see the 5 NM lines in their own colour at 15 NM, tick *5 NM* in the 15 NM row.

**Range marks as ticks** (*Range marks:* Lines / Ticks / Both, per profile): *Ticks* replaces the lines across the view with short ticks on the **distance axis** (the horizon line in the elevation view, the centreline in the azimuth view), as on some real scopes; *Both* draws lines and ticks. The ticks are at the same distances as the marks of the table; the marks with text have the **tick length** chosen (4–30 px, default 10), the others 60% of it. **Side:** both sides of the axis, above or below. The distance text moves out of the ticks. Colour, width and line style are those of each mark type (table below). The beam is not thickened on ticks.

**Distance text**: *Decimal* (`1.25NM`, `2.5NM`) or *Fractions* (`1 1/4NM`, `2 1/2NM`, `3/4NM`), and its **text size** (10–18 px, default 12). The **NM** box next to each range writes the unit (`1.25NM`) or only the number (`1.25`, `1 1/4`); for older scopes with no text, choose *none* in the range. At the top, **Distance text on all ranges** + **Apply to all** sets every range at once: *Full* or *Number only* (unit on/off; a range with no text gets the default marks again) or *None*. Single ranges can be changed afterwards. **Default** restores the table above; the text format and size stay as chosen.

### 11.2 Colours and lines

For every element of the **Modern** display:

- **colour**: list with swatches, any colour typed as `#RRGGBB`, or the swatch button for the **visual picker** — a colour square (hue across, saturation down), a brightness bar, the new and old colour, the code and the colours used recently. The radar shows the colour while you choose it; **OK** keeps it, **Cancel** puts the old one back. The same picker is used for the phosphor, the reminders and the coordination panel buttons;
- **line style**: solid, dashed, dash-dot, dotted;
- **width**: 1–6 px;
- a **preview**.

Elements: each type of range mark, range text, glide path, centreline, approach limits, scan limits, antenna beam, antenna (symbol; profiles from older versions start with the colour of the scan limits), decision height, runway and threshold, ground, touchdown point, altitude scale, altitude lines, background; and the colours of **plots** (history) and **tracks** inside / outside the limits, of the **coasting tracks** and of the label text.

The last two columns, **Modern** and **Analog**, show or hide each line (not the range marks, which have their own table), separately in the two modes: for example no approach limit lines on the analog scope, as on many old radars. Only the drawing changes: the track is still green or red by the approach limits.

**Analog scope**: one colour, the **phosphor** — yellow-green (P39), amber/yellow, green (P1), orange, blue-white or any colour — used by all elements with different brightness. The **line style and width** set in the table (and the Analog Show column) apply to it too.

### 11.3 Reminders

**Distance reminders** help you remember an action at a given distance from touchdown (e.g. coordinate with the tower, ask for landing clearance).

| Column | Description |
|---|---|
| Distance NM | From touchdown. |
| Show | **Marker**, **Line** or **Both**. |
| Symbol, Size | Marker symbol (from the symbol library) and size. |
| Line, Width | Line style and width. |
| Colour | Colour of marker and line (the phosphor in Analog mode). |
| Note (optional) | Shown only as a **tooltip** when the mouse is over the marker or the line. Nothing is written on the radar picture. |

- The **marker** is drawn in the elevation view, at the horizon line.
- The **line** is drawn across both views and **replaces** the range mark at that distance.
- **Horizon line**: choose *distance text above, markers below* or *distance text below, markers above*.

  ![Horizon line options](images/horizon-options.svg)

- Reminders in this section apply to **all runways**. Each runway can also have **its own** reminders, in the runway editor ([section 12.2](#122-runway-reminders)).

---

## 12. Runways and the runway editor

### 12.1 Runway editor

**Settings... → Edit the runways file** opens the editor of `runways.par`:

- runway list on the left; **New**, **Duplicate** (handy for the opposite end of a runway) and **Delete**;
- fields on the right, checked while typing (wrong values turn red, with a hint);
- **Save** writes the file; the previous one is kept as `runways.par.bak`. Comments and separator lines are kept in place.

| Field | Notes |
|---|---|
| Airport ICAO | e.g. `LIRF` |
| Runway / approach | Free text, e.g. `16L`, or `14 3.0` and `14 2.5` for two glide slopes on the same runway |
| Runway heading | **True** heading, not magnetic. Use the value *Heading for AuroraPAR* of the CRSCalculator (section 12.4) |
| Threshold elevation | ft |
| Threshold latitude / longitude | **Landing threshold.** Any common format: `41.80292`, `41°48'10.5"N`, `414810.5N`, `0121503E` (also `O` for west). A latitude and longitude pasted together are split automatically. |
| Runway length / width | m |
| Glide slope | degrees, usually 3.0 |
| Threshold crossing height | ft, usually 50 |
| Decision height | ft **above the threshold** |
| Default range | NM |
| Touchdown from threshold | m, optional (empty = automatic, TCH / tan(glide slope)) |
| Magnetic variation | optional, e.g. `3E`, `2W` (empty = default in Settings). The resulting final course is shown next to the field. |

### 12.2 Runway reminders

Below the fields, **Distance reminders of this runway** works like the Reminders tab ([section 11.3](#113-reminders)) but only for the selected runway, in addition to the reminders for all runways. They are kept by airport and runway name and saved at once (not with *Save*). If you rename a runway, its reminders stay with the old name.

### 12.3 The `runways.par` file

One runway per line, fields separated by `;`, decimals with a dot. Lines starting with `#` are comments.

```
ICAO;DESIGNATOR;HEADING;ELEVATION;LATITUDE;LONGITUDE;LENGTH_M;WIDTH_M;GLIDE_SLOPE;TCH;MDH;DEFAULT_DISTANCE[;TOUCHDOWN_M[;MAGVAR]]
```

To give the magnetic variation without the touchdown distance leave that field empty: `...;10;;3E`.

Several lines with the same ICAO and designator and different glide path angles are the approaches of one runway ([section 8b](#8b-glide-path-several-approaches-and-unpublished-angle)).

### 12.4 CRSCalculator: runway heading and magnetic variation

A heading taken from the runway number or from the magnetic value on a chart is rounded, and a small error grows with the distance: **0.1° is about 48 m of lateral error at 15 NM** (0.3° is 145 m). **CRSCalculator** (download **CRSCalculator-win-x64.zip** from the same page as AuroraPAR, a small program of its own: `CRSCalculator.exe`) gives the heading from the coordinates of the two thresholds:

1. Write the coordinates of **threshold A** (the landing threshold of the runway whose heading you want, as in the runway editor) and of **threshold B** (the other end, the threshold of the opposite runway), in any format of the editor: **latitude** and **longitude** each in its own box, as the two fields of `runways.par`. Pasting both together, or a whole line of `runways.par`, in the latitude box fills both boxes (so for B you can paste the line of the opposite runway). Use at least 6 decimals (or seconds with 2 decimals): the program shows the **precision** that the digits you wrote allow, and warns when it is too rough.
2. **Heading for AuroraPAR** is the value to write in the *Runway heading* field of the editor (button **Copy**). It is calculated as AuroraPAR does (on a sphere), so that an aircraft on the extended centreline shows zero lateral offset on the radar. Below it the **true heading on the WGS84 ellipsoid**, as the charts give it (it differs by a tenth of a degree or so), and the values for the **opposite runway**.
3. The **distance** between the thresholds, to compare with the published length (type it to get a check): a big difference means a wrong coordinate, a displaced threshold or the wrong end.
4. The **magnetic variation now** at the runway, calculated with the World Magnetic Model (WMM2025, valid 2025-2029, accuracy about 0.3°), for today or another date, with its yearly change. The variation printed on the charts is updated only now and then, so it may differ; type the published one to see the **final course (CRS)** with both and decide which to use (the radar uses the variation of *Settings* or of the runway, [section 12.1](#121-runway-editor)).

**Picking a threshold from the file.** Choose the `runways.par` file (it is remembered), then the airport (ICAO) and the runway: threshold A is filled in automatically; if the opposite end is in the file, B too, otherwise B stays empty and you type it by hand. The option **Only runways without the opposite end** (on by default) lists just those, so you can fill in the missing ones quickly. A line shows what the file already says for that runway, with the button **Write heading and length in runways.par...**: after a confirmation it writes the calculated heading and length in the lines of that runway (and of its opposite end, if it is in the file), keeping the old file as `.bak`.

**Check a whole `runways.par` file.** The button **Check the whole runways.par file...** reads the file, pairs each runway with its opposite end (16L ↔ 34R, 09 ↔ 27; the approaches of the same runway count as one) and shows, for each one, the heading and the length in the file next to the ones calculated from the thresholds. Tick what to write (**Heading** and **Length**) and press **Write the file** (after a confirmation): only those two fields of the lines change, comments and everything else stay as they are, and the old file is kept as `runways.par.bak`. A heading that differs by more than 1° from the file is not ticked (⚠: wrong coordinate or wrong end?). The calculated length is the distance between the two landing thresholds: it is the runway length only when the thresholds are not displaced. Runways whose opposite end is not in the file are listed and left alone. Apart from this, CRSCalculator does not read or write `runways.par`: you copy the values into the editor.

---

## 13. Where the settings are stored

All profiles and options are in one file:

```
%AppData%\AuroraPAR\AuroraPAR.settings.json
```

(type `%AppData%\AuroraPAR` in the Explorer address bar; the full path is also shown at the bottom of the Settings window).

- It is kept when you download a new version.
- **Portable mode**: create an empty file named `AuroraPAR.settings.json` next to `AuroraPAR.exe` and the program uses that one instead (e.g. on a USB stick).
- A damaged file is renamed `AuroraPAR.settings.json.bad` and the defaults are used.

---

## 14. Troubleshooting

| Problem | Solution |
|---|---|
| Coordination panel: *waiting for the tower / radar* although both are on | The two panels are not on the same channel: check the airport, the role (one radar, one tower) and that the **panel code** is the same on both (or empty on both). |
| Coordination panel stays on *connecting...* | The relay on the internet is not reachable from this PC. Most often a **DNS ad blocker** (Pi-hole, AdGuard, NextDNS…): add **broker.emqx.io** and **broker.hivemq.com** to its allow list (whitelist). Otherwise an antivirus web shield, a VPN or a proxy. Check: open the phone panel link in a browser on the same PC. Details of every attempt in the **log**: `%AppData%\AuroraPAR\coord-AuroraPAR.log` (or `coord-AuroraCoord.log`). |
| `STS FAIL` / red STS lamp | Aurora is not running, or its access for third-party programs is off: **Aurora → Settings → Other → Software → 3rd Party software access**. AuroraPAR reconnects by itself. |
| Tracks move in jumps, red `DATA` or flashing **ANT. R/R** | In Aurora set the traffic refresh rate to **0.5 s**. |
| An aircraft is not shown | It is outside the antenna beam: tilt the antenna, widen the beam (or the scan limits) or increase the range. |
| No QNH (`----`) | Aurora has no METAR for the airport yet; it is requested every minute. |
| "Cannot read the runway file" or "No valid runway found" | Keep `runways.par` in the same folder as `AuroraPAR.exe` and check its lines (or use **Settings... → Edit the runways file**). |
| The final course differs from the chart | Check the runway heading (true) and the magnetic variation of the runway. |
| Elevation tail looks like steps | Real altitude updates arrive every few seconds; set one dot every 3 s. |

---

## 15. Quick reference

| | |
|---|---|
| Range | Mouse wheel · range list · RANGE knob |
| Tilt | ↑ ↓ ← → · Home = neutral · tilt buttons · EL/AZ knobs |
| Range · DH · brightness (keys) | Page up / down, End · Shift+↑ ↓, Shift+Home · Ctrl+↑ ↓, Ctrl+Home |
| DH | − / + · type in the box · DH knob (double click on the centre = runway value) |
| Glide path | GP box: published angles, or type an angle + Enter (unpublished, orange) · GP DEG keys (analog) |
| Labels | L = all · drag = move · double click = back · right click = hide |
| Display | A = Modern / Analog (unless locked in the profile) · P / Shift+P = next / previous profile · T = test traffic · C = linear / log distance scale (LIN / LOG keys) |
| Distances | Always from the **touchdown point** |
| Colours | Green inside the approach limits, red outside |
