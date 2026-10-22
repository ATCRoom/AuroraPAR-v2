namespace ATCRoom.Maps
{
    /// <summary>
    /// One category of map (what the user turns on and off, and colours, in the Trainer). Every category has its shapes
    /// (lines, symbols) and, when <see cref="HasLabels"/>, its labels (the texts), each with its own on / off and colour.
    /// </summary>
    public sealed record MapCategoryInfo(string Id, string Name, bool HasLabels, string DefaultColour, bool ShapesOn, bool LabelsOn);

    /// <summary>The categories of the maps of an airport (first group of the Trainer maps).</summary>
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

        /// <summary>In the order of the lists. Shown by default: boundaries, MVA with labels, airports with labels.</summary>
        public static readonly IReadOnlyList<MapCategoryInfo> All =
        [
            new(Boundaries, "Geographic boundaries", false, "#6B7F8C", true, false),
            new(Mva, "MVA (vectoring minimum altitudes)", true, "#2F6F86", true, true),
            new(Fix, "FIX", true, "#C8B04A", false, false),
            new(Vrp, "VRP (visual reporting points)", true, "#C586C0", false, false),
            new(Airports, "Airports", true, "#8FD68F", true, true),
            new(Navaids, "Navaids (VOR, NDB, TACAN)", true, "#E08A4A", false, false),
            new(Ctr, "CTR", true, "#4A9BE0", false, false),
            new(Atz, "ATZ", true, "#E05A5A", false, false)
        ];

        public static MapCategoryInfo? Find(string? id) =>
            All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The id written as in <see cref="All"/>, or null when it is not a category.</summary>
        public static string? Normal(string? id) => Find(id)?.Id;

        public static string NameOf(string? id) => Find(id)?.Name ?? (id ?? "");
    }
}
