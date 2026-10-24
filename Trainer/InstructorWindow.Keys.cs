using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: keys for the commands of the selected aircraft, also for a macro keyboard
    /// (a few keys and a knob that send keys). Each command can have one key, assigned in the Keys window by pressing
    /// it; the steps of the heading and rate keys are chosen there too. The keys work while the Instructor+ window is
    /// in front, also while typing in a field (choose keys that are not used for typing: F13–F24 by default).
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private static readonly (string Id, string Text)[] KeyCommands =
        [
            ("HeadingLeft", "Heading left (step)"),
            ("HeadingRight", "Heading right (step)"),
            ("FinalCourse", "Final CRS"),
            ("RateUp", "Rate UP (step)"),
            ("RateDown", "Rate DN (step)"),
            ("NormalRate", "Normal rate"),
            ("StopTurn", "Stop turn"),
            ("StopVertical", "Stop climb/descent"),
            ("Navigation", "MANUAL / ROUTE"),
            ("NextWaypoint", "Next WPT"),
            ("SlowToFinal", "Slow dwn to final"),
            ("NextAircraft", "Next aircraft"),
            ("Pause", "PAUSE ALL / RESUME ALL")
        ];

        /// <summary>Keys at the start: for a macro keyboard of 6 keys and a knob (knob: heading and Final CRS).</summary>
        private static Dictionary<string, string> DefaultKeys() => new()
        {
            ["HeadingLeft"] = nameof(Key.F19),
            ["HeadingRight"] = nameof(Key.F20),
            ["FinalCourse"] = nameof(Key.F21),
            ["RateUp"] = nameof(Key.F13),
            ["RateDown"] = nameof(Key.F14),
            ["NormalRate"] = nameof(Key.F15),
            ["Navigation"] = nameof(Key.F16),
            ["NextWaypoint"] = nameof(Key.F17),
            ["NextAircraft"] = nameof(Key.F18)
        };

        private Dictionary<string, string> KeyMap
        {
            get
            {
                settings.Keys ??= DefaultKeys();
                // Written by hand in the settings: empty keys left out.
                foreach (string id in settings.Keys.Where(k => string.IsNullOrWhiteSpace(k.Value)).Select(k => k.Key).ToList()) settings.Keys.Remove(id);
                return settings.Keys;
            }
        }

        private static Key PressedKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

        /// <summary>A key in the Instructor+ window: the command it is assigned to, if any.</summary>
        private void TrainerKey(object sender, KeyEventArgs e)
        {
            Key key = PressedKey(e);
            if (key == Key.None) return;
            // N / Shift+N (fixed, not assigned): the next / previous aircraft, only while not typing in a field.
            if (key == Key.N && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift) && !Typing()
                && !KeyMap.Values.Any(v => string.Equals(v, nameof(Key.N), StringComparison.OrdinalIgnoreCase)))
            {
                StepAircraft(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
                e.Handled = true;
                return;
            }
            // Keys held with Ctrl, Alt or Shift are not the assigned ones.
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            string name = key.ToString();
            string? command = KeyMap.FirstOrDefault(k => string.Equals(k.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
            if (command == null) return;
            RunKeyCommand(command);
            e.Handled = true;
        }

        /// <summary>The keyboard is in a field where one types (a box, an editable list).</summary>
        private static bool Typing() =>
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

        /// <summary>The next (+1) or previous (−1) aircraft of the TRAFFIC LIST (after the last, the first).</summary>
        private void StepAircraft(int step)
        {
            int count = list.Items.Count;
            if (count == 0) return;
            list.SelectedIndex = list.SelectedIndex < 0 ? (step > 0 ? 0 : count - 1) : ((list.SelectedIndex + step) % count + count) % count;
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
        }

        private void RunKeyCommand(string command)
        {
            int headingStep = (int)Math.Clamp(Math.Round(settings.HeadingKeyStep), 1, 10);
            double rateStep = Math.Clamp(settings.RateKeyStep, 10, 500);
            switch (command)
            {
                case "HeadingLeft": StepHeading(-headingStep); break;
                case "HeadingRight": StepHeading(headingStep); break;
                case "FinalCourse": FinalCourse(); break;
                case "RateUp": StepRate(rateStep); break;
                case "RateDown": StepRate(-rateStep); break;
                case "NormalRate": NormalRate(); break;
                case "StopTurn": StopTurn(); break;
                case "StopVertical": StopVertical(); break;
                case "Navigation": ToggleNavigation(); break;
                case "NextWaypoint": NextWaypoint(); break;
                case "SlowToFinal": SlowToFinal(); break;
                case "NextAircraft": StepAircraft(1); break;
                case "Pause": TogglePause(); break;
            }
        }

        private Window? keysWindow;

        /// <summary>The Keys window: a key for each command (Assign, then press it), and the steps.</summary>
        private void OpenKeysWindow()
        {
            if (keysWindow != null)
            {
                keysWindow.Activate();
                return;
            }
            string? assigning = null;
            Dictionary<string, TextBlock> shown = [];
            StackPanel panel = new() { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock
            {
                Text = "Keys for the selected aircraft, also for a macro keyboard. Click Assign, then press the key (or turn the knob). They work while the Instructor+ window is in front, also while typing in a field: choose keys that are not used for typing (F13–F24 are free on normal keyboards). Fixed: N = next aircraft, Shift+N = previous (not while typing in a field).",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            void ShowKeys()
            {
                foreach ((string id, _) in KeyCommands)
                {
                    shown[id].Text = assigning == id ? "press a key… (Esc: cancel)" : KeyMap.TryGetValue(id, out string? key) && key.Length > 0 ? key : "—";
                }
            }
            for (int i = 0; i < KeyCommands.Length; i++)
            {
                (string id, string text) = KeyCommands[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = new() { Text = text, VerticalAlignment = VerticalAlignment.Center };
                TextBlock key = new() { FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
                Border keyBorder = new() { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 2, 6, 2), Child = key };
                shown[id] = key;
                Button assign = new() { Content = "Assign", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 4, 2) };
                Button clear = new() { Content = "Clear", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 0, 2) };
                assign.Click += (s, e) => { assigning = id; ShowKeys(); };
                clear.Click += (s, e) =>
                {
                    KeyMap.Remove(id);
                    save();
                    assigning = null;
                    ShowKeys();
                };
                StackPanel buttons = new() { Orientation = Orientation.Horizontal };
                buttons.Children.Add(assign);
                buttons.Children.Add(clear);
                Grid.SetRow(label, i);
                Grid.SetRow(keyBorder, i);
                Grid.SetColumn(keyBorder, 1);
                Grid.SetRow(buttons, i);
                Grid.SetColumn(buttons, 2);
                grid.Children.Add(label);
                grid.Children.Add(keyBorder);
                grid.Children.Add(buttons);
            }
            panel.Children.Add(grid);

            TextBox headingStep = NumberBox(settings.HeadingKeyStep.ToString("0", CultureInfo.InvariantCulture));
            TextBox rateStep = NumberBox(settings.RateKeyStep.ToString("0", CultureInfo.InvariantCulture));
            headingStep.ToolTip = "Degrees of heading for each press of the heading keys (or each click of the knob): 1 to 10. The ◀ L / R ▶ buttons of the window stay at 1°.";
            rateStep.ToolTip = "Feet per minute of rate for each press of the rate keys: 10 to 500.";
            void ApplySteps()
            {
                if (TryRead(headingStep, out double degrees) && degrees >= 1 && degrees <= 10) settings.HeadingKeyStep = Math.Round(degrees);
                if (TryRead(rateStep, out double feet) && feet >= 10 && feet <= 500) settings.RateKeyStep = Math.Round(feet / 10) * 10;
                headingStep.Text = settings.HeadingKeyStep.ToString("0", CultureInfo.InvariantCulture);
                rateStep.Text = settings.RateKeyStep.ToString("0", CultureInfo.InvariantCulture);
                save();
            }
            foreach (TextBox box in new[] { headingStep, rateStep })
            {
                box.LostFocus += (s, e) => ApplySteps();
                box.KeyDown += (s, e) => { if (e.Key == Key.Enter) ApplySteps(); };
            }
            WrapPanel steps = new() { Margin = new Thickness(0, 8, 0, 0) };
            steps.Children.Add(Caption("Heading step"));
            steps.Children.Add(headingStep);
            steps.Children.Add(Caption("°    Rate step"));
            steps.Children.Add(rateStep);
            steps.Children.Add(Caption("ft/min"));
            panel.Children.Add(steps);

            DockPanel bottom = new() { Margin = new Thickness(0, 10, 0, 0) };
            Button defaults = new() { Content = "Defaults", Padding = new Thickness(10, 2, 10, 2) };
            Button close = new() { Content = "Close", Padding = new Thickness(14, 2, 14, 2), FontWeight = FontWeights.SemiBold, IsCancel = true };
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            bottom.Children.Add(defaults);
            defaults.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(bottom);

            Window window = new()
            {
                Title = "Instructor+ keys",
                Owner = this,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel { Width = 470, Children = { panel } }
            };
            defaults.Click += (s, e) =>
            {
                settings.Keys = DefaultKeys();
                settings.HeadingKeyStep = 5;
                settings.RateKeyStep = 100;
                headingStep.Text = "5";
                rateStep.Text = "100";
                assigning = null;
                save();
                ShowKeys();
            };
            close.Click += (s, e) => window.Close();
            window.PreviewKeyDown += (s, e) =>
            {
                if (assigning == null) return;
                Key key = PressedKey(e);
                e.Handled = true;
                if (key == Key.Escape)
                {
                    assigning = null;
                    ShowKeys();
                    return;
                }
                // Keys that only change other keys are not taken alone, and keys held with them are not taken (they
                // would then work alone too).
                if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None) return;
                if (Keyboard.Modifiers != ModifierKeys.None)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                string name = key.ToString();
                // One key for one command.
                foreach (string other in KeyMap.Where(k => string.Equals(k.Value, name, StringComparison.OrdinalIgnoreCase)).Select(k => k.Key).ToList()) KeyMap.Remove(other);
                KeyMap[assigning] = name;
                assigning = null;
                save();
                ShowKeys();
            };
            window.Closed += (s, e) => keysWindow = null;
            ShowKeys();
            keysWindow = window;
            window.Show();
        }
    }
}
