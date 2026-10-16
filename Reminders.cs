using System.Globalization;

namespace AuroraPAR
{
    /// <summary>How a distance reminder is shown.</summary>
    internal enum ReminderShow
    {
        Marker,
        Line,
        Both
    }

    /// <summary>
    /// Reminder at a distance from touchdown, for an action of the controller there (e.g. coordinate with the
    /// tower): a symbol above the distance text of the elevation view, a coloured line across both views, or both.
    /// </summary>
    internal class DistanceReminder
    {
        /// <summary>Distance from the touchdown point, NM.</summary>
        public double Distance { get; set; } = 4;
        public ReminderShow Show { get; set; } = ReminderShow.Marker;
        public SymbolShape Symbol { get; set; } = SymbolShape.TriangleDown;
        public double Size { get; set; } = 10;
        public LineDash Dash { get; set; } = LineDash.Solid;
        public double Width { get; set; } = 2;
        public string Color { get; set; } = "#FFB000";
        /// <summary>Optional note, shown only as a tooltip.</summary>
        public string? Note { get; set; }

        public bool HasMarker => Show != ReminderShow.Line;
        public bool HasLine => Show != ReminderShow.Marker;

        public const double MaxDistance = 20;

        public string ToolTip()
        {
            string distance = AuroraPAR.Distance.Kilometres
                ? (Distance * DistanceUnits.KilometresPerNM).ToString("0.0#", CultureInfo.InvariantCulture) + " km"
                : Distance.ToString("0.0#", CultureInfo.InvariantCulture) + " NM";
            return string.IsNullOrWhiteSpace(Note) ? distance : $"{distance} – {Note.Trim()}";
        }

        public void Normalize()
        {
            Distance = double.IsNaN(Distance) ? 4 : Math.Clamp(Distance, 0.05, MaxDistance);
            if (!Enum.IsDefined(Show)) Show = ReminderShow.Marker;
            if (!Enum.IsDefined(Symbol) || Symbol == SymbolShape.None || Symbol == SymbolShape.Line || Symbol == SymbolShape.Custom) Symbol = SymbolShape.TriangleDown;
            Size = double.IsNaN(Size) ? 10 : Math.Clamp(Size, 4, 30);
            if (!Enum.IsDefined(Dash)) Dash = LineDash.Solid;
            Width = double.IsNaN(Width) ? 2 : Math.Clamp(Width, 1, 6);
            if (!ColorText.TryParse(Color, out _)) Color = "#FFB000";
        }

        public static void Normalize(List<DistanceReminder>? list)
        {
            if (list == null) return;
            list.RemoveAll(r => r == null);
            foreach (DistanceReminder reminder in list) reminder.Normalize();
        }
    }
}
