namespace ATCRoom.Maps
{
    /// <summary>
    /// One category of map (what the user turns on and off, and colours, in the programs). Every category has its shapes
    /// (lines, symbols) and, when <see cref="HasLabels"/>, its labels (the texts), each with its own on / off and colour.
    /// <see cref="Ground"/>: a part of the ground layout of the aerodrome (saved in the block "Ground" of the file, so the
    /// programs made before it do not see it and cannot spoil it); <see cref="Area"/>: its shapes are filled areas.
    /// </summary>
    public sealed record MapCategoryInfo(string Id, string Name, bool HasLabels, string DefaultColour, bool ShapesOn, bool LabelsOn,
        bool Ground = false, bool Area = false);

    /// <summary>The categories of the maps of an airport.</summary>
    public static class MapCategory
    {
        public const string Boundaries = "Boundaries";
        public const string Mva = "Mva";
        public const string Fix = "Fix";
        public const string Vrp = "Vrp";
        public const string Airports = "Airports";
        public const string Navaids = "Navaids";
        public const string Ctr = "Ctr";
        public const string Atz = "Atz";

        // Ground layout (format version 3). The areas are drawn in this order: grass first, the runways on top.
        public const string GroundGrass = "GroundGrass";
        public const string GroundTaxiway = "GroundTaxiway";
        public const string GroundApron = "GroundApron";
        public const string GroundConcrete = "GroundConcrete";
        public const string GroundBuilding = "GroundBuilding";
        public const string GroundRunway = "GroundRunway";
        public const string GroundRunwayOutline = "GroundRunwayOutline";
        public const string GroundCentreline = "GroundCentreline";
        public const string GroundStopBar = "GroundStopBar";
        public const string GroundLabels = "GroundLabels";

        /// <summary>In the order of the lists (and of the drawing). Shown by default: boundaries, MVA with labels, airports with labels, the ground.</summary>
        public static readonly IReadOnlyList<MapCategoryInfo> All =
        [
            new(Boundaries, "Geographic boundaries", false, "#6B7F8C", true, false),
            new(Mva, "MVA (vectoring minimum altitudes)", true, "#2F6F86", true, true),
            new(Fix, "FIX", true, "#C8B04A", false, false),
            new(Vrp, "VRP (visual reporting points)", true, "#C586C0", false, false),
            new(Airports, "Airports", true, "#8FD68F", true, true),
            new(Navaids, "Navaids (VOR, NDB, TACAN)", true, "#E08A4A", false, false),
            new(Ctr, "CTR", true, "#4A9BE0", false, false),
            new(Atz, "ATZ", true, "#E05A5A", false, false),
            new(GroundGrass, "Ground: grass, aerodrome boundary", false, "#2E4A2A", true, false, Ground: true, Area: true),
            new(GroundTaxiway, "Ground: taxiways", false, "#3B4650", true, false, Ground: true, Area: true),
            new(GroundApron, "Ground: aprons", false, "#46505A", true, false, Ground: true, Area: true),
            new(GroundConcrete, "Ground: concrete (run-up areas...)", false, "#4A535C", true, false, Ground: true, Area: true),
            new(GroundBuilding, "Ground: buildings", false, "#6B6F75", true, false, Ground: true, Area: true),
            new(GroundRunway, "Ground: runways (paved surface)", false, "#2B2F33", true, false, Ground: true, Area: true),
            new(GroundRunwayOutline, "Ground: runway outlines", false, "#8A949C", true, false, Ground: true),
            new(GroundCentreline, "Ground: taxiway centrelines", false, "#C9A227", true, false, Ground: true),
            new(GroundStopBar, "Ground: stop bars, holding points", false, "#D04040", true, false, Ground: true),
            new(GroundLabels, "Ground: labels (taxiways, aprons)", true, "#E6C95A", false, true, Ground: true)
        ];

        public static MapCategoryInfo? Find(string? id) =>
            All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The id written as in <see cref="All"/>, or null when it is not a category.</summary>
        public static string? Normal(string? id) => Find(id)?.Id;

        public static string NameOf(string? id) => Find(id)?.Name ?? (id ?? "");

        /// <summary>A category of the ground layout.</summary>
        public static bool IsGround(string? id) => Find(id)?.Ground == true;

        /// <summary>A category whose shapes are filled areas (the ground areas).</summary>
        public static bool IsArea(string? id) => Find(id)?.Area == true;

        /// <summary>The place of a category in <see cref="All"/> (the drawing order); one that is not known comes last.</summary>
        public static int OrderOf(string? id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return All.Count;
        }
    }
}
