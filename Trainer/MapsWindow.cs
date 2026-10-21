using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// The maps of the surveillance radar: which are shown and their colours (lines and labels), the folder, a new read of
    /// it, and ATCRoom Map Creator to make new maps. Changes are applied at once.
    /// </summary>
    internal sealed class MapsWindow : Window
    {
        private readonly TrainerSettings settings;
        private readonly Func<(List<LoadedMap> Maps, List<string> Errors)> reload;
        private readonly Action changed;
        private readonly StackPanel list = new();
        private readonly TextBlock errorsText = new() { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        /// <param name="reload">Reads the Maps folder again (and gives the maps to the RDR).</param>
        /// <param name="changed">A map was turned on or off, or a colour changed: redraw and save.</param>
        public MapsWindow(TrainerSettings settings, (List<LoadedMap> Maps, List<string> Errors) current,
            Func<(List<LoadedMap> Maps, List<string> Errors)> reload, Action changed)
        {
            this.settings = settings;
            this.reload = reload;
            this.changed = changed;
            Title = "Maps of the surveillance radar (RDR)";
            Width = 640;
            Height = 520;
            MinWidth = 480;
            MinHeight = 300;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            DockPanel root = new() { Margin = new Thickness(12) };
            TextBlock intro = new()
            {
                Text = "The maps are the files in the Maps folder of the Trainer. New maps: make them with ATCRoom Map Creator " +
                       "(from Aurora, EuroScope or AIP files), then Reload. The maps you make are for your own use.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

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
                ToolTip = File.Exists(MapLibrary.MapCreatorPath) ? "Make a new map from a file of Aurora, EuroScope or an AIP."
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
            ShowMaps(current);
        }

        /// <summary>One row per map: shown or not, colour of the lines, colour of the labels.</summary>
        private void ShowMaps((List<LoadedMap> Maps, List<string> Errors) maps)
        {
            list.Children.Clear();
            if (maps.Maps.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "No maps yet.", Foreground = Brushes.DimGray });
            }
            foreach (LoadedMap map in maps.Maps)
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
                list.Children.Add(row);
            }
            errorsText.Text = maps.Errors.Count == 0 ? "" : "Not read (not a map file):\n" + string.Join("\n", maps.Errors);
            errorsText.Visibility = maps.Errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
