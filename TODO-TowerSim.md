# Tower circuit simulator (instructor / student) — idea for a new project

**Status: idea only. Start when AuroraPAR (including the Instructor version) is finished.** To be discussed again then; nothing decided yet.

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

## To decide when we start

- Name of the project and its repository (probably in the dedicated GitHub organisation, see TODO.md).
- What is shared with AuroraPAR (code library or copy) and what is new.
- What the student sees exactly (labels, data from Aurora-like tags, aerodrome map, runways and circuit drawing).
- How the instructor's two levels look (mockups first).
- The aircraft database: which types, which data, how the user can add types.
- Pre-set routes: how they are defined and edited (per aerodrome and runway in use).
- Whether the simulated traffic is ever mixed with IVAO traffic (as for the PAR Instructor: only between instructor and student, never on the network).
