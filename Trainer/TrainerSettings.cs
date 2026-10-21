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

        public void Normalize()
        {
            RadarRange = NearestRange(RadarRange);
        }

        /// <summary>The range of the list closest to a value (the default for a value that is not a number).</summary>
        public static double NearestRange(double range)
        {
            if (!double.IsFinite(range) || range <= 0) return DefaultRadarRange;
            return RadarRanges.OrderBy(r => Math.Abs(r - range)).First();
        }
    }
}
