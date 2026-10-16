using System.Globalization;

namespace AuroraPAR
{
    /// <summary>
    /// Values that can be shown in a track label.
    /// </summary>
    internal enum LabelField
    {
        None,
        Callsign,
        DistanceFromTouchdown,
        /// <summary>Altitude (A) with QNH, height above the threshold (H) with QFE.</summary>
        Altitude,
        GroundSpeed,
        VerticalSpeed,
        /// <summary>Above (U) or below (D) the ideal glide path.</summary>
        GlidePathDeviation,
        /// <summary>Left (L) or right (R) of the extended centreline, as seen by the pilot.</summary>
        CenterlineDeviation,
        /// <summary>SSR (transponder) code, as A1234 (PAR+SSR radars).</summary>
        SsrCode,
        /// <summary>Identity given by the radar when callsign and code are unknown: see <see cref="TrackIdentities"/>.</summary>
        TrackId
    }

    /// <summary>Row of the label drawn outside the frame.</summary>
    internal enum LabelOutsideRow
    {
        None,
        /// <summary>The first row, above the frame (like a title).</summary>
        First,
        /// <summary>The last row, below the frame.</summary>
        Last
    }

    /// <summary>Frame around the framed rows of a label: any of the four sides, with colour, width and dash style of the theme.</summary>
    internal sealed class LabelFrameBox : System.Windows.Controls.Decorator
    {
        public bool Top, Right, Bottom, Left;
        public System.Windows.Media.Pen? Pen;

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            if (Pen == null) return;
            double h = Pen.Thickness / 2;
            System.Windows.Size size = RenderSize;
            if (Top) dc.DrawLine(Pen, new System.Windows.Point(0, h), new System.Windows.Point(size.Width, h));
            if (Bottom) dc.DrawLine(Pen, new System.Windows.Point(0, size.Height - h), new System.Windows.Point(size.Width, size.Height - h));
            if (Left) dc.DrawLine(Pen, new System.Windows.Point(h, 0), new System.Windows.Point(h, size.Height));
            if (Right) dc.DrawLine(Pen, new System.Windows.Point(size.Width - h, 0), new System.Windows.Point(size.Width - h, size.Height));
        }
    }

    /// <summary>
    /// Inner line of the label frame: between two columns (vertical, in the middle of the gap after its column) or
    /// below a row (horizontal), from edge to edge of the framed rows. It follows the size of the grid cells it spans.
    /// </summary>
    internal sealed class LabelGridLine : System.Windows.FrameworkElement
    {
        public bool Vertical;
        /// <summary>Gap between two columns (the right margin of the cells), the line is in its middle.</summary>
        public double ColumnGap;
        /// <summary>How far the line goes beyond the cells at each end (to reach the sides of the frame).</summary>
        public double ExtendStart, ExtendEnd;
        public System.Windows.Media.Pen Pen = null!;

        public LabelGridLine() => IsHitTestVisible = false;

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            if (Vertical)
            {
                double x = ActualWidth - ColumnGap / 2;
                dc.DrawLine(Pen, new System.Windows.Point(x, -ExtendStart), new System.Windows.Point(x, ActualHeight + ExtendEnd));
            }
            else
            {
                double y = ActualHeight;
                dc.DrawLine(Pen, new System.Windows.Point(-ExtendStart, y), new System.Windows.Point(ActualWidth + ExtendEnd, y));
            }
        }
    }

    /// <summary>Builds the visual of a label (frame, field outside the frame, cells) for the radar and for the preview.</summary>
    internal static class LabelVisual
    {
        private const double FramePadding = 3;
        /// <summary>Space between two columns, and above / below an inner line between rows.</summary>
        private const double ColumnGap = 8;
        private const double RowLineGap = 2;

        /// <summary>Row kept outside the frame, or -1 (a single row cannot be outside).</summary>
        public static int OutsideIndex(LabelLayout layout) =>
            layout.Rows < 2 ? -1 : layout.OutsideRow == LabelOutsideRow.First ? 0 : layout.OutsideRow == LabelOutsideRow.Last ? layout.Rows - 1 : -1;

        /// <summary>Text colour of a row: its own for the field outside the frame.</summary>
        public static System.Windows.Media.Brush TextBrush(LabelLayout layout, int row, Theme theme) =>
            theme.Brush(row == OutsideIndex(layout) ? StyleElement.LabelTextOutside : StyleElement.LabelText);

        /// <summary>
        /// Panel with the framed rows and, if set, the row outside the frame (same column widths). <paramref name="cells"/>
        /// gets the text cells row by row; the text is set by the caller.
        /// </summary>
        public static System.Windows.Controls.StackPanel Build(LabelLayout layout, Theme theme, out System.Windows.Controls.TextBlock[] cells)
        {
            int outside = OutsideIndex(layout);
            bool frameShown = layout.HasFrame && !theme.IsHidden(StyleElement.LabelFrame);
            cells = new System.Windows.Controls.TextBlock[layout.Rows * layout.Columns];
            System.Windows.Controls.StackPanel root = new() { Orientation = System.Windows.Controls.Orientation.Vertical };
            System.Windows.Controls.Grid.SetIsSharedSizeScope(root, true);
            System.Windows.Controls.Grid framed = new();
            System.Windows.Controls.Grid outer = new();
            foreach (System.Windows.Controls.Grid g in new[] { framed, outer })
            {
                for (int c = 0; c < layout.Columns; c++)
                {
                    g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto, SharedSizeGroup = "label" + c });
                }
            }
            // Inner lines of the frame between framed rows (not next to the row outside the frame).
            HashSet<int> rowLines = frameShown
                ? layout.RowLines.Where(r => r >= 0 && r + 1 < layout.Rows && r != outside && r + 1 != outside).ToHashSet()
                : [];
            int framedRow = 0;
            for (int r = 0; r < layout.Rows; r++)
            {
                System.Windows.Controls.Grid target = r == outside ? outer : framed;
                int gridRow = r == outside ? 0 : framedRow++;
                target.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                for (int c = 0; c < layout.Columns; c++)
                {
                    System.Windows.Controls.TextBlock cell = new()
                    {
                        FontSize = 12,
                        Foreground = TextBrush(layout, r, theme),
                        Margin = new System.Windows.Thickness(0, rowLines.Contains(r - 1) ? RowLineGap : 0, c < layout.Columns - 1 ? ColumnGap : 0, rowLines.Contains(r) ? RowLineGap : 0),
                        Visibility = layout.Get(r, c) == LabelField.None ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible
                    };
                    System.Windows.Controls.Grid.SetRow(cell, gridRow);
                    System.Windows.Controls.Grid.SetColumn(cell, c);
                    target.Children.Add(cell);
                    cells[r * layout.Columns + c] = cell;
                }
            }
            System.Windows.Media.Pen? pen = null;
            if (frameShown)
            {
                pen = new System.Windows.Media.Pen(theme.Brush(StyleElement.LabelFrame), Math.Max(1, theme.Width(StyleElement.LabelFrame)))
                {
                    DashStyle = Theme.DashArray(theme.Dash(StyleElement.LabelFrame)) is { } dash ? new System.Windows.Media.DashStyle(dash, 0) : System.Windows.Media.DashStyles.Solid
                };
            }
            LabelFrameBox box = new()
            {
                Child = framed,
                Pen = pen,
                Top = frameShown && layout.FrameTop,
                Right = frameShown && layout.FrameRight,
                Bottom = frameShown && layout.FrameBottom,
                Left = frameShown && layout.FrameLeft
            };
            framed.Margin = new System.Windows.Thickness(box.Left ? FramePadding : 0, box.Top ? FramePadding : 0, box.Right ? FramePadding : 0, box.Bottom ? FramePadding : 0);
            if (pen != null)
            {
                // Inner lines, from edge to edge (up to the sides of the frame when they are drawn).
                int framedRows = framed.RowDefinitions.Count;
                foreach (int r in rowLines)
                {
                    int gridRow = outside >= 0 && r > outside ? r - 1 : r;
                    LabelGridLine line = new() { Pen = pen, ExtendStart = box.Left ? FramePadding : 0, ExtendEnd = box.Right ? FramePadding : 0 };
                    System.Windows.Controls.Grid.SetRow(line, gridRow);
                    System.Windows.Controls.Grid.SetColumnSpan(line, Math.Max(1, layout.Columns));
                    framed.Children.Add(line);
                }
                foreach (int c in layout.ColumnLines.Where(c => c >= 0 && c + 1 < layout.Columns).Distinct())
                {
                    LabelGridLine line = new() { Pen = pen, Vertical = true, ColumnGap = ColumnGap, ExtendStart = box.Top ? FramePadding : 0, ExtendEnd = box.Bottom ? FramePadding : 0 };
                    System.Windows.Controls.Grid.SetColumn(line, c);
                    System.Windows.Controls.Grid.SetRowSpan(line, Math.Max(1, framedRows));
                    framed.Children.Add(line);
                }
            }
            if (outside == 0)
            {
                outer.Margin = new System.Windows.Thickness(box.Left ? FramePadding : 0, 0, 0, 3);
                root.Children.Add(outer);
                root.Children.Add(box);
            }
            else if (outside > 0)
            {
                outer.Margin = new System.Windows.Thickness(box.Left ? FramePadding : 0, 3, 0, 0);
                root.Children.Add(box);
                root.Children.Add(outer);
            }
            else
            {
                root.Children.Add(box);
            }
            return root;
        }
    }

    /// <summary>
    /// Layout of a track label: a grid of rows and columns, each cell showing one field (or nothing).
    /// </summary>
    internal class LabelLayout
    {
        public const int MaxRows = 6;
        public const int MaxColumns = 3;

        public int Rows { get; set; } = 1;
        public int Columns { get; set; } = 1;
        /// <summary>Cells row by row (Rows × Columns).</summary>
        public List<LabelField> Cells { get; set; } = [];

        /// <summary>Sides of the frame drawn around the label (none by default); colour and line: Display style → Label frame.</summary>
        public bool FrameTop { get; set; }
        public bool FrameRight { get; set; }
        public bool FrameBottom { get; set; }
        public bool FrameLeft { get; set; }
        /// <summary>A row kept outside the frame, above it (first row) or below it (last row), with its own text colour.</summary>
        public LabelOutsideRow OutsideRow { get; set; }
        /// <summary>Inner lines of the frame: below each of these rows (0 = between the first and the second row).</summary>
        public List<int> RowLines { get; set; } = [];
        /// <summary>Inner lines of the frame: after each of these columns (0 = between the first and the second column).</summary>
        public List<int> ColumnLines { get; set; } = [];

        public bool HasFrame => FrameTop || FrameRight || FrameBottom || FrameLeft || RowLines.Count > 0 || ColumnLines.Count > 0;

        /// <summary>Turns an inner line on or off.</summary>
        public static void Toggle(List<int> lines, int index, bool on)
        {
            lines.Remove(index);
            if (on) lines.Add(index);
            lines.Sort();
        }

        /// <summary>Keeps only the inner lines inside the grid.</summary>
        private void TrimLines()
        {
            RowLines = (RowLines ?? []).Where(r => r >= 0 && r < Rows - 1).Distinct().OrderBy(r => r).ToList();
            ColumnLines = (ColumnLines ?? []).Where(c => c >= 0 && c < Columns - 1).Distinct().OrderBy(c => c).ToList();
        }

        public LabelField Get(int row, int column) => Cells[row * Columns + column];

        public void Set(int row, int column, LabelField field) => Cells[row * Columns + column] = field;

        /// <summary>
        /// True when no cell shows anything: only the track symbol is drawn.
        /// </summary>
        public bool IsEmpty => Cells.All(c => c == LabelField.None);

        /// <summary>
        /// Changes the number of rows and columns, keeping the fields that still fit.
        /// </summary>
        public void Resize(int rows, int columns)
        {
            rows = Math.Clamp(rows, 1, MaxRows);
            columns = Math.Clamp(columns, 1, MaxColumns);
            List<LabelField> cells = [];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    cells.Add(r < Rows && c < Columns && r * Columns + c < Cells.Count ? Get(r, c) : LabelField.None);
                }
            }
            Rows = rows;
            Columns = columns;
            Cells = cells;
            TrimLines();
        }

        /// <summary>
        /// Repairs a layout read from a file (wrong sizes, missing cells).
        /// </summary>
        public void Normalize()
        {
            Cells ??= [];
            int rows = Math.Clamp(Rows, 1, MaxRows);
            int columns = Math.Clamp(Columns, 1, MaxColumns);
            List<LabelField> cells = [];
            for (int i = 0; i < rows * columns; i++)
            {
                cells.Add(i < Cells.Count && Enum.IsDefined(Cells[i]) ? Cells[i] : LabelField.None);
            }
            Rows = rows;
            Columns = columns;
            Cells = cells;
            if (!Enum.IsDefined(OutsideRow)) OutsideRow = LabelOutsideRow.None;
            TrimLines();
        }

        public static LabelLayout Create(int rows, int columns, params LabelField[] cells)
        {
            LabelLayout layout = new() { Rows = rows, Columns = columns, Cells = [.. cells] };
            layout.Normalize();
            return layout;
        }

        /// <summary>
        /// Default elevation label: deviation from the glide path and callsign, distance, altitude, speed, vertical speed.
        /// </summary>
        public static LabelLayout DefaultElevation() => Create(5, 2,
            LabelField.GlidePathDeviation, LabelField.Callsign,
            LabelField.DistanceFromTouchdown, LabelField.None,
            LabelField.Altitude, LabelField.None,
            LabelField.GroundSpeed, LabelField.None,
            LabelField.VerticalSpeed, LabelField.None);

        /// <summary>
        /// Default azimuth label: deviation from the centreline and callsign, distance, speed.
        /// </summary>
        public static LabelLayout DefaultAzimuth() => Create(3, 2,
            LabelField.CenterlineDeviation, LabelField.Callsign,
            LabelField.DistanceFromTouchdown, LabelField.None,
            LabelField.GroundSpeed, LabelField.None);
    }

    /// <summary>
    /// Text of each label field.
    /// </summary>
    internal static class LabelFormatter
    {
        private const double MetresPerFoot = 0.3048;

        public static string DisplayName(LabelField field) => field switch
        {
            LabelField.None => "(empty)",
            LabelField.Callsign => "Callsign",
            LabelField.DistanceFromTouchdown => "Distance from touchdown",
            LabelField.Altitude => "Altitude / height",
            LabelField.GroundSpeed => "Ground speed",
            LabelField.VerticalSpeed => "Vertical speed",
            LabelField.GlidePathDeviation => "Deviation from glide path",
            LabelField.CenterlineDeviation => "Deviation from centreline",
            LabelField.SsrCode => "SSR code (A1234)",
            LabelField.TrackId => "Track ID (fictitious)",
            _ => field.ToString()
        };

        private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Length(double feet, bool metres) =>
            metres ? $"{Number(feet * MetresPerFoot, "0")} m" : $"{Number(feet, "0")} ft";

        /// <summary>Compact length (PAR-E): no space, unit in capitals, e.g. 23M or 75FT.</summary>
        private static string CompactLength(double feet, bool metres) =>
            metres ? $"{Number(feet * MetresPerFoot, "0")}M" : $"{Number(feet, "0")}FT";

        /// <summary>
        /// Text of a field for an aircraft. Heights, deviations and vertical speed in feet (ft, ft/min) or metres (m, m/s).
        /// </summary>
        public static string Format(LabelField field, Aircraft aircraft, Runway runway, ViewOptions options) =>
            options.NumbersOnlyLabels ? FormatNumbers(field, aircraft, runway, options)
            : options.CompactLabels ? FormatCompact(field, aircraft, runway, options) : FormatNormal(field, aircraft, runway, options);

        /// <summary>
        /// Numbers only, as on the ARTMACS display: 12.8 (distance), 414 (ground speed), 1250 (altitude or height),
        /// +53 / -20 (above / below the glide path), +75 / -40 (right / left of the centreline), -700 (vertical speed),
        /// in the units of the profile; callsign, SSR code and track ID as usual.
        /// </summary>
        private static string FormatNumbers(LabelField field, Aircraft aircraft, Runway runway, ViewOptions options)
        {
            bool metres = options.ScaleInMetres;
            string Signed(double feet) => Number(metres ? feet * MetresPerFoot : feet, "+0;-0;0");
            switch (field)
            {
                case LabelField.DistanceFromTouchdown:
                    {
                        double nm = aircraft.DistanceFromTouchdown(runway);
                        return Number(options.DistanceUnit == DistanceUnit.Kilometres ? nm * DistanceUnits.KilometresPerNM : nm, "0.0");
                    }
                case LabelField.Altitude:
                    {
                        double feet = options.Qfe ? aircraft.Altitude - runway.Elevation : aircraft.Altitude;
                        return Number(metres ? feet * MetresPerFoot : feet, "0");
                    }
                case LabelField.GroundSpeed:
                    return Number(aircraft.Speed, "0");
                case LabelField.VerticalSpeed:
                    if (aircraft.VerticalSpeedFpm is not double fpm) return "";
                    return metres ? Number(fpm * MetresPerFoot / 60, "+0.0;-0.0;0.0") : Number(Math.Round(fpm / 10) * 10, "+0;-0;0");
                case LabelField.GlidePathDeviation:
                    return Signed(aircraft.Altitude - runway.Elevation - runway.GlidePathHeight(aircraft.DistanceFromTouchdown(runway)));
                case LabelField.CenterlineDeviation:
                    return Signed(aircraft.LateralOffset(runway) * Runway.FeetPerNM);
                default:
                    return FormatNormal(field, aircraft, runway, options);
            }
        }

        private static string FormatNormal(LabelField field, Aircraft aircraft, Runway runway, ViewOptions options)
        {
            switch (field)
            {
                case LabelField.Callsign:
                    return aircraft.Callsign;
                case LabelField.DistanceFromTouchdown:
                    return options.DistanceUnit == DistanceUnit.Kilometres
                        ? $"{Number(aircraft.DistanceFromTouchdown(runway) * DistanceUnits.KilometresPerNM, "0.00")} km"
                        : $"{Number(aircraft.DistanceFromTouchdown(runway), "0.0")} NM";
                case LabelField.Altitude:
                    return options.Qfe
                        ? $"H {Length(aircraft.Altitude - runway.Elevation, options.ScaleInMetres)}"
                        : $"A {Length(aircraft.Altitude, options.ScaleInMetres)}";
                case LabelField.GroundSpeed:
                    return $"{Number(aircraft.Speed, "0")} Kts";
                case LabelField.VerticalSpeed:
                    if (aircraft.VerticalSpeedFpm is not double fpm) return "";
                    return options.ScaleInMetres
                        ? $"{Number(fpm * MetresPerFoot / 60, "+0.0;-0.0;0.0")} m/s"
                        : $"{Number(Math.Round(fpm / 10) * 10, "+0;-0;0")} ft/min";
                case LabelField.GlidePathDeviation:
                    {
                        double deviation = aircraft.Altitude - runway.Elevation - runway.GlidePathHeight(aircraft.DistanceFromTouchdown(runway));
                        return $"{(deviation >= 0 ? "U" : "D")} {Length(Math.Abs(deviation), options.ScaleInMetres)}";
                    }
                case LabelField.CenterlineDeviation:
                    {
                        double feet = aircraft.LateralOffset(runway) * Runway.FeetPerNM;
                        return $"{(feet >= 0 ? "R" : "L")} {Length(Math.Abs(feet), options.ScaleInMetres)}";
                    }
                case LabelField.SsrCode:
                    return aircraft.Squawk is string code ? "A" + code : "";
                case LabelField.TrackId:
                    return options.Identities.Get(aircraft.Callsign);
                default:
                    return "";
            }
        }

        /// <summary>
        /// Compact text, as on the PAR-E: D:4.45KM, A3M / B2M (above / below the glide path), R23M / L5M, ID:56,
        /// A600M (altitude) or H600M (height); the other fields as usual.
        /// </summary>
        private static string FormatCompact(LabelField field, Aircraft aircraft, Runway runway, ViewOptions options)
        {
            bool metres = options.ScaleInMetres;
            switch (field)
            {
                case LabelField.DistanceFromTouchdown:
                    {
                        double nm = aircraft.DistanceFromTouchdown(runway);
                        return options.DistanceUnit == DistanceUnit.Kilometres
                            ? $"D:{Number(nm * DistanceUnits.KilometresPerNM, "0.00")}KM"
                            : $"D:{Number(nm, "0.00")}NM";
                    }
                case LabelField.Altitude:
                    return options.Qfe
                        ? $"H{CompactLength(aircraft.Altitude - runway.Elevation, metres)}"
                        : $"A{CompactLength(aircraft.Altitude, metres)}";
                case LabelField.GlidePathDeviation:
                    {
                        double deviation = aircraft.Altitude - runway.Elevation - runway.GlidePathHeight(aircraft.DistanceFromTouchdown(runway));
                        return $"{(deviation >= 0 ? "A" : "B")}{CompactLength(Math.Abs(deviation), metres)}";
                    }
                case LabelField.CenterlineDeviation:
                    {
                        double feet = aircraft.LateralOffset(runway) * Runway.FeetPerNM;
                        return $"{(feet >= 0 ? "R" : "L")}{CompactLength(Math.Abs(feet), metres)}";
                    }
                case LabelField.TrackId:
                    {
                        string id = options.Identities.Get(aircraft.Callsign);
                        return id.Length == 0 ? "" : "ID:" + id;
                    }
                default:
                    // The other fields are the same as the normal text.
                    return FormatNormal(field, aircraft, runway, options);
            }
        }
    }

    /// <summary>
    /// Fictitious identities of the tracks, for radars that receive neither the callsign nor the SSR code. A track
    /// gets a random two-digit ID (01 to 99) when it appears in the scan; the ID stays while the track is seen and
    /// is never given again in the session (only when all 99 have been used are the free ones given again). A track
    /// that is out of the beam for <see cref="DropAfter"/> (10 s, or the coasting time + 2 s if longer) loses its ID: when it comes back it gets a new one. The
    /// user can assign an ID of his own to a callsign, kept for the session; it wins over the random one.
    /// Session only, nothing is saved.
    /// </summary>
    internal sealed class TrackIdentities
    {
        public const int MaxLength = 7;
        /// <summary>Time out of the scan after which a track loses its random ID (longer than the coasting time).</summary>
        public TimeSpan DropAfter { get; set; } = TimeSpan.FromSeconds(10);
        private readonly Random random = new();
        private readonly Dictionary<string, (string Id, DateTime LastSeen)> given = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> used = [];
        private readonly Dictionary<string, string> assigned = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Random IDs given to the tracks (profile option); the assigned ones are shown anyway.</summary>
        public bool RandomEnabled { get; set; } = true;

        /// <summary>
        /// Gives an ID to the tracks seen now (inside the scan) and takes it away from those not seen for a while.
        /// </summary>
        public void Update(IEnumerable<string> seen, DateTime now)
        {
            foreach (string callsign in seen)
            {
                string id = given.TryGetValue(callsign, out var entry) ? entry.Id : NewId();
                given[callsign] = (id, now);
            }
            foreach (string callsign in given.Where(g => now - g.Value.LastSeen > DropAfter).Select(g => g.Key).ToList())
            {
                given.Remove(callsign);
            }
        }

        private string NewId()
        {
            List<string> free = Enumerable.Range(1, 99).Select(n => n.ToString("00")).Where(id => !used.Contains(id)).ToList();
            if (free.Count == 0)
            {
                // All used in this session: the ones not shown now, again.
                HashSet<string> shown = given.Values.Select(g => g.Id).ToHashSet();
                free = Enumerable.Range(1, 99).Select(n => n.ToString("00")).Where(id => !shown.Contains(id)).ToList();
                if (free.Count == 0) free = ["00"];
            }
            string chosen = free[random.Next(free.Count)];
            used.Add(chosen);
            return chosen;
        }

        /// <summary>ID shown for a callsign: the assigned one, else the random one (if enabled), else nothing.</summary>
        public string Get(string callsign)
        {
            if (assigned.TryGetValue(callsign, out string? own)) return own;
            return RandomEnabled && given.TryGetValue(callsign, out var entry) ? entry.Id : "";
        }

        public string? Assigned(string callsign) => assigned.TryGetValue(callsign, out string? own) ? own : null;

        /// <summary>Assigns an ID to a callsign (empty: back to the random one).</summary>
        public void Assign(string callsign, string? id)
        {
            id = (id ?? "").Trim().ToUpperInvariant();
            if (id.Length > MaxLength) id = id[..MaxLength];
            if (id.Length == 0) assigned.Remove(callsign);
            else assigned[callsign] = id;
        }
    }

    /// <summary>
    /// Vertical speed of each aircraft, from the change of altitude over the last seconds (least squares,
    /// so the steps of the altitude received from Aurora are smoothed). Unknown for the first seconds of a track.
    /// </summary>
    internal class VerticalSpeedEstimator
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);
        private const double MinSpanSeconds = 2;
        private readonly Dictionary<string, Queue<(DateTime Time, double Altitude)>> samples = [];

        public void Update(IReadOnlyList<Aircraft> aircrafts, DateTime now)
        {
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                present.Add(aircraft.Callsign);
                if (!samples.TryGetValue(aircraft.Callsign, out Queue<(DateTime Time, double Altitude)>? queue))
                {
                    queue = new();
                    samples[aircraft.Callsign] = queue;
                }
                queue.Enqueue((now, aircraft.Altitude));
                while (queue.Count > 0 && now - queue.Peek().Time > Window)
                {
                    queue.Dequeue();
                }
                aircraft.VerticalSpeedFpm = Estimate(queue);
            }
            foreach (string callsign in samples.Keys.Where(c => !present.Contains(c)).ToList())
            {
                samples.Remove(callsign);
            }
        }

        private static double? Estimate(Queue<(DateTime Time, double Altitude)> queue)
        {
            if (queue.Count < 2) return null;
            DateTime start = queue.Peek().Time;
            double n = 0, sumT = 0, sumA = 0, sumTT = 0, sumTA = 0, last = 0;
            foreach ((DateTime time, double altitude) in queue)
            {
                double t = (time - start).TotalSeconds;
                n++;
                sumT += t;
                sumA += altitude;
                sumTT += t * t;
                sumTA += t * altitude;
                last = t;
            }
            if (last < MinSpanSeconds) return null;
            double denominator = n * sumTT - sumT * sumT;
            if (denominator <= 0) return null;
            double feetPerSecond = (n * sumTA - sumT * sumA) / denominator;
            return feetPerSecond * 60;
        }
    }
}
