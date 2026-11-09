# ATCRoom map files (open format)

The maps of **AuroraPAR v2 Trainer** (surveillance radar RDR) and of **TWR Trainer** are plain JSON files in an open
format. They are made by **ATCRoom Map Creator** from files of other formats, or written by hand, and can be used by
other programs. The home of the Map Creator and of this format is the repository ATCRoom/TWR-Trainer.

## Where they are

ATCRoom Map Creator 2.0 saves into the maps folder **shared by the programs**:

- normally `%AppData%\ATCRoom\Maps\` (Windows);
- in portable mode (the settings file of a program, `AuroraPARTrainer.settings.json` or `TWRTrainer.settings.json`, next
  to `ATCRoomMapCreator.exe`), the folder `Maps` next to the program.

AuroraPAR v2 Trainer 1.0.0 still reads only its own folder, `%AppData%\AuroraPARTrainer\Maps\` (portable: `Maps` next to
`AuroraPARTrainer.exe`): for it, use **Save as...** into that folder. "Open an existing map" lists the maps of both folders.

Version 2: **one file per airport**, named with its ICAO (`LIPX.json`). The Trainer loads the maps of the airport in use
by itself; in **RDR → Maps...** you choose, for each category, whether its lines / symbols and its labels are shown and
their colours (kept for each airport). **Reload** reads the folder again. Files of version 1 (no airport) are still read and
shown for every airport, as before.

> **⚠ Personal use only — simulation only.** The Map Creator reads files of any kind (IVAO, VATSIM, AIP, KML…), but
> the maps you make from them are **for your own use only**: **do not distribute them** (no sharing, no uploading)
> without the permission of the owner of the data. They are for flight simulation and training on AuroraPAR only and
> **must never be used for real-world navigation or air traffic control**.

## The file (version 2, and version 3 with the ground)

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
| `FormatVersion` | whole number: 2, or 3 when the file has the block `Ground` (1: a whole map without airport and categories) |
| `Name` | name of the map (empty: the file name) |
| `Airport` | ICAO of the airport the map is for (empty: version 1, shown for every airport) |
| `Source` / `Sources` | optional free text: where the data come from, who made the map, the licence; `Sources` per category |
| `Lines` | lines: `Name` (may be empty), `Category`, `Points` each `[latitude, longitude]`; a line is closed when its last point repeats the first |
| `Labels` | texts on the map: `Text`, `Category`, `Latitude`, `Longitude`, `Size` (8 is the usual size) |
| `Points` | named points: `Name` (the label), `Description` (long name, not shown), `Category`, `Latitude`, `Longitude`, `Symbol` |
| `Symbol` | `Point` (FIX, triangle), `Airport` (circle), `Vrp` (square), `Vor` (hexagon with dot), `Vortac` (hexagon with ticks), `Tacan` (diamond with dot), `Ndb` (circle with dot) |
| `LineColour` / `LabelColour` | version 1 only: colours `#RRGGBB` of all the lines and of all the labels |
| `Ground` | version 3, optional: the ground layout of the aerodrome (see below) |

Categories (`Category`): `Boundaries` (geographic boundaries), `Mva` (vectoring minimum altitudes, with labels), `Fix`,
`Vrp`, `Airports`, `Navaids` (VOR, NDB, TACAN), `Ctr`, `Atz` (the last five with labels, the name of a point is its label),
and the ground categories below. What a part of a source file is (a CTR, an ATZ, a sector, a taxiway...) is decided by the
user in the Map Creator; the name only suggests it.

### The ground layout (version 3)

```json
"Ground": {
  "Areas":  [ { "Name": "DFAB", "Category": "GroundTaxiway", "Points": [ [40.2463, 18.1375], [40.2460, 18.1378], [40.2463, 18.1375] ] } ],
  "Lines":  [ { "Name": "C", "Category": "GroundStopBar", "Points": [ [40.2460, 18.1279], [40.2459, 18.1281] ] } ],
  "Labels": [ { "Text": "M west", "Category": "GroundLabels", "Latitude": 40.2440, "Longitude": 18.1352, "Size": 8 } ]
}
```

| List | Categories |
| --- | --- |
| `Areas` (filled, drawn in this order) | `GroundGrass` (grass, aerodrome boundary), `GroundTaxiway`, `GroundApron`, `GroundConcrete` (run-up areas...), `GroundBuilding`, `GroundRunway` (paved surface) |
| `Lines` | `GroundRunwayOutline` (the runway itself), `GroundCentreline` (taxiway centrelines), `GroundStopBar` (stop bars, holding points) |
| `Labels` | `GroundLabels` (names of taxiways and aprons, exactly as written) |

The ground is in a block of its own so that the programs made before version 3 (AuroraPAR v2 Trainer 1.0.0, ATCRoom Map
Creator 1.x) do not see it and cannot spoil it: they keep the block as it is when they save.

**Nothing is lost:** a program keeps what it does not know — fields of the file, of a line, of a label, of a point or of
the block `Ground`, and categories it does not know (kept with their name, not emptied) — and writes it back when it saves.

Coordinates in decimal degrees (WGS 84), north and east positive. Comments (`//`, `/* */`) and trailing commas are
accepted when reading; fields the program does not know are kept. Points with impossible coordinates and lines with
fewer than two points are skipped.

## What ATCRoom Map Creator reads

