using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATCRoom.Maps;

namespace ATCRoom.MapCreator
{
    /// <summary>
    /// "Open an existing map": the map files (.json) found in the maps folder of the Trainer, each with its airport and the
    /// categories it has. One is chosen to be loaded into the Map Creator, to add or change something.
    /// </summary>
    internal sealed class MapPickerWindow : Window
    {
        private readonly ListBox list = new();
        private readonly Button open = new() { Content = "Open", IsDefault = true, IsEnabled = false, Padding = new Thickness(16, 3, 16, 3), Margin = new Thickness(0, 0, 6, 0) };
        private string? chosen;

        private MapPickerWindow(Window owner, string folder, List<(string Path, string Text)> maps, List<string> unreadable)
        {
            Title = "Open an existing map";
            Width = 480;
            Height = 380;
            MinWidth = 360;
            MinHeight = 240;
            ShowInTaskbar = false;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            DockPanel root = new() { Margin = new Thickness(12) };
            TextBlock where = new() { Text = $"Maps in {folder}", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(where, Dock.Top);
            root.Children.Add(where);

            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            Button cancel = new() { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
            buttons.Children.Add(open);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            if (unreadable.Count > 0)
            {
                TextBlock bad = new()
                {
                    Text = "Not a map file: " + string.Join(", ", unreadable),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.DarkRed,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                DockPanel.SetDock(bad, Dock.Bottom);
                root.Children.Add(bad);
            }

            foreach ((string path, string text) in maps)
            {
                list.Items.Add(new ListBoxItem { Content = text, Tag = path, Padding = new Thickness(4) });
            }
            if (maps.Count == 0)
            {
                list.Items.Add(new ListBoxItem { Content = "No maps yet in this folder.", IsEnabled = false });
            }
            list.SelectionChanged += (s, e) => open.IsEnabled = list.SelectedItem is ListBoxItem { Tag: string };
            list.MouseDoubleClick += (s, e) =>
            {
                if (list.SelectedItem is ListBoxItem { Tag: string }) Choose();
            };
            open.Click += (s, e) => Choose();
            root.Children.Add(list);
            Content = root;
        }

        private void Choose()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: string path }) return;
            chosen = path;
            DialogResult = true;
        }

        /// <summary>Shows the maps of the folder; the path of the one chosen, or null.</summary>
        public static string? Pick(Window owner, string folder)
        {
            List<(string Path, string Text)> maps = [];
            List<string> unreadable = [];
            try
            {
                if (Directory.Exists(folder))
                {
                    foreach (string path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                    {
                        string name = System.IO.Path.GetFileName(path);
                        try
                        {
                            MapFile map = MapFile.Load(path);
                            string categories = string.Join(", ", map.UsedCategories().Select(id => MapCategory.NameOf(id)));
                            string airport = map.Airport.Length > 0 ? map.Airport : "old kind, no airport";
                            maps.Add((path, $"{name}  ·  {airport}  ·  {(categories.Length > 0 ? categories : "empty")}"));
                        }
                        catch (Exception)
                        {
                            unreadable.Add(name);
                        }
                    }
                }
            }
            catch (Exception) { }
            MapPickerWindow window = new(owner, folder, maps, unreadable);
            return window.ShowDialog() == true ? window.chosen : null;
        }
    }
}
