using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Table to edit a list of distance reminders (used for the profile and for a single runway).
    /// Every change is reported at once through the callback.
    /// </summary>
    internal class ReminderEditor : StackPanel
    {
        private static readonly string[] ShowNames = ["Marker", "Line", "Both"];
        private static readonly string[] DashNames = ["Solid", "Dashed", "Dash-dot", "Dotted"];
        private static readonly SymbolShape[] MarkerShapes = Enum.GetValues<SymbolShape>()
            .Where(s => s != SymbolShape.None && s != SymbolShape.Line && s != SymbolShape.Custom).ToArray();

        private readonly Func<List<DistanceReminder>> list;
        private readonly Action changed;
        private readonly Grid grid = new();
        private bool building;

        /// <param name="list">Gets the list to edit (it may be replaced between calls, e.g. another runway).</param>
        /// <param name="changed">Called after every change (save and redraw).</param>
        public ReminderEditor(Func<List<DistanceReminder>> list, Action changed)
        {
            this.list = list;
            this.changed = changed;
            Children.Add(grid);
            Button add = new() { Content = "Add reminder", Width = 110, Height = 26, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            add.Click += (s, e) =>
            {
                List<DistanceReminder> reminders = list();
                double next = reminders.Count == 0 ? 4 : Math.Max(0.5, reminders.Max(r => r.Distance) - 1);
                reminders.Add(new DistanceReminder { Distance = next });
                changed();
                Rebuild();
            };
            Children.Add(add);
            Children.Add(new TextBlock
            {
                Text = "Note: optional. Shown only as a tooltip when the mouse is over the marker or the line — nothing is written on the radar picture. Use it as a reminder of the action to take at that distance (e.g. \"Coordinate with TWR\").",
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            });
            Rebuild();
        }

        /// <summary>Rebuilds the table from the list (e.g. after another runway is selected).</summary>
        public void Rebuild()
        {
            building = true;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            foreach (double width in new double[] { 70, 76, 140, 54, 86, 56, 240, 200, 30 })
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] headers = [AuroraPAR.Distance.Kilometres ? "Distance km" : "Distance NM", "Show", "Symbol", "Size", "Line", "Width", "Colour", "Note (optional)", ""];
            for (int c = 0; c < headers.Length; c++)
            {
                Add(new TextBlock { Text = headers[c], FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4) }, 0, c);
            }
            List<DistanceReminder> reminders = list();
            if (reminders.Count == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                TextBlock none = new() { Text = "No reminders.", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumnSpan(none, 4);
                Add(none, 1, 0);
            }
            int row = 1;
            foreach (DistanceReminder reminder in reminders.OrderByDescending(r => r.Distance).ToList())
            {
                AddRow(reminder, row++);
            }
            building = false;
        }

        private void AddRow(DistanceReminder reminder, int row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            void Changed()
            {
                if (building) return;
                changed();
            }

            // Shown and typed in the distance unit of the profile; kept in NM.
            double perNM = AuroraPAR.Distance.Kilometres ? DistanceUnits.KilometresPerNM : 1;
            string shown = (reminder.Distance * perNM).ToString("0.0#", CultureInfo.InvariantCulture);
            TextBox distance = new()
            {
                Text = shown,
                Width = 60,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                ToolTip = $"Distance from the touchdown point, {(perNM == 1 ? "NM" : "km")} (Enter to apply)"
            };
            void ApplyDistance()
            {
                // Unchanged text: nothing (a value rounded for the display is not saved again).
                if (distance.Text.Trim() == shown) { distance.ClearValue(TextBox.BackgroundProperty); return; }
                if (double.TryParse(distance.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double typed)
                    && typed / perNM is double value && value > 0 && value <= DistanceReminder.MaxDistance)
                {
                    distance.ClearValue(TextBox.BackgroundProperty);
                    if (value != reminder.Distance)
                    {
                        reminder.Distance = value;
                        Changed();
                    }
                }
                else
                {
                    distance.Background = new SolidColorBrush(Color.FromRgb(255, 215, 215));
                }
            }
            distance.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyDistance();
            };
            distance.LostFocus += (s, e) => ApplyDistance();
            Add(distance, row, 0);

            ComboBox show = Combo(ShowNames, (int)reminder.Show, 68);
            Add(show, row, 1);

            ComboBox symbol = new() { Width = 130, Height = 22, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (SymbolShape shape in MarkerShapes)
            {
                StackPanel item = new() { Orientation = Orientation.Horizontal };
                item.Children.Add(new Border
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new Path
                    {
                        Data = Symbols.Create(shape, 10),
                        Stroke = Brushes.Black,
                        StrokeThickness = 1.5,
                        Fill = Symbols.IsFilled(shape) ? Brushes.Black : null,
                        RenderTransform = new TranslateTransform(8, 8)
                    }
                });
                item.Children.Add(new TextBlock { Text = Symbols.DisplayName(shape), VerticalAlignment = VerticalAlignment.Center });
                symbol.Items.Add(item);
            }
            symbol.SelectedIndex = Math.Max(0, Array.IndexOf(MarkerShapes, reminder.Symbol));
            Add(symbol, row, 2);

            ComboBox size = Combo(Enumerable.Range(2, 9).Select(i => $"{i * 2}").ToArray(), Math.Clamp((int)Math.Round(reminder.Size / 2) - 2, 0, 8), 46);
            size.ToolTip = "Size of the marker, px";
            Add(size, row, 3);

            ComboBox dash = Combo(DashNames, (int)reminder.Dash, 80);
            Add(dash, row, 4);

            ComboBox width = Combo(Enumerable.Range(1, 6).Select(i => $"{i} px").ToArray(), Math.Clamp((int)Math.Round(reminder.Width) - 1, 0, 5), 50);
            Add(width, row, 5);

            Add(ColorPicker.Create(reminder.Color, ColorPicker.StandardColors, hex =>
            {
                reminder.Color = hex;
                Changed();
            }), row, 6);

            TextBox note = new()
            {
                Text = reminder.Note ?? "",
                Width = 190,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Optional reminder, shown when the mouse is over the marker. Leave empty for symbol only."
            };
            note.LostFocus += (s, e) =>
            {
                string? text = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim();
                if (text != reminder.Note)
                {
                    reminder.Note = text;
                    Changed();
                }
            };
            note.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Keyboard.ClearFocus();
            };
            Add(note, row, 7);

            Button delete = new() { Content = "✕", Width = 24, Height = 22, ToolTip = "Delete this reminder", HorizontalAlignment = HorizontalAlignment.Left };
            delete.Click += (s, e) =>
            {
                list().Remove(reminder);
                changed();
                Rebuild();
            };
            Add(delete, row, 8);

            // Only the settings that apply to the chosen way of showing it.
            void UpdateEnabled()
            {
                symbol.IsEnabled = reminder.HasMarker;
                size.IsEnabled = reminder.HasMarker;
                dash.IsEnabled = reminder.HasLine;
                width.IsEnabled = reminder.HasLine;
            }
            UpdateEnabled();
            show.SelectionChanged += (s, e) =>
            {
                reminder.Show = (ReminderShow)Math.Max(0, show.SelectedIndex);
                UpdateEnabled();
                Changed();
            };
            symbol.SelectionChanged += (s, e) =>
            {
                reminder.Symbol = MarkerShapes[Math.Max(0, symbol.SelectedIndex)];
                Changed();
            };
            size.SelectionChanged += (s, e) =>
            {
                reminder.Size = (Math.Max(0, size.SelectedIndex) + 2) * 2;
                Changed();
            };
            dash.SelectionChanged += (s, e) =>
            {
                reminder.Dash = (LineDash)Math.Max(0, dash.SelectedIndex);
                Changed();
            };
            width.SelectionChanged += (s, e) =>
            {
                reminder.Width = Math.Max(0, width.SelectedIndex) + 1;
                Changed();
            };
        }

        private static ComboBox Combo(string[] items, int selected, double width)
        {
            return new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = selected,
                Width = width,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        private void Add(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }
}
