using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Editor of the elevation and azimuth label layouts of the active profile.
    /// </summary>
    public partial class LabelEditorWindow : Window
    {
        private readonly AppSettings settings;
        private readonly Action commit;

        /// <summary>
        /// Example values shown in the preview.
        /// </summary>
        private static string Sample(LabelField field) => field switch
        {
            LabelField.Callsign => "AZA123",
            LabelField.DistanceFromTouchdown => "6.2 NM",
            LabelField.Altitude => "A 2060 ft",
            LabelField.GroundSpeed => "140 Kts",
            LabelField.VerticalSpeed => "-750 ft/min",
            LabelField.GlidePathDeviation => "U 85 ft",
            LabelField.CenterlineDeviation => "R 35 ft",
            LabelField.SsrCode => "A2201",
            LabelField.TrackId => "42",
            _ => ""
        };

        internal LabelEditorWindow(AppSettings settings, Action commit)
        {
            InitializeComponent();
            this.settings = settings;
            this.commit = commit;
            CloseButton.Click += (s, e) => Close();
            RandomIdCheck.IsChecked = Active.RandomTrackIds;
            RandomIdCheck.Checked += (s, e) => { Active.RandomTrackIds = true; commit(); };
            RandomIdCheck.Unchecked += (s, e) => { Active.RandomTrackIds = false; commit(); };
            BuildSection(ElevationPanel, p => p.ElevationLabel, (p, l) => p.ElevationLabel = l, LabelLayout.DefaultElevation);
            BuildSection(AzimuthPanel, p => p.AzimuthLabel, (p, l) => p.AzimuthLabel = l, LabelLayout.DefaultAzimuth);
        }

        private Profile Active => settings.Active;

        /// <summary>
        /// Builds (or rebuilds after a change) the controls of one label: size, cells and preview.
        /// </summary>
        private void BuildSection(StackPanel panel, Func<Profile, LabelLayout> get, Action<Profile, LabelLayout> set, Func<LabelLayout> createDefault)
        {
            panel.Children.Clear();
            LabelLayout layout = get(Active);

            void Changed()
            {
                commit();
                BuildSection(panel, get, set, createDefault);
            }

            // Size and default.
            StackPanel sizeRow = new() { Orientation = Orientation.Horizontal };
            ComboBox rows = new() { Width = 50, ItemsSource = Enumerable.Range(1, LabelLayout.MaxRows).ToList(), SelectedItem = layout.Rows };
            ComboBox columns = new() { Width = 50, ItemsSource = Enumerable.Range(1, LabelLayout.MaxColumns).ToList(), SelectedItem = layout.Columns };
            Button reset = new() { Content = "Default", Width = 80, Margin = new Thickness(20, 0, 0, 0) };
            sizeRow.Children.Add(new TextBlock { Text = "Rows:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            sizeRow.Children.Add(rows);
            sizeRow.Children.Add(new TextBlock { Text = "Columns:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) });
            sizeRow.Children.Add(columns);
            sizeRow.Children.Add(reset);
            panel.Children.Add(sizeRow);
            rows.SelectionChanged += (s, e) =>
            {
                if (rows.SelectedItem is int r && r != layout.Rows)
                {
                    layout.Resize(r, layout.Columns);
                    Changed();
                }
            };
            columns.SelectionChanged += (s, e) =>
            {
                if (columns.SelectedItem is int c && c != layout.Columns)
                {
                    layout.Resize(layout.Rows, c);
                    Changed();
                }
            };
            reset.Click += (s, e) =>
            {
                set(Active, createDefault());
                Changed();
            };

            // Cells.
            List<string> fieldNames = Enum.GetValues<LabelField>().Select(LabelFormatter.DisplayName).ToList();
            Grid cells = new() { Margin = new Thickness(0, 8, 0, 0) };
            for (int c = 0; c < layout.Columns; c++) cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            for (int r = 0; r < layout.Rows; r++)
            {
                cells.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < layout.Columns; c++)
                {
                    int row = r, column = c;
                    ComboBox cell = new()
                    {
                        ItemsSource = fieldNames,
                        SelectedIndex = (int)layout.Get(r, c),
                        Margin = new Thickness(0, 2, 6, 2)
                    };
                    cell.SelectionChanged += (s, e) =>
                    {
                        LabelField field = (LabelField)cell.SelectedIndex;
                        if (cell.SelectedIndex >= 0 && field != layout.Get(row, column))
                        {
                            layout.Set(row, column, field);
                            Changed();
                        }
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    cells.Children.Add(cell);
                }
            }
            panel.Children.Add(cells);

            // Frame: a map of the label grid where every line (the four sides and the inner lines between rows
            // and between columns, from edge to edge) is turned on or off with a click; and the row kept outside it.
            panel.Children.Add(new TextBlock
            {
                Text = "Frame: click a line to draw it or not (orange = drawn). Colour, width and line: Settings → Colours and lines → Label frame.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 2)
            });
            panel.Children.Add(BuildFrameMap(layout, Changed));
            StackPanel frameRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            frameRow.Children.Add(new TextBlock { Text = "Outside the frame:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "A row drawn outside the frame, above it (first row) or below it (last row), with its own text colour (Settings → Colours and lines → Label text outside)." });
            ComboBox outside = new() { Width = 100, ItemsSource = new[] { "None", "First row", "Last row" }, SelectedIndex = (int)layout.OutsideRow, IsEnabled = layout.Rows > 1 };
            outside.SelectionChanged += (s, e) =>
            {
                if (outside.SelectedIndex >= 0 && (LabelOutsideRow)outside.SelectedIndex != layout.OutsideRow)
                {
                    layout.OutsideRow = (LabelOutsideRow)outside.SelectedIndex;
                    Changed();
                }
            };
            frameRow.Children.Add(outside);
            panel.Children.Add(frameRow);

            // Preview, as on the radar screen (with the colours of the Display style).
            Theme theme = Theme.Modern(Active.Style);
            StackPanel preview = LabelVisual.Build(layout, theme, out TextBlock[] previewCells);
            for (int r = 0; r < layout.Rows; r++)
            {
                for (int c = 0; c < layout.Columns; c++) previewCells[r * layout.Columns + c].Text = Sample(layout.Get(r, c));
            }
            Border previewBox = new()
            {
                Background = Brushes.Black,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 160,
                MinHeight = 30,
                Child = layout.IsEmpty
                    ? new TextBlock { Text = "(only the track symbol)", Foreground = Brushes.Gray, FontSize = 12 }
                    : preview
            };
            panel.Children.Add(new TextBlock { Text = "Preview:", Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(previewBox);
        }

        private static readonly Brush LineOn = new SolidColorBrush(Color.FromRgb(0xC8, 0x78, 0x32));
        private static readonly Brush LineOff = new SolidColorBrush(Color.FromRgb(0xC9, 0xCE, 0xD6));

        /// <summary>
        /// Clickable map of the frame: the cells of the framed rows (the row outside the frame is not in it) and,
        /// around and between them, the lines that can be drawn.
        /// </summary>
        private static Grid BuildFrameMap(LabelLayout layout, Action changed)
        {
            const double Thick = 6, CellWidth = 110, CellHeight = 22;
            int outside = LabelVisual.OutsideIndex(layout);
            List<int> framedRows = Enumerable.Range(0, layout.Rows).Where(r => r != outside).ToList();
            Grid map = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
            for (int c = 0; c < layout.Columns; c++)
            {
                map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Thick) });
                map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
            }
            map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Thick) });
            for (int i = 0; i < framedRows.Count; i++)
            {
                map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Thick) });
                map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });
            }
            map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Thick) });
            int mapColumns = map.ColumnDefinitions.Count, mapRows = map.RowDefinitions.Count;
            // Cells.
            for (int i = 0; i < framedRows.Count; i++)
            {
                for (int c = 0; c < layout.Columns; c++)
                {
                    LabelField field = layout.Get(framedRows[i], c);
                    Border cell = new()
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)),
                        Margin = new Thickness(2),
                        Child = new TextBlock
                        {
                            Text = field == LabelField.None ? "" : LabelFormatter.DisplayName(field),
                            FontSize = 10,
                            Foreground = Brushes.DimGray,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    Grid.SetRow(cell, 2 * i + 1);
                    Grid.SetColumn(cell, 2 * c + 1);
                    map.Children.Add(cell);
                }
            }
            void AddLine(int row, int column, int rowSpan, int columnSpan, string tip, Func<bool> get, Action<bool> set)
            {
                Border line = new() { Background = get() ? LineOn : LineOff, Cursor = Cursors.Hand, ToolTip = tip, Margin = new Thickness(1) };
                line.MouseLeftButtonUp += (s, e) =>
                {
                    set(!get());
                    changed();
                };
                Grid.SetRow(line, row);
                Grid.SetColumn(line, column);
                Grid.SetRowSpan(line, rowSpan);
                Grid.SetColumnSpan(line, columnSpan);
                map.Children.Add(line);
            }
            // Sides.
            AddLine(0, 0, 1, mapColumns, "Top side", () => layout.FrameTop, v => layout.FrameTop = v);
            AddLine(mapRows - 1, 0, 1, mapColumns, "Bottom side", () => layout.FrameBottom, v => layout.FrameBottom = v);
            AddLine(0, 0, mapRows, 1, "Left side", () => layout.FrameLeft, v => layout.FrameLeft = v);
            AddLine(0, mapColumns - 1, mapRows, 1, "Right side", () => layout.FrameRight, v => layout.FrameRight = v);
            // Inner lines between framed rows (label row index of the row above the line).
            for (int i = 0; i + 1 < framedRows.Count; i++)
            {
                int row = framedRows[i];
                AddLine(2 * i + 2, 1, 1, mapColumns - 2, "Line between these rows",
                    () => layout.RowLines.Contains(row), v => LabelLayout.Toggle(layout.RowLines, row, v));
            }
            // Inner lines between columns.
            for (int c = 0; c + 1 < layout.Columns; c++)
            {
                int column = c;
                AddLine(1, 2 * c + 2, mapRows - 2, 1, "Line between these columns",
                    () => layout.ColumnLines.Contains(column), v => LabelLayout.Toggle(layout.ColumnLines, column, v));
            }
            return map;
        }
    }
}
