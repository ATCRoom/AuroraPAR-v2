using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the Aircraft performance window. A table of the performance classes (a row
    /// each): speeds (minimum, maximum, initial, final), climb and descent, accelerations, bank. A cell is changed by
    /// typing in it, or with the mouse wheel over it (a step). Add class, Duplicate, Remove, Reset to defaults. Save
    /// checks the values (a wrong one is said, and nothing is saved) and gives them back in <see cref="Result"/>.
    /// </summary>
    internal sealed class PerformanceWindow : Window
    {
        /// <summary>A row of the table: a class being changed.</summary>
        private sealed class Row : INotifyPropertyChanged
        {
            private string name = "";
            private double minSpeed, maxSpeed, initialSpeed, finalSpeed, maxClimb, maxDescent, acceleration, deceleration, verticalAcceleration, maxBank;
            public Dictionary<string, JsonElement>? Extra;

            public string Name { get => name; set => Set(ref name, value); }
            public double MinSpeed { get => minSpeed; set => Set(ref minSpeed, value); }
            public double MaxSpeed { get => maxSpeed; set => Set(ref maxSpeed, value); }
            public double InitialSpeed { get => initialSpeed; set => Set(ref initialSpeed, value); }
            public double FinalSpeed { get => finalSpeed; set => Set(ref finalSpeed, value); }
            public double MaxClimb { get => maxClimb; set => Set(ref maxClimb, value); }
            public double MaxDescent { get => maxDescent; set => Set(ref maxDescent, value); }
            public double Acceleration { get => acceleration; set => Set(ref acceleration, value); }
            public double Deceleration { get => deceleration; set => Set(ref deceleration, value); }
            public double VerticalAcceleration { get => verticalAcceleration; set => Set(ref verticalAcceleration, value); }
            public double MaxBank { get => maxBank; set => Set(ref maxBank, value); }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void Set<T>(ref T field, T value, [CallerMemberName] string property = "")
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
            }

            public static Row From(PerformanceClass c) => new()
            {
                Name = c.Name,
                MinSpeed = c.MinSpeedKt,
                MaxSpeed = c.MaxSpeedKt,
                InitialSpeed = c.ApproachSpeedKt,
                FinalSpeed = c.FinalSpeedKt,
                MaxClimb = c.MaxClimbFpm,
                MaxDescent = c.MaxDescentFpm,
                Acceleration = c.AccelerationKtPerSecond,
                Deceleration = c.DecelerationKtPerSecond,
                VerticalAcceleration = c.VerticalAccelerationFpmPerSecond,
                MaxBank = c.MaxBankDegrees,
                Extra = c.Extra == null ? null : new Dictionary<string, JsonElement>(c.Extra)
            };

            public PerformanceClass ToClass() => new()
            {
                Name = Name.Trim(),
                MinSpeedKt = MinSpeed,
                MaxSpeedKt = MaxSpeed,
                ApproachSpeedKt = InitialSpeed,
                FinalSpeedKt = FinalSpeed,
                MaxClimbFpm = MaxClimb,
                MaxDescentFpm = MaxDescent,
                AccelerationKtPerSecond = Acceleration,
                DecelerationKtPerSecond = Deceleration,
                VerticalAccelerationFpmPerSecond = VerticalAcceleration,
                MaxBankDegrees = MaxBank,
                Extra = Extra == null ? null : new Dictionary<string, JsonElement>(Extra)
            };

            public Row Copy() => From(ToClass());
        }

        /// <summary>The columns: property, header, unit, step of the wheel, format, limits (low, high), highlighted.</summary>
        private static readonly (string Property, string Header, string Unit, double Step, string Format, double Low, double High, bool Key)[] Columns =
        [
            (nameof(Row.MinSpeed), "Min speed", "kt", 5, "0", 30, 400, false),
            (nameof(Row.MaxSpeed), "Max speed", "kt", 5, "0", 40, SimEngine.MaxSpeed, false),
            (nameof(Row.InitialSpeed), "Initial speed", "kt (Speed box)", 5, "0", 30, SimEngine.MaxSpeed, true),
            (nameof(Row.FinalSpeed), "Final speed", "kt (Slow dwn)", 5, "0", 30, SimEngine.MaxSpeed, true),
            (nameof(Row.MaxClimb), "Max climb", "ft/min", 100, "0", 200, 8000, false),
            (nameof(Row.MaxDescent), "Max descent", "ft/min", 100, "0", 200, 8000, false),
            (nameof(Row.Acceleration), "Accel.", "kt/s", 0.1, "0.0", 0.2, 10, false),
            (nameof(Row.Deceleration), "Decel.", "kt/s", 0.1, "0.0", 0.2, 10, false),
            (nameof(Row.VerticalAcceleration), "Vert. accel.", "ft/min/s", 50, "0", 50, 2000, false),
            (nameof(Row.MaxBank), "Max bank", "°", 1, "0", 10, 60, false)
        ];

        private static readonly Brush KeyBack = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xD8));

        private readonly ObservableCollection<Row> rows = [];
        private readonly DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24,
            FontSize = 12,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD))
        };
        private readonly TextBlock message = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        /// <summary>The classes saved (null: cancelled).</summary>
        public List<PerformanceClass>? Result { get; private set; }

        public PerformanceWindow(IEnumerable<PerformanceClass> classes)
        {
            Title = "Aircraft performance";
            Width = 1060;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            foreach (PerformanceClass c in classes) rows.Add(Row.From(c));

            DataGridTextColumn nameColumn = new()
            {
                Header = Header("Class", ""),
                Binding = new Binding(nameof(Row.Name)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
                FontWeight = FontWeights.SemiBold
            };
            grid.Columns.Add(nameColumn);
            foreach (var column in Columns)
            {
                Style right = new(typeof(TextBlock));
                right.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                right.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(0, 0, 6, 0)));
                DataGridTextColumn text = new()
                {
                    Header = Header(column.Header, column.Unit),
                    Binding = new Binding(column.Property) { StringFormat = column.Format, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ConverterCulture = CultureInfo.InvariantCulture },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    ElementStyle = right
                };
                if (column.Key)
                {
                    Style cell = new(typeof(DataGridCell));
                    cell.Setters.Add(new Setter(BackgroundProperty, KeyBack));
                    cell.Setters.Add(new Setter(ForegroundProperty, Brushes.Black));
                    text.CellStyle = cell;
                }
                grid.Columns.Add(text);
            }
            grid.ItemsSource = rows;
            grid.PreviewMouseWheel += Wheel;
            grid.CellEditEnding += (s, e) => message.Text = "";

            StackPanel panel = new() { Margin = new Thickness(12) };
            panel.Children.Add(grid);
            panel.Children.Add(message);
            DockPanel edit = new() { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            Button add = Small("Add class");
            add.ToolTip = "A new class (with the values of the Jet), at the end.";
            add.Click += (s, e) => AddRow(Row.From(PerformanceClass.Defaults()[2]), "New class");
            Button duplicate = Small("Duplicate");
            duplicate.ToolTip = "A copy of the selected class, to change it (e.g. a slower Jet).";
            duplicate.Click += (s, e) =>
            {
                if (grid.SelectedItem is Row selected) AddRow(selected.Copy(), selected.Name.Trim() + " 2");
                else message.Text = "Select a class to duplicate.";
            };
            Button remove = Small("Remove");
            remove.ToolTip = "Removes the selected class (flight plans of this class get the Jet).";
            remove.Click += (s, e) =>
            {
                if (grid.SelectedItem is not Row selected) message.Text = "Select a class to remove.";
                else if (rows.Count == 1) message.Text = "At least one class is needed.";
                else rows.Remove(selected);
            };
            Button reset = Small("Reset to defaults");
            reset.ToolTip = "Light, Turboprop and Jet with their first values (the other classes are removed).";
            reset.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Put back Light, Turboprop and Jet with their first values? The other classes are removed.", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                rows.Clear();
                foreach (PerformanceClass c in PerformanceClass.Defaults()) rows.Add(Row.From(c));
                message.Text = "";
            };
            edit.Children.Add(add);
            edit.Children.Add(duplicate);
            edit.Children.Add(remove);
            DockPanel.SetDock(reset, Dock.Right);
            reset.Margin = new Thickness(0);
            edit.Children.Add(reset);
            panel.Children.Add(edit);
            panel.Children.Add(new TextBlock
            {
                Text = "Initial speed: put in the Speed box when the class is chosen (Quick, the first point of a new flight plan). Final speed: the speed of \"Slow dwn to final\". "
                    + "Type in a cell, or turn the mouse wheel over it. Saved in performance.json beside the program: the new values are for the aircraft created from now on; those flying keep theirs.",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });
            StackPanel end = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button save = Small("Save");
            save.FontWeight = FontWeights.SemiBold;
            save.IsDefault = true;
            save.Click += (s, e) => Save();
            Button cancel = Small("Cancel");
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0);
            end.Children.Add(save);
            end.Children.Add(cancel);
            panel.Children.Add(end);
            Content = panel;
        }

        private static Button Small(string text) => new() { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };

        private static TextBlock Header(string name, string unit)
        {
            TextBlock header = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            header.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold });
            if (unit.Length > 0)
            {
                header.Inlines.Add(new System.Windows.Documents.LineBreak());
                header.Inlines.Add(new System.Windows.Documents.Run(unit) { FontSize = 10, Foreground = Brushes.DimGray });
            }
            return header;
        }

        private void AddRow(Row row, string name)
        {
            string unique = name;
            for (int i = 2; rows.Any(r => string.Equals(r.Name.Trim(), unique, StringComparison.OrdinalIgnoreCase)); i++) unique = $"{name} {i}";
            row.Name = unique;
            rows.Add(row);
            grid.SelectedItem = row;
            grid.ScrollIntoView(row);
            message.Text = "";
        }

        /// <summary>The wheel over a cell (not while it is being typed in): one step up or down.</summary>
        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            DependencyObject? at = e.OriginalSource as DependencyObject;
            while (at != null && at is not DataGridCell) at = VisualTreeHelper.GetParent(at);
            if (at is not DataGridCell cell || cell.IsEditing || cell.DataContext is not Row row) return;
            int index = grid.Columns.IndexOf(cell.Column) - 1;
            if (index < 0 || index >= Columns.Length) return;
            var column = Columns[index];
            PropertyInfo? property = typeof(Row).GetProperty(column.Property);
            if (property?.GetValue(row) is not double value) return;
            double step = column.Step * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1);
            double next = Math.Round((value + (e.Delta > 0 ? step : -step)) / column.Step) * column.Step;
            property.SetValue(row, Math.Clamp(Math.Round(next, 3), column.Low, column.High));
            message.Text = "";
            e.Handled = true;
        }

        /// <summary>Save: the values checked (the first wrong one is said, the row selected), then given back.</summary>
        private void Save()
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                message.Text = "A value is not a number: correct it (or press Esc in the cell) before saving.";
                return;
            }
            string? Problem(Row row)
            {
                string name = row.Name.Trim();
                if (name.Length == 0) return "A class has no name.";
                if (rows.Count(r => string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) > 1) return $"Two classes are named {name}.";
                foreach (var column in Columns)
                {
                    double value = (double)typeof(Row).GetProperty(column.Property)!.GetValue(row)!;
                    if (!double.IsFinite(value) || value < column.Low || value > column.High)
                    {
                        return string.Format(CultureInfo.InvariantCulture, "{0}: {1} must be from {2} to {3} {4}.", name, column.Header.ToLowerInvariant(), column.Low, column.High, column.Unit.Split(' ')[0]);
                    }
                }
                if (row.MaxSpeed < row.MinSpeed + 10) return $"{name}: the max speed must be at least 10 kt more than the min speed.";
                if (row.InitialSpeed < row.MinSpeed || row.InitialSpeed > row.MaxSpeed)
                    return $"{name}: the initial speed ({row.InitialSpeed:0} kt) must be between the min speed ({row.MinSpeed:0} kt) and the max speed ({row.MaxSpeed:0} kt).";
                if (row.FinalSpeed < row.MinSpeed || row.FinalSpeed > row.InitialSpeed)
                    return $"{name}: the final speed ({row.FinalSpeed:0} kt) must be between the min speed ({row.MinSpeed:0} kt) and the initial speed ({row.InitialSpeed:0} kt).";
                return null;
            }
            if (rows.Count == 0)
            {
                message.Text = "At least one class is needed.";
                return;
            }
            foreach (Row row in rows)
            {
                if (Problem(row) is string problem)
                {
                    message.Text = problem;
                    grid.SelectedItem = row;
                    grid.ScrollIntoView(row);
                    return;
                }
            }
            Result = rows.Select(r => r.ToClass()).ToList();
            DialogResult = true;
        }
    }
}
