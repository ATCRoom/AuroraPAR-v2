using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>
    /// The maps of the surveillance radar for the airport in use: for each category (MVA, FIX, VRP, airports, navaids, CTR,
    /// ATZ, geographic boundaries) whether its lines / symbols and its labels are shown, and their colours; the choice is
    /// kept for each airport. Also the folder, a new read of it, and ATCRoom Map Creator to make the maps. Changes are
    /// applied at once.
    /// </summary>
    internal sealed class MapsWindow : Window
    {
        private readonly TrainerSettings settings;
        private readonly Func<(List<LoadedMap> Maps, List<string> Errors)> current;
        private readonly Func<(List<LoadedMap> Maps, List<string> Errors)> reload;
        private readonly Func<string> airport;
        private readonly Action changed;
        private readonly StackPanel list = new();
        private readonly TextBlock airportText = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        private readonly TextBlock errorsText = new() { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        /// <param name="current">The maps read (and the files that could not be read).</param>
        /// <param name="reload">Reads the Maps folder again (and gives the maps to the RDR).</param>
        /// <param name="airport">The airport (ICAO) in use.</param>
        /// <param name="changed">A category was turned on or off, or a colour changed: redraw and save.</param>
        public MapsWindow(TrainerSettings settings, Func<(List<LoadedMap> Maps, List<string> Errors)> current,
            Func<(List<LoadedMap> Maps, List<string> Errors)> reload, Func<string> airport, Action changed)
        {
            this.settings = settings;
            this.current = current;
            this.reload = reload;
            this.airport = airport;
            this.changed = changed;
            Title = "Maps of the surveillance radar (RDR)";
            Width = 660;
            Height = 600;
            MinWidth = 480;
            MinHeight = 320;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            DockPanel root = new() { Margin = new Thickness(12) };
            TextBlock intro = new()
            {
                Text = "One map file per airport in the Maps folder of the Trainer, made with ATCRoom Map Creator. Choose what to show for each " +
                       "category and its colour: the choice is kept for each airport, and changing airport loads its maps. The maps you make are for your own use.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);
            DockPanel.SetDock(airportText, Dock.Top);
            root.Children.Add(airportText);

            WrapPanel buttons = new() { Margin = new Thickness(0, 10, 0, 0) };
            Button reloadButton = new() { Content = "Reload", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            reloadButton.Click += (s, e) => ShowMaps(this.reload());
            Button folderButton = new() { Content = "Open folder", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            folderButton.Click += (s, e) => OpenFolder();
            Button creatorButton = new()
            {
                Content = "Open ATCRoom Map Creator",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = File.Exists(MapLibrary.MapCreatorPath),
                ToolTip = File.Exists(MapLibrary.MapCreatorPath) ? "Make the map of an airport from the files of IVAO, EuroScope or an AIP."
                    : "ATCRoomMapCreator.exe is not beside AuroraPARTrainer.exe."
            };
            ToolTipService.SetShowOnDisabled(creatorButton, true);
            creatorButton.Click += (s, e) => OpenMapCreator();
            Button closeButton = new() { Content = "Close", Padding = new Thickness(16, 3, 16, 3), IsCancel = true };
            closeButton.Click += (s, e) => Close();
            buttons.Children.Add(reloadButton);
            buttons.Children.Add(folderButton);
            buttons.Children.Add(creatorButton);
            buttons.Children.Add(closeButton);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            DockPanel.SetDock(errorsText, Dock.Bottom);
            root.Children.Add(errorsText);

            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            ShowMaps(current());
        }

        /// <summary>Shows the maps again (the airport in use changed).</summary>
        public void Refresh() => ShowMaps(current());

        /// <summary>One row per category of the maps of the airport, then the maps of the old kind.</summary>
        private void ShowMaps((List<LoadedMap> Maps, List<string> Errors) maps)
        {
            list.Children.Clear();
            string icao = airport();
            airportText.Text = icao.Length > 0 ? $"Airport {icao}" : "No airport";
            List<LoadedMap> mine = maps.Maps.Where(m => m.Map.Airport.Length > 0 && string.Equals(m.Map.Airport, icao, StringComparison.OrdinalIgnoreCase)).ToList();
            HashSet<string> present = mine.SelectMany(m => m.Map.UsedCategories()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (present.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = icao.Length == 0 ? "No airport in use." : $"No maps of {icao} yet. Make them with ATCRoom Map Creator (one file per airport, for example {icao}.json), then Reload.",
                    Foreground = Brushes.DimGray,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            foreach (MapCategoryInfo category in MapCategory.All.Where(c => present.Contains(c.Id)))
            {
                list.Children.Add(CategoryRow(icao, category, mine));
            }
            List<LoadedMap> legacy = maps.Maps.Where(m => m.Map.Airport.Length == 0).ToList();
            if (legacy.Count > 0)
            {
                list.Children.Add(new TextBlock { Text = "Maps of the old kind (shown for every airport)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 8) });
                foreach (LoadedMap map in legacy) list.Children.Add(LegacyRow(map));
            }
            errorsText.Text = maps.Errors.Count == 0 ? "" : "Not read (not a map file):\n" + string.Join("\n", maps.Errors);
            errorsText.Visibility = maps.Errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string SymbolName(MapSymbol symbol) => symbol switch
        {
            MapSymbol.Point => "Point (triangle)",
            MapSymbol.Airport => "Airport (circle)",
            MapSymbol.Vrp => "VRP (square)",
            MapSymbol.Vor => "VOR (hexagon)",
            MapSymbol.Vortac => "VORTAC",
            MapSymbol.Tacan => "TACAN (diamond)",
            MapSymbol.Ndb => "NDB (circle, dot)",
            _ => symbol.ToString()
        };

        /// <summary>A category: lines / symbols shown and their colour, labels shown and their colour.</summary>
        private StackPanel CategoryRow(string icao, MapCategoryInfo category, List<LoadedMap> mine)
        {
            CategoryDisplaySettings display = settings.CategoryDisplay(icao, category.Id);
            StackPanel row = new() { Margin = new Thickness(0, 0, 0, 12) };
            string source = mine.Select(m => m.Map.Sources.TryGetValue(category.Id, out string? text) ? text : "").FirstOrDefault(t => t.Length > 0) ?? "";
            TextBlock title = new() { Text = category.Name, FontWeight = FontWeights.SemiBold };
            if (source.Length > 0) title.ToolTip = "Source: " + source;
            row.Children.Add(title);

            StackPanel shapesRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 4, 0, 0) };
            CheckBox shapes = new() { Content = "Show", IsChecked = display.Shapes ?? category.ShapesOn, Width = 80, VerticalAlignment = VerticalAlignment.Center };
            shapes.Click += (s, e) =>
            {
                display.Shapes = shapes.IsChecked == true;
                changed();
            };
            shapesRow.Children.Add(shapes);
            shapesRow.Children.Add(ColorPicker.Create(display.ShapeColour ?? category.DefaultColour, ColorPicker.StandardColors, hex =>
            {
                display.ShapeColour = hex;
                changed();
                // Labels without a colour of their own follow the lines: show it.
                if (display.LabelColour == null && category.HasLabels) Dispatcher.BeginInvoke(new Action(() => ShowMaps(current())));
            }));
            // The points of the category can be drawn with another symbol (the map file is not changed).
            if (mine.Any(m => m.Map.OfCategory(category.Id).Points.Count > 0))
            {
                ComboBox symbols = new() { MinWidth = 150, Margin = new Thickness(12, 0, 0, 0), ToolTip = "The symbol of the points. \"As in the map\": the one saved in the map file. More symbols: Symbols.json in the Maps folder (then Reload)." };
                symbols.Items.Add("Symbol: as in the map");
                foreach (MapSymbol symbol in Enum.GetValues<MapSymbol>()) symbols.Items.Add(SymbolName(symbol));
                int builtIn = Enum.GetValues<MapSymbol>().Length;
                foreach (CustomSymbol custom in CustomSymbols.All) symbols.Items.Add(custom.Name);
                int customIndex = CustomSymbols.All.FindIndex(c => c.Name.Equals(display.Symbol, StringComparison.OrdinalIgnoreCase));
                symbols.SelectedIndex = Enum.TryParse(display.Symbol, ignoreCase: true, out MapSymbol saved) && Enum.IsDefined(saved) ? (int)saved + 1
                    : customIndex >= 0 ? builtIn + 1 + customIndex : 0;
                symbols.SelectionChanged += (s, e) =>
                {
                    if (symbols.SelectedIndex < 0) return;
                    int index = symbols.SelectedIndex;
                    display.Symbol = index == 0 ? null : index <= builtIn ? ((MapSymbol)(index - 1)).ToString() : CustomSymbols.All[index - builtIn - 1].Name;
                    changed();
                };
                shapesRow.Children.Add(symbols);
            }
            row.Children.Add(shapesRow);

            if (category.HasLabels)
            {
                StackPanel labelsRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 4, 0, 0) };
                CheckBox labels = new() { Content = "Labels", IsChecked = display.Labels ?? category.LabelsOn, Width = 80, VerticalAlignment = VerticalAlignment.Center };
                labels.Click += (s, e) =>
                {
                    display.Labels = labels.IsChecked == true;
                    changed();
                };
                labelsRow.Children.Add(labels);
                labelsRow.Children.Add(ColorPicker.Create(display.LabelColour ?? display.ShapeColour ?? category.DefaultColour, ColorPicker.StandardColors, hex =>
                {
                    display.LabelColour = hex;
                    changed();
                }));
                row.Children.Add(labelsRow);
            }
            return row;
        }

        /// <summary>A map of the old kind: shown or not, colour of the lines, colour of the labels.</summary>
        private StackPanel LegacyRow(LoadedMap map)
        {
            MapDisplaySettings display = settings.MapDisplay(map.FileName);
            StackPanel row = new() { Margin = new Thickness(0, 0, 0, 12) };
            CheckBox shown = new()
            {
                Content = map.Map.Name,
                IsChecked = display.Visible,
                FontWeight = FontWeights.SemiBold,
                ToolTip = $"{map.FileName}: {map.Map.Summary}{(map.Map.Source.Length > 0 ? "\nSource: " + map.Map.Source : "")}"
            };
            shown.Click += (s, e) =>
            {
                display.Visible = shown.IsChecked == true;
                changed();
            };
            row.Children.Add(shown);
            StackPanel colours = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 4, 0, 0) };
            colours.Children.Add(new TextBlock { Text = "Lines", Width = 44, VerticalAlignment = VerticalAlignment.Center });
            colours.Children.Add(ColorPicker.Create(display.LineColour ?? map.Map.LineColour, ColorPicker.StandardColors, hex =>
            {
                display.LineColour = hex;
                changed();
            }));
            row.Children.Add(colours);
            StackPanel labelColours = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 4, 0, 0) };
            labelColours.Children.Add(new TextBlock { Text = "Labels", Width = 44, VerticalAlignment = VerticalAlignment.Center });
            labelColours.Children.Add(ColorPicker.Create(display.LabelColour ?? map.Map.LabelColour, ColorPicker.StandardColors, hex =>
            {
                display.LabelColour = hex;
                changed();
            }));
            row.Children.Add(labelColours);
            return row;
        }

        private static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(MapLibrary.Folder);
                Process.Start(new ProcessStartInfo(MapLibrary.Folder) { UseShellExecute = true });
            }
            catch (Exception) { }
        }

        private static void OpenMapCreator()
        {
            try
            {
                Process.Start(new ProcessStartInfo(MapLibrary.MapCreatorPath) { UseShellExecute = true });
            }
            catch (Exception) { }
        }
    }
}
