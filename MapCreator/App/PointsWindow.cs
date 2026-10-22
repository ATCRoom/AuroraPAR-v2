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

        public PointRow(MapPoint point, bool isShown)
        {
            Point = point;
            this.isShown = isShown;
        }

        public MapPoint Point { get; }

        /// <summary>Text of the row: the name, the group (airport) and the long name when there is one.</summary>
        public string Label =>
            Point.Name + (Point.Group.Length > 0 ? $"   [{Point.Group}]" : "") + (Point.Description.Length > 0 ? $"   {Point.Description}" : "");

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

        /// <param name="entry">The part: its points are listed.</param>
        /// <param name="changed">A point was switched on or off: redraw the picture and the numbers.</param>
        public PointsWindow(ImportEntry entry, Action changed)
        {
            this.entry = entry;
            this.changed = changed;
            rows = entry.Content.Points.Select(p => new PointRow(p, !entry.Excluded.Contains(p))).ToList();
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

            TextBlock help = new()
            {
                Text = "Show all / Hide all act on the points in the list below: type a name (or the start of it) and choose a group to narrow it down.",
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
            box.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PointRow.Label)) { Mode = BindingMode.OneTime });
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
            countText.Text = $"{shown} of {rows.Count} points shown";
        }

        private void OnRowChanged(PointRow row)
        {
            if (row.IsShown) entry.Excluded.Remove(row.Point);
            else entry.Excluded.Add(row.Point);
            if (bulk) return;
            UpdateCount();
            changed();
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