Many files (or a whole folder) at once, shown together on one picture; each part gets a category chosen by the user; a circle
around the airport keeps only what is inside (an area of the ground is kept whole when a corner of it is inside); one file
per airport is saved (into the shared maps folder, merging into the
existing file of the airport: the categories saved replace the ones in the file, the others are kept). A map can be opened
again ("Open an existing map...", or a `.json` with Browse) to add to it or correct it; a category of it set to "not used"
is then removed from the file when it is saved into the maps.

**Coordinates of the IVAO / Aurora files**, all the forms: DMS with dots `N038.10.04.627`, DMS without dots
`N0381004627`, decimal degrees without a letter `38.02235785` (south and west with a minus), and the short forms with the
missing parts at zero: `S040` (40°), `N038.10` (38°10′), `E024.33.34` (24°33′34″), `N03810` (38°10′), `E0243334`
(24°33′34″); with more than two digits after the dot it cannot be minutes and is read as decimal degrees
(`N041.00850773`). In the EuroScope files a letter and one dot is always decimal degrees (`N013.32861`). Reference points (`NAME;NAME;`,
a FIX / VOR / NDB instead of the coordinates) are not read yet.

| Format | What becomes the map |
| --- | --- |
| IVAO `.geo` | segments `lat;lon;lat;lon;TAG;` joined into lines, one part per tag (COAST suggested: boundaries; the tags of a ground layout - TAXIWAY, APRON, RUNWAY, TAXI_CENTER, STOPLINE... - suggest the ground categories; a tag that mixes several kinds, BUILDING for the boundary too, needs the user's choice: the KML or the `.pol` is better for the areas) |
| IVAO `.fix` | `NAME;lat;lon;TYPE;`: terminal and both (suggested: FIX), en-route apart, hidden skipped |
| IVAO `.vor`, `.ndb` | navaids with symbol (VOR, VORTAC, TACAN, NDB); hidden and impossible rows skipped and named |
| IVAO `.ap` | airports (label ICAO, name as description); ICAO ending ZZ and hidden skipped |
| IVAO `.vfi` | VFR points `NAME;CODE;lat;lon;` or `NAME;lat;lon;`: the label is the first block, the second or both (`PI-SIERRA`), chosen by the user (first choice: both when the second block repeats, as a prefix) |
| IVAO `lartcc` / `hartcc` | one part per airspace ID (T rows = border, L rows = label); CTR / ATZ suggested by the name |
| IVAO `.str` | only the groups with `MAPS` in the second field (CTR, ATZ...), `<br>` starts a new path; procedures (STAR, IAP) not used yet |
| Aurora map file (IVAO) `.mva` | `T;NAME;lat;lon;` lines, `L;NAME;lat;lon;TEXT;SIZE;` labels (suggested: MVA); coordinates `N047.10.47.653` |
| EuroScope `.ese` (VATSIM) | `[FREETEXT]` texts, `[AIRSPACE]` sector lines and circles, MSAW areas with their altitude (suggested: MVA); `OFFSET` applied |
| EuroScope `.sct` (VATSIM) | `[VOR]`, `[NDB]` (navaids with their symbol), `[FIXES]`, `[AIRPORT]`; `[GEO]` segments joined into lines, one part for each colour (the coast suggested: boundaries). Runways, SID, STAR, ARTCC, airways and regions are not read |
| KML / KMZ (Google Earth) | polygons → closed lines with the name as label at the centre (a 3D one: only the outline in plan), lines, stray points; one part for the CTR (suggested: CTR), one for the ATZ (suggested: ATZ), one for each folder of the others. A ground layout: the folder names suggest the ground categories (Twys, Rwys, Rwy_Boundaries, TwyCtrls, Stopbars, Ad_Concrete_Areas, Ad_Boundaries...), the aprons among the concrete areas are a part of their own; into the ground the names of the areas are not labels and a point gives a label without what follows an underscore (`A_C` → `A`) |
| Ground: filled polygons `.pol` | `STATIC;TYPE;1;TYPE;` starts a polygon (TYPE: GRASS, TAXIWAY, APRON, CONCRETE, BUILDING, RUNWAY...), then one corner per row `lat;lon;`; the comment before is its name; one part per TYPE, ground category suggested |
| Ground: labels `.txi` | `TEXT;ICAO;lat;lon;` labels of taxiways and aprons (suggested: ground labels), the text exactly as written (`M west`); the airport from the file name, a row with another ICAO kept and named in the notes |
| AIP lists (PDF) | **ENR 6.3.1** visual reporting points (both lists; label = code, description = name; a point listed twice kept once, suggested VRP); **ENR 4.4** significant points (code and coordinates, suggested FIX); **ENR 4.1.1 / 4.1.2** radio aids (read by the place of the words: label = ID, description = name and type; VOR, VORTAC, TACAN, NDB / L; a DME alone is skipped; suggested Navaids). The kind of list is recognised from the title on the first page. |
| AIP CTR and ATZ (PDF) | **ENR 2.1.2.x** (CTR, also with zones such as Catania: one polygon per zone), **ENR 2.1.3** (military ATZ) and the **AD 2** sheets of the airports (ATZ): polygons with arcs ("then arc of circle in clockwise direction radius ... centred on ... till point ..."), "to point of origin" and "Circular area centered on ... within a ... NM radius". Only the first column of the table is read; each polygon has its name as label (CTR and ATZ suggested). An arc that the text sends more than 300° round is turned the other way and the note says so. |
| AIP table of minimum altitudes (PDF) | the areas "Line joining following points:" and arcs, the altitude of the left column as the label (suggested: MVA). The chart pages are not converted. |
