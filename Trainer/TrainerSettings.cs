namespace AuroraPAR
{
    /// <summary>
    /// Settings of the parts of AuroraPAR v2 Trainer (saved in its settings file, under "Trainer").
    /// </summary>
    internal sealed class TrainerSettings
    {
        /// <summary>Ranges of the surveillance radar (NM from the centre to the nearer edge of the view).</summary>
        public static readonly double[] RadarRanges = [5, 10, 20, 30, 40, 60, 80, 120];
        public const double DefaultRadarRange = 40;

        /// <summary>Range of the surveillance radar (RDR), NM.</summary>
        public double RadarRange { get; set; } = DefaultRadarRange;
        /// <summary>RDR in its own window (two monitors); false: in the main window, where R switches between RDR and PAR.</summary>
        public bool RadarSeparateWindow { get; set; }
        /// <summary>One window: RDR was shown when the program was closed (false: PAR).</summary>
        public bool RadarShown { get; set; }
        /// <summary>Size and position of the RDR window, restored when it opens again.</summary>
        public WindowPlacement? RadarWindow { get; set; }
        /// <summary>Maps of the RDR, by file name in the Maps folder: shown or not, and the colours chosen (null: the file's).</summary>
        public Dictionary<string, MapDisplaySettings> Maps { get; set; } = [];
        /// <summary>Maps of an airport (files of version 2): for each airport (ICAO) and category, what is shown and the colours chosen.</summary>
        public Dictionary<string, Dictionary<string, CategoryDisplaySettings>> AirportMaps { get; set; } = [];
        /// <summary>QNH typed by hand while Aurora is not connected (hPa), used again at the next start; null: none.</summary>
        public int? ManualQnh { get; set; }

        public void Normalize()
        {
            RadarRange = NearestRange(RadarRange);
            Maps ??= [];
            foreach (string key in Maps.Keys.ToList())
            {
                if (Maps[key] == null) Maps.Remove(key);
            }
            AirportMaps ??= [];
            foreach (string airport in AirportMaps.Keys.ToList())
            {
                Dictionary<string, CategoryDisplaySettings>? categories = AirportMaps[airport];
                if (categories == null)
                {
                    AirportMaps.Remove(airport);
                    continue;
                }
                foreach (string category in categories.Keys.ToList())
                {
                    if (categories[category] == null) categories.Remove(category);
                }
            }
            if (ManualQnh is int q && (q < 850 || q > 1090)) ManualQnh = null;
        }

        /// <summary>The settings of a map (created when it is new: shown, with the colours of its file).</summary>
        public MapDisplaySettings MapDisplay(string fileName)
        {
            if (!Maps.TryGetValue(fileName, out MapDisplaySettings? display) || display == null)
            {
                display = new MapDisplaySettings();
                Maps[fileName] = display;
            }
            return display;
        }

        /// <summary>The settings of a category of the maps of an airport (created when it is new: the defaults of the category).</summary>
        public CategoryDisplaySettings CategoryDisplay(string airport, string category)
        {
            string key = (airport ?? "").ToUpperInvariant();
            if (!AirportMaps.TryGetValue(key, out Dictionary<string, CategoryDisplaySettings>? categories) || categories == null)
            {
                categories = [];
                AirportMaps[key] = categories;
            }
            if (!categories.TryGetValue(category, out CategoryDisplaySettings? display) || display == null)
            {
                display = new CategoryDisplaySettings();
                categories[category] = display;
            }
            return display;
        }

        /// <summary>The range of the list closest to a value (the default for a value that is not a number).</summary>
        public static double NearestRange(double range)
        {
            if (!double.IsFinite(range) || range <= 0) return DefaultRadarRange;
            return RadarRanges.OrderBy(r => Math.Abs(r - range)).First();
        }
    }

    /// <summary>How a category of the maps of an airport is shown on the RDR; null: the default of the category.</summary>
    internal sealed class CategoryDisplaySettings
    {
        /// <summary>Lines and symbols shown.</summary>
        public bool? Shapes { get; set; }
        /// <summary>Labels (texts, names of points) shown.</summary>
        public bool? Labels { get; set; }
        /// <summary>Colour of the lines and symbols (#RRGGBB).</summary>
        public string? ShapeColour { get; set; }
        /// <summary>Colour of the labels (#RRGGBB); null: the colour of the lines and symbols.</summary>
        public string? LabelColour { get; set; }
        /// <summary>Symbol of the points of the category (name of a <see cref="ATCRoom.Maps.MapSymbol"/>); null: the one saved in the map.</summary>
        public string? Symbol { get; set; }
    }

    /// <summary>How a map is shown on the RDR.</summary>
    internal sealed class MapDisplaySettings
    {
        public bool Visible { get; set; } = true;
        /// <summary>Colour of the lines (#RRGGBB), null: the colour of the map file.</summary>
        public string? LineColour { get; set; }
        /// <summary>Colour of the labels (#RRGGBB), null: the colour of the map file.</summary>
        public string? LabelColour { get; set; }
    }
}
