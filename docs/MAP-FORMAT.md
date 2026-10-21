# ATCRoom map files (open format)

The maps of the surveillance radar (RDR) of **AuroraPAR v2 Trainer** are plain JSON files in an open format. They are
made by **ATCRoom Map Creator** from files of other formats, or written by hand, and can be used by other programs.

## Where they are

In the folder `Maps` beside the settings file of the Trainer:

- normally `%AppData%\AuroraPARTrainer\Maps\` (Windows);
- in portable mode (a file `AuroraPARTrainer.settings.json` next to `AuroraPARTrainer.exe`), the folder `Maps` next to
  the program.

Every `.json` file of the folder is a map. In the Trainer: **RDR → Maps...** shows them, turns each one on or off and
sets its two colours; **Reload** reads the folder again.

The maps you make from data of IVAO, VATSIM or an AIP are for your own use: do not pass them on without the permission
of the owner of the data.

## The file

```json
{
  "FormatVersion": 1,
  "Name": "MVA LIPI",
  "Source": "Made by hand from ...",
  "LineColour": "#2F6F86",
  "LabelColour": "#2F6F86",
  "Lines": [
    { "Name": "7000", "Points": [ [45.903, 12.539], [45.970, 12.616], [45.903, 12.539] ] }
  ],
  "Labels": [
    { "Text": "70", "Latitude": 46.189, "Longitude": 13.011, "Size": 8 }
  ],
  "Points": [
    { "Name": "RIV", "Latitude": 46.03, "Longitude": 13.05, "Symbol": "Point" }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `FormatVersion` | whole number, 1 today |
| `Name` | name of the map in the list (empty: the file name) |
| `Source` | optional free text: where the data come from, who made the map, its licence |
| `LineColour` / `LabelColour` | colours `#RRGGBB` of all the lines and of all the labels (the label colour starts equal to the line colour) |
| `Lines` | lines: `Name` (may be empty) and `Points`, each `[latitude, longitude]`; a line is closed when its last point repeats the first |
| `Labels` | texts on the map: `Text`, `Latitude`, `Longitude`, `Size` (8 is the usual size) |
| `Points` | named points: `Name`, `Latitude`, `Longitude`, `Symbol` = `Point` (small triangle) or `Airport` (small circle) |

Coordinates in decimal degrees (WGS 84), north and east positive. Comments (`//`, `/* */`) and trailing commas are
accepted when reading; fields the program does not know are kept. Points with impossible coordinates and lines with
fewer than two points are skipped.

## What ATCRoom Map Creator reads

| Format | What becomes the map |
| --- | --- |
| Aurora map file (IVAO) | `T;NAME;lat;lon;` lines (consecutive points with the same name are one line), `L;NAME;lat;lon;TEXT;SIZE;` labels; coordinates `N047.10.47.653` |
| EuroScope `.ese` (VATSIM) | `[FREETEXT]` texts, `[AIRSPACE]` sector lines (`SECTORLINE` + `COORD`), circles (`CIRCLE_SECTORLINE` around a coordinate), MSAW areas (`MSAW:name:altitude` + `COORD`) with their altitude as a label; `OFFSET` lines applied. Each part can be left out. |
| AIP table of minimum altitudes (PDF) | the areas written "Line joining following points:" with their coordinates (`44°16'56" N 008°39'51" E`) and arcs ("arc of circle ... radius 17 NM centred on ... till point ..."), the altitude of the left column as the label (`7000 FT` → `70`, `FL90` → `90`). The chart pages (drawings) are not converted. |
