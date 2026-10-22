# ATCRoom map files (open format)

The maps of the surveillance radar (RDR) of **AuroraPAR v2 Trainer** are plain JSON files in an open format. They are
made by **ATCRoom Map Creator** from files of other formats, or written by hand, and can be used by other programs.

## Where they are

In the folder `Maps` beside the settings file of the Trainer:

- normally `%AppData%\AuroraPARTrainer\Maps\` (Windows);
- in portable mode (a file `AuroraPARTrainer.settings.json` next to `AuroraPARTrainer.exe`), the folder `Maps` next to
  the program.

Version 2: **one file per airport**, named with its ICAO (`LIPX.json`). The Trainer loads the maps of the airport in use
by itself; in **RDR → Maps...** you choose, for each category, whether its lines / symbols and its labels are shown and
their colours (kept for each airport). **Reload** reads the folder again. Files of version 1 (no airport) are still read and
shown for every airport, as before.

The maps you make from data of IVAO, VATSIM or an AIP are for your own use: do not pass them on without the permission
of the owner of the data.

## The file (version 2)

```json
{
  "FormatVersion": 2,
  "Name": "LIPX",
  "Airport": "LIPX",
  "Source": "",
  "Sources": { "Fix": "IVAO sector file, 2026" },
  "Lines": [
    { "Name": "7000", "Category": "Mva", "Points": [ [45.903, 12.539], [45.970, 12.616], [45.903, 12.539] ] }
  ],
  "Labels": [
    { "Text": "70", "Category": "Mva", "Latitude": 46.189, "Longitude": 13.011, "Size": 8 }
  ],
  "Points": [
    { "Name": "RIV", "Description": "", "Category": "Fix", "Latitude": 46.03, "Longitude": 13.05, "Symbol": "Point" }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `FormatVersion` | whole number, 2 today (1: a whole map without airport and categories) |
| `Name` | name of the map (empty: the file name) |
| `Airport` | ICAO of the airport the map is for (empty: version 1, shown for every airport) |
| `Source` / `Sources` | optional free text: where the data come from, who made the map, the licence; `Sources` per category |
| `Lines` | lines: `Name` (may be empty), `Category`, `Points` each `[latitude, longitude]`; a line is closed when its last point repeats the first |
| `Labels` | texts on the map: `Text`, `Category`, `Latitude`, `Longitude`, `Size` (8 is the usual size) |
| `Points` | named points: `Name` (the label), `Description` (long name, not shown), `Category`, `Latitude`, `Longitude`, `Symbol` |
| `Symbol` | `Point` (FIX, triangle), `Airport` (circle), `Vrp` (square), `Vor` (hexagon with dot), `Vortac` (hexagon with ticks), `Tacan` (diamond with dot), `Ndb` (circle with dot) |
| `LineColour` / `LabelColour` | version 1 only: colours `#RRGGBB` of all the lines and of all the labels |

Categories (`Category`): `Boundaries` (geographic boundaries), `Mva` (vectoring minimum altitudes, with labels), `Fix`,
`Vrp`, `Airports`, `Navaids` (VOR, NDB, TACAN), `Ctr`, `Atz` (the last five with labels, the name of a point is its label).
What a part of a source file is (a CTR, an ATZ, a sector...) is decided by the user in the Map Creator; the name only suggests it.

Coordinates in decimal degrees (WGS 84), north and east positive. Comments (`//`, `/* */`) and trailing commas are
accepted when reading; fields the program does not know are kept. Points with impossible coordinates and lines with
fewer than two points are skipped.

## What ATCRoom Map Creator reads

Many files (or a whole folder) at once, shown together on one picture; each part gets a category chosen by the user; a circle
around the airport keeps only what is inside; one file per airport is saved (into the maps of the Trainer, merging into the
existing file of the airport).

| Format | What becomes the map |
| --- | --- |
| IVAO `.geo` | segments `lat;lon;lat;lon;TAG;` joined into lines, one part per tag (COAST suggested: boundaries); coordinates with or without dots |
| IVAO `.fix` | `NAME;lat;lon;TYPE;`: terminal and both (suggested: FIX), en-route apart, hidden skipped |
| IVAO `.vor`, `.ndb` | navaids with symbol (VOR, VORTAC, TACAN, NDB); hidden and impossible rows skipped and named |
| IVAO `.ap` | airports (label ICAO, name as description); ICAO ending ZZ and hidden skipped |
| IVAO `.vfi` | VFR points (label = code, description = name) |
| IVAO `lartcc` / `hartcc` | one part per airspace ID (T rows = border, L rows = label); CTR / ATZ suggested by the name |
| IVAO `.str` | only the groups with `MAPS` in the second field (CTR, ATZ...), `<br>` starts a new path; procedures (STAR, IAP) not used yet |
| Aurora map file (IVAO) `.mva` | `T;NAME;lat;lon;` lines, `L;NAME;lat;lon;TEXT;SIZE;` labels (suggested: MVA); coordinates `N047.10.47.653` |
| EuroScope `.ese` (VATSIM) | `[FREETEXT]` texts, `[AIRSPACE]` sector lines and circles, MSAW areas with their altitude (suggested: MVA); `OFFSET` applied |
| AIP table of minimum altitudes (PDF) | the areas "Line joining following points:" and arcs, the altitude of the left column as the label (suggested: MVA). The chart pages are not converted. |
