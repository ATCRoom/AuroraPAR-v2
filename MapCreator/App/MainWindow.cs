using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATCRoom.Maps;
using Microsoft.Win32;
using IoPath = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ATCRoom.MapCreator
{
    /// <summary>
    /// The window of ATCRoom Map Creator: drop many files (or a folder) at once, see them all together on one picture,
    /// choose for each part the category it goes into, cut a circle around the airport and save the map of that airport
    /// into the maps of AuroraPAR v2 Trainer (one file per airport, the categories of other imports are kept).
    /// </summary>
    internal sealed class MainWindow : Window
    {
        private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".geo", ".fix", ".vor", ".ndb", ".ap", ".apt", ".vfi", ".str", ".mva", ".lartcc", ".hartcc", ".artcc", ".ese", ".pdf"
        };

        private readonly Dictionary<string, FileImport> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly StackPanel entriesPanel = new();
        private readonly TextBlock notesText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DimGray, FontSize = 12 };
        private readonly TextBox airportBox = new() { Width = 80, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox cutBox = new() { Content = "Keep only what is inside a circle", Margin = new Thickness(0, 6, 0, 0) };
        private readonly ComboBox centreBox = new() { Width = 190 };
        private readonly TextBox radiusBox = new() { Text = "60", Width = 52, Padding = new Thickness(2), Margin = new Thickness(0, 0, 4, 0) };
        private readonly TextBlock cutText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.DimGray, FontSize = 12 };
        private readonly TextBox sourceBox = new() { Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(2) };
        private readonly Button saveButton = new() { Content = "Save into the maps of the Trainer", Padding = new Thickness(12, 4, 12, 4), IsEnabled = false };
        private readonly Button saveAsButton = new() { Content = "Save as...", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        private readonly Button clearButton = new() { Content = "Clear all", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly MapPreview preview = new();

        private double? cutLatitude, cutLongitude;
        private readonly Dictionary<string, (double Latitude, double Longitude)> airportsFound = new(StringComparer.OrdinalIgnoreCase);
        private bool updating;

        public MainWindow(string[] startFiles)
        {
            Title = $"ATCRoom Map Creator ({Version})";
            Width = 1180;
            Height = 780;
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AuroraPAR.ico")); } catch (Exception) { }
            AllowDrop = true;
            // Preview: also over the text boxes (they would take the drop for themselves).
            PreviewDragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            PreviewDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
                {
                    e.Handled = true;
                    OpenPaths(dropped);
                }
            };

            Grid root = new() { Margin = new Thickness(12) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column: drop zone, the parts found, then the airport, the cut and the save.
            Grid left = new() { Margin = new Thickness(0, 0, 12, 0) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid drop = new() { Height = 92, Margin = new Thickness(0, 0, 0, 10) };
            drop.Children.Add(new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x6A, 0x7A, 0x8A)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                RadiusX = 8,
                RadiusY = 8,
                Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xF8))
            });
            StackPanel dropText = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            dropText.Children.Add(new TextBlock { Text = "Drop the map files here (many at once, or a folder)", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            dropText.Children.Add(new TextBlock
            {
                Text = "IVAO: .geo .fix .vor .ndb .ap .vfi .str .mva lartcc/hartcc · EuroScope .ese · AIP table (PDF)",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Button browse = new() { Content = "Browse...", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            browse.Click += (s, e) => Browse();
            dropText.Children.Add(browse);
            drop.Children.Add(dropText);
            Grid.SetRow(drop, 0);
            left.Children.Add(drop);

            StackPanel listContent = new();
            listContent.Children.Add(new TextBlock { Text = "Parts found: choose the category of each", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            listContent.Children.Add(entriesPanel);
            listContent.Children.Add(notesText);
            ScrollViewer scroll = new() { Content = listContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(scroll, 1);
            left.Children.Add(scroll);

            StackPanel controls = new();
            controls.Children.Add(new TextBlock { Text = "Airport of these maps (ICAO code, e.g. LIBC)" });
            controls.Children.Add(airportBox);
            controls.Children.Add(cutBox);
            StackPanel centreRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 4, 0, 0) };
            centreRow.Children.Add(new TextBlock { Text = "Centre", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            centreRow.Children.Add(centreBox);
            centreRow.Children.Add(new TextBlock { Text = "Radius", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
            centreRow.Children.Add(radiusBox);
            centreRow.Children.Add(new TextBlock { Text = "NM", VerticalAlignment = VerticalAlignment.Center });
            controls.Children.Add(centreRow);
            Button drawButton = new()
            {
                Content = "Draw the circle on the picture",
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(18, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Then click the centre on the picture and drag to the border of the circle."
            };
            drawButton.Click += (s, e) =>
            {
                preview.DrawingCircle = true;
                cutText.Text = "Click the centre on the picture and drag to the border of the circle.";
            };
            controls.Children.Add(drawButton);
            controls.Children.Add(cutText);
            controls.Children.Add(new TextBlock { Text = "Source, author, licence (optional)", Margin = new Thickness(0, 8, 0, 0) });
            controls.Children.Add(sourceBox);
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(saveAsButton);
            buttons.Children.Add(clearButton);
            controls.Children.Add(buttons);
            controls.Children.Add(statusText);
            controls.Children.Add(new TextBlock
            {
                Text = $"Saved into the maps of the Trainer: {TrainerMapsFolderText}. The maps you make are for your own use: data of IVAO, VATSIM or an AIP may not be passed on without their owner's permission.",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            Grid.SetRow(controls, 2);
            left.Children.Add(controls);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            Border previewFrame = new() { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = preview };
            Grid.SetColumn(previewFrame, 1);
            root.Children.Add(previewFrame);
            Content = root;

            saveButton.Click += (s, e) => SaveIntoTrainer();
            saveAsButton.Click += (s, e) => SaveAs();
            clearButton.Click += (s, e) => ClearAll();
            cutBox.Click += (s, e) => ApplyCut();
            radiusBox.TextChanged += (s, e) => ApplyCut();
            centreBox.SelectionChanged += (s, e) => CentreChosen();
            preview.CircleDrawn += (latitude, longitude, radius) =>
            {
                cutLatitude = latitude;
                cutLongitude = longitude;
                radiusBox.Text = Math.Round(radius, 1).ToString(CultureInfo.InvariantCulture);
                updating = true;
                cutBox.IsChecked = true;
                if (centreBox.Items.Count > 0) centreBox.SelectedIndex = 0;
                updating = false;
                ApplyCut();
            };

            Rebuild(refit: true);
            if (startFiles.Length > 0) Loaded += (s, e) => OpenPaths(startFiles);
        }

        private static string Version
        {
            get
            {
                string? text = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(text)) return "1.1.0";
                int plus = text.IndexOf('+');
                return plus >= 0 ? text[..plus] : text;
            }
        }

        /// <summary>
        /// The maps folder of AuroraPAR v2 Trainer: beside the program when the Trainer is portable here (its settings file
        /// next to this program), else in the user's settings folder.
        /// </summary>
        private static string TrainerMapsFolder
        {
            get
            {
                string here = AppContext.BaseDirectory;
                if (File.Exists(IoPath.Combine(here, "AuroraPARTrainer.settings.json"))) return IoPath.Combine(here, "Maps");
                return IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPARTrainer", "Maps");
            }
        }

        /// <summary>The folder as said to the user, without the Windows user name.</summary>
        private static string TrainerMapsFolderText =>
            File.Exists(IoPath.Combine(AppContext.BaseDirectory, "AuroraPARTrainer.settings.json"))
                ? "the Maps folder beside this program (portable Trainer)"
                : "%AppData%\\AuroraPARTrainer\\Maps";

        private void Browse()
        {
            OpenFileDialog dialog = new()
            {
                Title = "Map files",
                Multiselect = true,
                Filter = "Map files|*.geo;*.fix;*.vor;*.ndb;*.ap;*.apt;*.vfi;*.str;*.mva;*.lartcc;*.hartcc;*.artcc;*.ese;*.pdf;*.txt|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true) OpenPaths(dialog.FileNames);
        }

        // ----- Reading ---------------------------------------------------------------------------------------------

        /// <summary>Reads the files (the known ones of a folder) and adds them to what is already there.</summary>
        private void OpenPaths(IEnumerable<string> paths)
        {
            List<string> list = [];
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        list.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                            .Where(f => KnownExtensions.Contains(IoPath.GetExtension(f))).Order(StringComparer.OrdinalIgnoreCase).Take(400));
                    }
                    catch (Exception) { }
                }
                else
                {
                    list.Add(path);
                }
            }
            if (list.Count == 0) return;
            bool first = files.Count == 0;
            statusText.Text = "";
            foreach (string path in list)
            {
                FileImport import = ReadFile(path);
                files[path] = import;
                if (import.Airport != null && import.Entries.Count > 0 && airportBox.Text.Trim().Length == 0) airportBox.Text = import.Airport;
            }
            Rebuild(refit: first);
        }

        private static FileImport ReadFile(string path)
        {
            try
            {
                byte[] head = new byte[5];
                int read;
                using (FileStream stream = File.OpenRead(path)) read = stream.Read(head, 0, head.Length);
                if (read >= 4 && Encoding.ASCII.GetString(head, 0, 4) == "%PDF") return MapImport.Read(path, null, PdfWords.Read(path));
                string[] lines = File.ReadAllLines(path);
                // Aurora and EuroScope files are often in the Windows code page: read again so the accents survive.
                if (lines.Any(l => l.Contains('�'))) lines = File.ReadAllLines(path, Encoding.Latin1);
                return MapImport.Read(path, lines, null);
            }
            catch (Exception ex)
            {
                FileImport failed = new() { FileName = path, FormatName = "not read" };
                failed.Notes.Add($"The file could not be read ({ex.Message}).");
                return failed;
            }
        }

        private void ClearAll()
        {
            files.Clear();
            cutLatitude = cutLongitude = null;
            updating = true;
            cutBox.IsChecked = false;
            updating = false;
            statusText.Text = "";
            Rebuild(refit: true);
        }

        // ----- The list of parts ---------------------------------------------------------------------------------------

        /// <summary>The list of the parts found, the notes and the picture, from the files read.</summary>
        private void Rebuild(bool refit)
        {
            entriesPanel.Children.Clear();
            foreach (FileImport file in files.Values)
            {
                foreach (ImportEntry entry in file.Entries) entriesPanel.Children.Add(EntryRow(file, entry));
            }
            if (entriesPanel.Children.Count == 0)
            {
                entriesPanel.Children.Add(new TextBlock { Text = "Nothing yet.", Foreground = Brushes.DimGray });
            }
            List<string> notes = [];
            foreach (FileImport file in files.Values)
            {
                string name = IoPath.GetFileName(file.FileName);
                if (file.Entries.Count == 0 && file.Notes.Count == 0) notes.Add($"• {name}: nothing found.");
                foreach (string note in file.Notes) notes.Add($"• {name}: {note}");
            }
            notesText.Text = string.Join("\n", notes);
            RefreshPicture(refit);
        }

        private UIElement EntryRow(FileImport file, ImportEntry entry)
        {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel text = new();
            text.Children.Add(new TextBlock { Text = entry.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = $"{IoPath.GetFileName(file.FileName)} · {entry.Content.Summary}",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            ComboBox box = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            box.Items.Add("— not used —");
            foreach (MapCategoryInfo category in MapCategory.All) box.Items.Add(category.Name);
            int index = entry.Category == null ? 0 : MapCategory.All.ToList().FindIndex(c => c.Id == entry.Category) + 1;
            box.SelectedIndex = Math.Max(0, index);
            if (entry.Suggested != null) box.ToolTip = $"Suggested: {MapCategory.NameOf(entry.Suggested)} (from the kind of file or its name; you decide).";
            box.SelectionChanged += (s, e) =>
            {
                if (updating) return;
                entry.Category = box.SelectedIndex <= 0 ? null : MapCategory.All[box.SelectedIndex - 1].Id;
                RefreshPicture(refit: false);
            };
            Grid.SetColumn(text, 0);
            Grid.SetColumn(box, 1);
            row.Children.Add(text);
            row.Children.Add(box);
            return new Border
            {
                Child = row,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        /// <summary>Everything with a category, together (before the cut).</summary>
        private MapFile Combine()
        {
            MapFile combined = new();
            foreach (FileImport file in files.Values)
            {
                foreach (ImportEntry entry in file.Entries)
                {
                    if (entry.Category != null) combined.AddAs(entry.Content, entry.Category);
                }
            }
            return combined;
        }

        private static bool IsEmpty(MapFile map) => map.Lines.Count == 0 && map.Labels.Count == 0 && map.Points.Count == 0;

        private void RefreshPicture(bool refit)
        {
            MapFile combined = Combine();
            preview.SetMap(IsEmpty(combined) ? null : combined, refit);
            // Airports found in the files: centres for the cut.
            airportsFound.Clear();
            foreach (MapPoint point in combined.Points.Where(p => p.Category == MapCategory.Airports && p.Name.Length > 0))
            {
                airportsFound[point.Name] = (point.Latitude, point.Longitude);
            }
            string? selected = centreBox.SelectedIndex > 0 ? centreBox.SelectedItem as string : null;
            updating = true;
            centreBox.Items.Clear();
            centreBox.Items.Add("(drawn on the picture)");
            foreach (string icao in airportsFound.Keys.Order(StringComparer.OrdinalIgnoreCase)) centreBox.Items.Add(icao);
            centreBox.SelectedIndex = selected != null && centreBox.Items.Contains(selected) ? centreBox.Items.IndexOf(selected) : 0;
            updating = false;
            saveButton.IsEnabled = !IsEmpty(combined);
            saveAsButton.IsEnabled = !IsEmpty(combined);
            ApplyCut();
        }

        // ----- The cut ---------------------------------------------------------------------------------------------

        private bool TryRadius(out double radius)
        {
            bool ok = double.TryParse(radiusBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out radius) && radius > 0 && radius <= 2000;
            radiusBox.Background = ok ? Brushes.White : new SolidColorBrush(Color.FromRgb(255, 215, 215));
            return ok;
        }

        /// <summary>An airport of the files was chosen as the centre of the circle.</summary>
        private void CentreChosen()
        {
            if (updating || centreBox.SelectedIndex <= 0) return;
            if (centreBox.SelectedItem is string icao && airportsFound.TryGetValue(icao, out (double Latitude, double Longitude) at))
            {
                cutLatitude = at.Latitude;
                cutLongitude = at.Longitude;
                if (airportBox.Text.Trim().Length == 0) airportBox.Text = icao.Length == 4 ? icao : "";
                updating = true;
                cutBox.IsChecked = true;
                updating = false;
                ApplyCut();
            }
        }

        private bool CutActive(out double latitude, out double longitude, out double radius)
        {
            latitude = cutLatitude ?? 0;
            longitude = cutLongitude ?? 0;
            radius = 0;
            return cutBox.IsChecked == true && cutLatitude != null && cutLongitude != null && TryRadius(out radius);
        }

        /// <summary>Shows the circle on the picture and what is inside it.</summary>
        private void ApplyCut()
        {
            if (updating) return;
            bool active = CutActive(out double latitude, out double longitude, out double radius);
            preview.CutOn = active;
            if (active)
            {
                preview.CutLatitude = latitude;
                preview.CutLongitude = longitude;
                preview.CutRadiusNM = radius;
            }
            preview.InvalidateVisual();
            MapFile combined = Combine();
            if (IsEmpty(combined))
            {
                cutText.Text = "";
            }
            else if (active)
            {
                MapFile cut = MapCut.CutCircle(combined, latitude, longitude, radius);
                cutText.Text = $"Inside the circle (kept): {cut.Summary}. The rest is dimmed on the picture.";
            }
            else if (cutBox.IsChecked == true)
            {
                cutText.Text = cutLatitude == null ? "Choose an airport as the centre, or draw the circle on the picture." : "The radius is not a number of NM.";
            }
            else
            {
                cutText.Text = $"Everything is kept: {combined.Summary}.";
            }
        }

        // ----- Saving ----------------------------------------------------------------------------------------------

        /// <summary>The map to save (cut, with the airport and the sources) and the categories it brings.</summary>
        private (MapFile Map, HashSet<string> Categories)? BuildOutput()
        {
            HashSet<string> categories = files.Values.SelectMany(f => f.Entries).Where(e => e.Category != null)
                .Select(e => e.Category!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (categories.Count == 0) return null;
            MapFile output = Combine();
            if (CutActive(out double latitude, out double longitude, out double radius)) output = MapCut.CutCircle(output, latitude, longitude, radius);
            string icao = airportBox.Text.Trim().ToUpperInvariant();
            output.Airport = icao;
            output.Name = icao.Length > 0 ? icao : "Map";
            string source = sourceBox.Text.Trim();
            if (source.Length > 0)
            {
                foreach (string category in categories) output.Sources[category] = source;
            }
            return (output, categories);
        }

        private static string FileNameFor(string icao)
        {
            char[] invalid = IoPath.GetInvalidFileNameChars();
            string safe = new(icao.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return (safe.Trim().Length > 0 ? safe.Trim() : "map") + ".json";
        }

        private bool AirportOk()
        {
            string icao = airportBox.Text.Trim();
            if (icao.Length == 4 && icao.All(char.IsAsciiLetterOrDigit)) return true;
            statusText.Text = "Write the ICAO code of the airport (4 letters, for example LIBC): the maps are saved one file per airport.";
            statusText.Foreground = Brushes.DarkRed;
            airportBox.Focus();
            return false;
        }

        private void SaveIntoTrainer()
        {
            if (!AirportOk()) return;
            (MapFile Map, HashSet<string> Categories)? built = BuildOutput();
            if (built == null) return;
            (MapFile map, HashSet<string> categories) = built.Value;
            string path = IoPath.Combine(TrainerMapsFolder, FileNameFor(map.Airport));
            MapFile toSave = map;
            if (File.Exists(path))
            {
                MapFile? existing = null;
                try { existing = MapFile.Load(path); } catch (Exception) { }
                if (existing == null || existing.Airport.Length == 0)
                {
                    if (MessageBox.Show(this, $"{IoPath.GetFileName(path)} is already there and is not a map of an airport of this kind. Replace it?",
                            "ATCRoom Map Creator", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                }
                else
                {
                    List<string> replaced = existing.UsedCategories().Where(c => categories.Contains(c)).Select(c => MapCategory.NameOf(c)).ToList();
                    List<string> kept = existing.UsedCategories().Where(c => !categories.Contains(c)).Select(c => MapCategory.NameOf(c)).ToList();
                    string text = $"The map of {map.Airport} is already there.\n\n" +
                        (replaced.Count > 0 ? $"Replaced: {string.Join(", ", replaced)}.\n" : "Nothing is replaced.\n") +
                        (kept.Count > 0 ? $"Kept: {string.Join(", ", kept)}.\n" : "") + "\nContinue?";
                    if (MessageBox.Show(this, text, "ATCRoom Map Creator", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    existing.RemoveCategories(categories);
                    existing.Merge(map);
                    existing.Name = map.Airport;
                    toSave = existing;
                }
            }
            Save(toSave, path, $"Saved into the maps of the Trainer as {IoPath.GetFileName(path)} ({string.Join(", ", toSave.UsedCategories().Select(c => MapCategory.NameOf(c)))}). In AuroraPAR v2 Trainer: RDR → Maps... → Reload.");
        }

        private void SaveAs()
        {
            (MapFile Map, HashSet<string> Categories)? built = BuildOutput();
            if (built == null) return;
            MapFile map = built.Value.Map;
            SaveFileDialog dialog = new()
            {
                Title = "Save the map",
                Filter = "ATCRoom map (*.json)|*.json",
                FileName = FileNameFor(map.Airport.Length > 0 ? map.Airport : "map")
            };
            try
            {
                Directory.CreateDirectory(TrainerMapsFolder);
                dialog.InitialDirectory = TrainerMapsFolder;
            }
            catch (Exception) { }
            if (dialog.ShowDialog(this) != true) return;
            Save(map, dialog.FileName, $"Saved as {IoPath.GetFileName(dialog.FileName)}.");
        }

        private void Save(MapFile map, string path, string done)
        {
            try
            {
                map.Save(path);
                statusText.Text = done;
                statusText.Foreground = Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                statusText.Text = $"Not saved: {ex.Message}";
                statusText.Foreground = Brushes.DarkRed;
            }
        }
    }
}
