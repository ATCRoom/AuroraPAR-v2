# 8. A lesson between two PCs

The instructor flies the aircraft on his PC, the student sees them on the PAR and RDR of his own PC, also in another
city. Both PCs run AuroraPAR v2 Trainer; nothing else to install, no port to open.

## Step 1 — Instructor: make the code

**Lesson…** (beside Coordination) → choose **Instructor** → **New code** → **Connect**. Tell the code to the student
(by voice, chat…).

## Step 2 — Student: type the code

**Lesson…** → choose **Student** → type the code → **Connect**.

## Step 3 — Check the link

The **Lesson** key turns blue on both PCs (orange: still waiting). On the Instructor+ the radar shows
`LESSON <code>: STUDENT LINKED`.

## Step 4 — Choose the runway on the student's PC

The Instructor+ follows the student's runway (while no aircraft flies): BEAM and TRAINEE PAR show what the student
really sees. Choose the same airport on the instructor's PC too, for the maps.

## Step 5 — Fly

Start the aircraft as in the other tutorials. With IVAO: each PC with its own Aurora; without IVAO: only the
simulated aircraft, the student's PAR shows **STS SIM** and takes the QNH of the instructor.

## Things to know

- If the link stops, the student sees **LESSON: LINK LOST** and the aircraft disappear after 5 s; the link comes back
  by itself.
- On the student's PC the Instructor windows do not open during the lesson.
- Whoever has the code can see the lesson: make a new code for each lesson. **Disconnect** ends it.
- To try it alone: two copies of the Trainer in two folders, each with an empty file `AuroraPARTrainer.settings.json` beside the program (portable mode), one instructor, one student.

The full description is in the [Instructor+ guide](../TRAINER.md#14-a-lesson-between-two-pcs).
