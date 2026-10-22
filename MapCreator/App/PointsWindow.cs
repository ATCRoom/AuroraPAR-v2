using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ATCRoom.Maps;

namespace ATCRoom.MapCreator
{
    /// <summary>One point in the list: shown (goes into the map) or not.</summary>
    internal sealed class PointRow : INotifyPropertyChanged
    {
        private bool isShown;
        private readonly ImportEntry entry;

        public PointRow(ImportEntry entry, MapPoint point, bool isShown)
        {
            this.entry = entry;
            Point = point;
            this.isShown = isShown;
        }

        /// <summary>The name was changed: the text of the row is read again.</summary>
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));

        public MapPoint Point { get; }

        /// <summary>Text of the row: the name, the group (airport) and the long name when there is one (a check box shows "_" as a key, so it is doubled).</summary>
        public string Label =>
            (Point.Name + (entry.OriginalNames.TryGetValue(Point, out string? original) ? $"   (was {original})" : "")
                + (Point.Group.Length > 0 ? $"   [{Point.Group}]" : "") + (Point.Description.Length > 0 ? $"   {Point.Description}" : "")).Replace("_", "__");

        public bool IsShown
        {
            get => isShown;
            set
            {
                if (isShown == value) return;
                isShown = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// "Choose points": the points of one part of the import, each with a check box (shown or not), a search by name, a
    /// choice of group (the airport named in the comment before the block in the source file) and the two keys
    /// "Show all" / "Hide all", that act on the points in the list (after the search and the group). Changes are
    /// applied at once to the picture.
    /// </summary>
    internal sealed class PointsWindow : Window
    {
        private const string AllGroups = "(all groups)";
        private readonly ImportEntry entry;
        private readonly Action changed;
        private readonly List<PointRow> rows;
        private readonly TextBox searchBox = new() { Margin = new Thickness(0, 0, 8, 0), MinWidth = 150 };
        private readonly ComboBox groupBox = new() { MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) };
        private readonly ListBox list = new();
        private readonly TextBlock countText = new() { Margin = new Thickness(0, 8, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray };
        private bool bulk;
        private readonly TextBox prefixBox = new() { Width = 70, Margin = new Thickness(0, 0, 4, 0), ToolTip = "A text to add to the names or to take away, for example PI-" };
        private readonly Button renameButton = new() { Content = "Rename selected...", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 12, 0), IsEnabled = false };

        /// <param name="entry">The part: its points are listed.</param>
        /// <param name="changed">A point was switched on or off: redraw the picture and the numbers.</param>
        public PointsWindow(ImportEntry entry, Action changed)
        {
            this.entry = entry;
            this.changed = changed;
            rows = entry.Content.Points.Select(p => new PointRow(entry, p, !entry.Excluded.Contains(p))).ToList();
            foreach (PointRow row in rows) row.PropertyChanged += (s, e) => OnRowChanged(row);

            Title = $"Choose points: {entry.Title}";
            Width = 520;
            Height = 560;
            MinWidth = 380;
            MinHeight = 300;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            DockPanel root = new() { Margin = new Thickness(12) };

            DockPanel top = new() { Margin = new Thickness(0, 0, 0, 8) };
            Button hideAll = new() { Content = "Hide all", Padding = new Thickness(10, 3, 10, 3) };
            hideAll.Click += (s, e) => SetVisibleRows(false);
            Button showAll = new() { Content = "Show all", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            showAll.Click += (s, e) => SetVisibleRows(true);
            DockPanel.SetDock(hideAll, Dock.Right);
            DockPanel.SetDock(showAll, Dock.Right);
            DockPanel.SetDock(groupBox, Dock.Right);
            top.Children.Add(hideAll);
            top.Children.Add(showAll);
            top.Children.Add(groupBox);
            top.Children.Add(searchBox);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            WrapPanel rename = new() { Margin = new Thickness(0, 0, 0, 8) };
            renameButton.Click += (s, e) => RenameSelected();
            Button addPrefix = new() { Content = "Add prefix", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0), ToolTip = "RULFE becomes PI-RULFE (the text in the box before the name)." };
            addPrefix.Click += (s, e) => RenameVisible(name => AddPrefix(name, prefixBox.Text.Trim()));
            Button addSuffix = new() { Content = "Add suffix", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0), ToolTip = "The text in the box after the name." };
            addSuffix.Click += (s, e) => RenameVisible(name => AddSuffix(name, prefixBox.Text.Trim()));
            Button removePrefix = new() { Content = "Remove prefix", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
            removePrefix.Click += (s, e) => RenameVisible(name => RemovePrefix(name, prefixBox.Text.Trim()));
            Button removeSuffix = new() { Content = "Remove suffix", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 12, 0) };
            removeSuffix.Click += (s, e) => RenameVisible(name => RemoveSuffix(name, prefixBox.Text.Trim()));
            Button removeHyphen = new() { Content = "Remove up to the hyphen", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 6, 0), ToolTip = "PI-RULFE becomes RULFE: everything up to the first hyphen goes." };
            removeHyphen.Click += (s, e) => RenameVisible(RemoveUpToHyphen);
            Button restore = new() { Content = "Restore names", Padding = new Thickness(8, 1, 8, 1) };
            restore.Click += (s, e) => RestoreVisible();
            rename.Children.Add(renameButton);
            rename.Children.Add(prefixBox);
            rename.Children.Add(addPrefix);
            rename.Children.Add(addSuffix);
            rename.Children.Add(removePrefix);
            rename.Children.Add(removeSuffix);
            rename.Children.Add(removeHyphen);
            rename.Children.Add(restore);
            DockPanel.SetDock(rename, Dock.Top);
            root.Children.Add(rename);

            TextBlock help = new()
            {
                Text = "Show all / Hide all and the renaming keys act on the points in the list below: type a name (or the start of it) and choose a group to narrow it down.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(help, Dock.Top);
            root.Children.Add(help);

            Button close = new() { Content = "Close", Padding = new Thickness(16, 3, 16, 3), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);
            DockPanel.SetDock(countText, Dock.Bottom);
            root.Children.Add(countText);

            DataTemplate template = new();
            FrameworkElementFactory box = new(typeof(CheckBox));
            box.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(PointRow.IsShown)) { Mode = BindingMode.TwoWay });
            box.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PointRow.Label)) { Mode = BindingMode.OneWay });
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
            template.VisualTree = box;
            list.ItemTemplate = template;
            VirtualizingPanel.SetIsVirtualizing(list, true);
            root.Children.Add(list);
            Content = root;

            groupBox.Items.Add(AllGroups);
            foreach (string group in rows.Select(r => r.Point.Group).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                groupBox.Items.Add(group);
            }
            groupBox.SelectedIndex = 0;
            groupBox.Visibility = groupBox.Items.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
            list.SelectionChanged += (s, e) => renameButton.IsEnabled = list.SelectedItem is PointRow;
            searchBox.TextChanged += (s, e) => Refill();
            groupBox.SelectionChanged += (s, e) => Refill();
            Refill();
        }

        /// <summary>The rows that match the search and the group.</summary>
        private List<PointRow> VisibleRows()
        {
            string text = searchBox.Text.Trim();
            string? group = groupBox.SelectedIndex > 0 ? groupBox.SelectedItem as string : null;
            return rows.Where(r =>
                (text.Length == 0 || r.Point.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Point.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
                && (group == null || string.Equals(r.Point.Group, group, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        private void Refill()
        {
            list.ItemsSource = VisibleRows();
            UpdateCount();
        }

        private void UpdateCount()
        {
            int shown = rows.Count(r => r.IsShown);
            int twice = rows.Where(r => r.IsShown).GroupBy(r => r.Point.Name, StringComparer.OrdinalIgnoreCase).Count(g => g.Count() > 1);
            countText.Text = $"{shown} of {rows.Count} points shown" + (twice > 0 ? $"   ·   {twice} name{(twice == 1 ? " is" : "s are")} used by more than one point" : "");
        }

        private void OnRowChanged(PointRow row)
        {
            if (row.IsShown) entry.Excluded.Remove(row.Point);
            else entry.Excluded.Add(row.Point);
            if (bulk) return;
            UpdateCount();
            changed();
        }

        /// <summary>A new name for a point (the name it had in the file is kept, to restore it).</summary>
        private void SetName(PointRow row, string newName)
        {
            string name = newName.Trim();
            if (name.Length == 0 || name == row.Point.Name) return;
            if (!entry.OriginalNames.ContainsKey(row.Point)) entry.OriginalNames[row.Point] = row.Point.Name;
            row.Point.Name = name;
            if (entry.OriginalNames[row.Point] == name) entry.OriginalNames.Remove(row.Point);
            row.Refresh();
        }

        private void RenameSelected()
        {
            if (list.SelectedItem is not PointRow row) return;
            string? name = AskName(this, row.Point.Name);
            if (name == null) return;
            SetName(row, name);
            UpdateCount();
            changed();
        }

        /// <summary>The new name of every point in the list (null: leave it as it is).</summary>
        private void RenameVisible(Func<string, string?> rule)
        {
            foreach (PointRow row in VisibleRows())
            {
                string? name = rule(row.Point.Name);
                if (name != null) SetName(row, name);
            }
            UpdateCount();
            changed();
        }

        private static string? RemovePrefix(string name, string prefix) =>
            prefix.Length > 0 && name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : null;

        private static string? AddPrefix(string name, string prefix) =>
            prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? prefix + name : null;

        private static string? AddSuffix(string name, string suffix) =>
            suffix.Length > 0 && !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name + suffix : null;

        private static string? RemoveSuffix(string name, string suffix) =>
            suffix.Length > 0 && name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : null;

        /// <summary>The labels were chosen again ("Label from"): the rows are read again.</summary>
        public void RefreshNames()
        {
            foreach (PointRow row in rows) row.Refresh();
            Refill();
        }

        private static string? RemoveUpToHyphen(string name)
        {
            int hyphen = name.IndexOf('-');
            return hyphen >= 0 && hyphen < name.Length - 1 ? name[(hyphen + 1)..] : null;
        }

        private void RestoreVisible()
        {
            foreach (PointRow row in VisibleRows())
            {
                if (!entry.OriginalNames.TryGetValue(row.Point, out string? original)) continue;
                row.Point.Name = original;
                entry.OriginalNames.Remove(row.Point);
                row.Refresh();
            }
            UpdateCount();
            changed();
        }

        /// <summary>A small window to type a name; null when cancelled.</summary>
        private static string? AskName(Window owner, string current)
        {
            Window window = new()
            {
                Title = "Rename point",
                Width = 340,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            StackPanel panel = new() { Margin = new Thickness(12) };
            TextBox box = new() { Text = current, Margin = new Thickness(0, 0, 0, 10) };
            string? result = null;
            Button ok = new() { Content = "OK", IsDefault = true, Padding = new Thickness(16, 3, 16, 3), Margin = new Thickness(0, 0, 6, 0) };
            ok.Click += (s, e) =>
            {
                result = box.Text;
                window.DialogResult = true;
            };
            Button cancel = new() { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            window.Content = panel;
            window.Loaded += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };
            window.ShowDialog();
            return result;
        }

        private void SetVisibleRows(bool shown)
        {
            bulk = true;
            foreach (PointRow row in VisibleRows()) row.IsShown = shown;
            bulk = false;
            UpdateCount();
            changed();
        }
    }
}
