using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ATCRoom.Maps;
using Microsoft.Win32;

namespace ATCRoom.MapCreator
{
    /// <summary>
    /// The window of ATCRoom Map Creator: drop a file (or Browse), see what was read and a preview, give the map a name,
    /// save it into the maps of AuroraPAR v2 Trainer (or anywhere with Save as).
    /// </summary>
    internal sealed class MainWindow : Window
    {
        private readonly TextBlock fileText = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock notesText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly StackPanel eseOptions = new() { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        private readonly CheckBox sectorLinesBox = new() { Content = "Sector lines and circles", IsChecked = true, Margin = new Thickness(0, 2, 0, 0) };
        private readonly CheckBox msawBox = new() { Content = "MSAW areas (minimum altitudes), with their altitude", IsChecked = true, Margin = new Thickness(0, 2, 0, 0) };
        private readonly CheckBox freeTextBox = new() { Content = "Free texts", IsChecked = true, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBox nameBox = new() { Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(2) };
        private readonly TextBox sourceBox = new() { Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(2) };
        private readonly Button saveButton = new() { Content = "Save into the maps of the Trainer", Padding = new Thickness(12, 4, 12, 4), IsEnabled = false };
        private readonly Button saveAsButton = new() { Content = "Save as...", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly MapPreview preview = new();

        private string? sourcePath;
        private IReadOnlyList<string>? textLines;
        private ImportResult? result;

        public MainWindow(string? startFile)
        {
            Title = $"ATCRoom Map Creator ({Version})";
            Width = 960;
            Height = 680;
            MinWidth = 640;
            MinHeight = 480;
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
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    e.Handled = true;
                    Open(files[0]);
                }
            };

            DockPanel root = new() { Margin = new Thickness(12) };

            // Drop zone.
            Grid drop = new() { Height = 96, Margin = new Thickness(0, 0, 0, 10) };
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
            dropText.Children.Add(new TextBlock { Text = "Drop a map file here", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            dropText.Children.Add(new TextBlock
            {
                Text = "Aurora map (IVAO) · EuroScope .ese (VATSIM) · AIP table of minimum altitudes (PDF)",
                Foreground = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Button browse = new() { Content = "Browse...", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            browse.Click += (s, e) => Browse();
            dropText.Children.Add(browse);
            drop.Children.Add(dropText);
            DockPanel.SetDock(drop, Dock.Top);
            root.Children.Add(drop);

            // Save row and status at the bottom.
            StackPanel bottom = new();
            StackPanel buttons = new() { Orientation = Orientation.Horizontal };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(saveAsButton);
            bottom.Children.Add(buttons);
            bottom.Children.Add(statusText);
            bottom.Children.Add(new TextBlock
            {
                Text = "The maps you make are for your own use: data of IVAO, VATSIM or an AIP may not be passed on without their owner's permission.",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            saveButton.Click += (s, e) => SaveIntoTrainer();
            saveAsButton.Click += (s, e) => SaveAs();

            // Left: what was read and the name; right: the preview.
            Grid middle = new() { Margin = new Thickness(0, 0, 0, 10) };
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel info = new() { Margin = new Thickness(0, 0, 12, 0) };
            info.Children.Add(fileText);
            info.Children.Add(notesText);
            eseOptions.Children.Add(new TextBlock { Text = "Put in the map:", FontWeight = FontWeights.SemiBold });
            eseOptions.Children.Add(sectorLinesBox);
            eseOptions.Children.Add(msawBox);
            eseOptions.Children.Add(freeTextBox);
            foreach (CheckBox box in new[] { sectorLinesBox, msawBox, freeTextBox }) box.Click += (s, e) => Convert();
            info.Children.Add(eseOptions);
            info.Children.Add(new TextBlock { Text = "Name of the map", Margin = new Thickness(0, 12, 0, 0) });
            info.Children.Add(nameBox);
            info.Children.Add(new TextBlock { Text = "Source, author, licence (optional)", Margin = new Thickness(0, 8, 0, 0) });
            info.Children.Add(sourceBox);
            info.Children.Add(new TextBlock
            {
                Text = $"Saved into the maps of the Trainer: {TrainerMapsFolderText}",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
            ScrollViewer infoScroll = new() { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(infoScroll, 0);
            middle.Children.Add(infoScroll);
            Border previewFrame = new() { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = preview };
            Grid.SetColumn(previewFrame, 1);
            middle.Children.Add(previewFrame);
            root.Children.Add(middle);

            Content = root;
            fileText.Text = "No file yet.";
            if (!string.IsNullOrEmpty(startFile)) Loaded += (s, e) => Open(startFile);
        }

        private static string Version
        {
            get
            {
                string? text = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(text)) return "1.0.0";
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
                if (File.Exists(System.IO.Path.Combine(here, "AuroraPARTrainer.settings.json"))) return System.IO.Path.Combine(here, "Maps");
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPARTrainer", "Maps");
            }
        }

        /// <summary>The folder as said to the user, without the Windows user name.</summary>
        private static string TrainerMapsFolderText =>
            File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "AuroraPARTrainer.settings.json"))
                ? "the Maps folder beside this program (portable Trainer)"
                : "%AppData%\\AuroraPARTrainer\\Maps";

        private void Browse()
        {
            OpenFileDialog dialog = new()
            {
                Title = "Map file to convert",
                Filter = "Map files (*.txt;*.mva;*.ese;*.pdf)|*.txt;*.mva;*.ese;*.pdf|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true) Open(dialog.FileName);
        }

        /// <summary>Reads a file and converts it with the importer of its format.</summary>
        private void Open(string path)
        {
            sourcePath = null;
            textLines = null;
            result = null;
            pdfWords = null;
            statusText.Text = "";
            try
            {
                byte[] head = new byte[5];
                using (FileStream stream = File.OpenRead(path)) stream.ReadExactly(head, 0, Math.Min(5, (int)Math.Min(5, stream.Length)));
                if (System.Text.Encoding.ASCII.GetString(head).StartsWith("%PDF"))
                {
                    pdfWords = PdfWords.Read(path);
                }
                else
                {
                    textLines = File.ReadAllLines(path);
                    // Aurora and EuroScope files are often in the Windows code page: read again so the accents survive.
                    if (textLines.Any(l => l.Contains('\uFFFD'))) textLines = File.ReadAllLines(path, System.Text.Encoding.Latin1);
                }
                sourcePath = path;
                nameBox.Text = System.IO.Path.GetFileNameWithoutExtension(path);
                sourceBox.Text = "";
                Convert();
            }
            catch (Exception ex)
            {
                ShowResult(null, $"{System.IO.Path.GetFileName(path)}: the file could not be read ({ex.Message}).");
            }
        }

        private List<PdfWord>? pdfWords;

        /// <summary>Converts the file read (again, when the options change) and shows the result.</summary>
        private void Convert()
        {
            if (sourcePath == null) return;
            string name = System.IO.Path.GetFileName(sourcePath);
            if (pdfWords != null)
            {
                result = MapImport.ImportAipTable(pdfWords);
            }
            else if (textLines != null)
            {
                switch (MapImport.Detect(textLines))
                {
                    case MapSourceFormat.Aurora:
                        result = MapImport.ImportAurora(textLines);
                        break;
                    case MapSourceFormat.EuroScope:
                        result = MapImport.ImportEuroScope(textLines, new EseOptions
                        {
                            SectorLines = sectorLinesBox.IsChecked == true,
                            MsawAreas = msawBox.IsChecked == true,
                            FreeText = freeTextBox.IsChecked == true
                        });
                        break;
                    default:
                        ShowResult(null, $"{name}: format not recognised. Known formats: Aurora map (lines T; and L;), EuroScope .ese, AIP table of minimum altitudes (PDF).");
                        return;
                }
            }
            if (result == null) return;
            eseOptions.Visibility = result.Format == MapSourceFormat.EuroScope ? Visibility.Visible : Visibility.Collapsed;
            string format = result.Format switch
            {
                MapSourceFormat.Aurora => "Aurora map (IVAO)",
                MapSourceFormat.EuroScope => "EuroScope .ese (VATSIM)",
                MapSourceFormat.AipTable => "AIP table of minimum altitudes (PDF)",
                _ => "?"
            };
            ShowResult(result.Map, $"{name}\n{format}: {result.Map.Summary}.");
            notesText.Text = string.Join("\n", result.Notes.Select(n => "• " + n));
        }

        private void ShowResult(MapFile? map, string text)
        {
            fileText.Text = text;
            if (map == null) notesText.Text = "";
            preview.Map = map;
            preview.InvalidateVisual();
            bool ok = map != null && (map.Lines.Count > 0 || map.Labels.Count > 0 || map.Points.Count > 0);
            saveButton.IsEnabled = ok;
            saveAsButton.IsEnabled = ok;
        }

        /// <summary>The map to save, with the name and source typed.</summary>
        private MapFile? Prepared()
        {
            if (result == null) return null;
            MapFile map = result.Map;
            map.Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : System.IO.Path.GetFileNameWithoutExtension(sourcePath ?? "map");
            map.Source = sourceBox.Text.Trim();
            return map;
        }

        private static string FileNameFor(string name)
        {
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string safe = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return (safe.Trim().Length > 0 ? safe.Trim() : "map") + ".json";
        }

        private void SaveIntoTrainer()
        {
            MapFile? map = Prepared();
            if (map == null) return;
            string path = System.IO.Path.Combine(TrainerMapsFolder, FileNameFor(map.Name));
            if (File.Exists(path) && MessageBox.Show(this, $"A map named {System.IO.Path.GetFileName(path)} is already there. Replace it?",
                    "ATCRoom Map Creator", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }
            Save(map, path, $"Saved into the maps of the Trainer as {System.IO.Path.GetFileName(path)}. In AuroraPAR v2 Trainer: RDR → Maps... → Reload.");
        }

        private void SaveAs()
        {
            MapFile? map = Prepared();
            if (map == null) return;
            SaveFileDialog dialog = new()
            {
                Title = "Save the map",
                Filter = "ATCRoom map (*.json)|*.json",
                FileName = FileNameFor(map.Name)
            };
            try
            {
                Directory.CreateDirectory(TrainerMapsFolder);
                dialog.InitialDirectory = TrainerMapsFolder;
            }
            catch (Exception) { }
            if (dialog.ShowDialog(this) != true) return;
            Save(map, dialog.FileName, $"Saved as {System.IO.Path.GetFileName(dialog.FileName)}.");
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

    /// <summary>A quick picture of the map, to check it before saving (north up, the whole map in the frame).</summary>
    internal sealed class MapPreview : FrameworkElement
    {
        public MapFile? Map { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth, height = ActualHeight;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), null, new Rect(0, 0, width, height));
            MapFile? map = Map;
            if (map == null || width < 20 || height < 20) return;
            List<double[]> all = map.Lines.SelectMany(l => l.Points)
                .Concat(map.Labels.Select(l => new[] { l.Latitude, l.Longitude }))
                .Concat(map.Points.Select(p => new[] { p.Latitude, p.Longitude })).ToList();
            if (all.Count == 0) return;
            double minLat = all.Min(p => p[0]), maxLat = all.Max(p => p[0]);
            double minLon = all.Min(p => p[1]), maxLon = all.Max(p => p[1]);
            double k = Math.Cos((minLat + maxLat) / 2 * Math.PI / 180);
            double spanX = Math.Max(1e-6, (maxLon - minLon) * k), spanY = Math.Max(1e-6, maxLat - minLat);
            double scale = Math.Min((width - 40) / spanX, (height - 40) / spanY);
            double offsetX = (width - spanX * scale) / 2, offsetY = (height - spanY * scale) / 2;
            Point P(double lat, double lon) => new(offsetX + (lon - minLon) * k * scale, offsetY + (maxLat - lat) * scale);
            Color lineColour = (Color)ColorConverter.ConvertFromString(map.LineColour);
            Color labelColour = (Color)ColorConverter.ConvertFromString(map.LabelColour);
            Pen pen = new(new SolidColorBrush(Color.FromRgb(
                (byte)Math.Min(255, lineColour.R + 60), (byte)Math.Min(255, lineColour.G + 60), (byte)Math.Min(255, lineColour.B + 60))), 1);
            foreach (MapLine line in map.Lines)
            {
                StreamGeometry geometry = new();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(P(line.Points[0][0], line.Points[0][1]), false, false);
                    for (int i = 1; i < line.Points.Count; i++) context.LineTo(P(line.Points[i][0], line.Points[i][1]), true, false);
                }
                dc.DrawGeometry(null, pen, geometry);
            }
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(
                (byte)Math.Min(255, labelColour.R + 90), (byte)Math.Min(255, labelColour.G + 90), (byte)Math.Min(255, labelColour.B + 90)));
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Typeface face = new("Segoe UI");
            foreach (MapLabel label in map.Labels)
            {
                FormattedText text = new(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, labelBrush, pixelsPerDip);
                Point at = P(label.Latitude, label.Longitude);
                dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
            }
            foreach (MapPoint point in map.Points)
            {
                Point at = P(point.Latitude, point.Longitude);
                dc.DrawEllipse(labelBrush, null, at, 2.5, 2.5);
                dc.DrawText(new FormattedText(point.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10, labelBrush, pixelsPerDip), new Point(at.X + 4, at.Y - 6));
            }
        }
    }
}
