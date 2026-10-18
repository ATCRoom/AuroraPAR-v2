using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// Window of the Instructor (key I, after turning it on in the Settings). PROVISIONAL (step 2 of the plan): the
    /// reference runway, new aircraft on the final, the list of the simulated aircraft, and heading / altitude /
    /// speed commands for the selected one. The plan view, the PAR monitor and the full commands come in the next
    /// steps. Closing the window removes the simulated aircraft.
    /// </summary>
    internal sealed class InstructorWindow : Window
    {
        private static readonly Brush ReadoutBack = Frozen(Color.FromRgb(0x1B, 0x1C, 0x19));
        private static readonly Brush ReadoutText = Frozen(Color.FromRgb(0xFF, 0xB8, 0x40));
        private static readonly double[] VerticalRates = [500, 700, 1000, 1500];

        private readonly InstructorBridge bridge;
        private readonly InstructorSettings settings;
        private readonly Func<Runway> radarRunway;
        private readonly Func<double> variation;
        private readonly Func<IEnumerable<string>> otherCallsigns;
        private readonly Func<Aircraft, bool> isSeen;
        private readonly Action save;

        private readonly TextBlock referenceText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        private readonly Button pauseButton = SmallButton("Pause");
        private readonly RadioButton qnhRadio = new() { Content = "QNH", GroupName = "InstructorPressure", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0), ToolTip = "Altitudes above mean sea level, as on the radar with QNH." };
        private readonly RadioButton qfeRadio = new() { Content = "QFE", GroupName = "InstructorPressure", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Heights above the threshold, as on the radar with QFE." };
        private readonly CheckBox auroraCheck = new() { Content = "Aurora-like data", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Positions every about 0.5 s and the altitude in steps every 3 s, as the real data from Aurora (the student sees realistic tracks). Off: perfect positions." };
        private readonly TextBox prefixBox = new() { Width = 54, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Start of the callsigns given automatically: 1 to 6 letters or digits (SIM → SIM1, SIM2...)." };

        private readonly TextBox distanceBox = NumberBox("10");
        private readonly TextBox offsetBox = NumberBox("0");
        private readonly TextBox newAltitudeBox = NumberBox("");
        private readonly TextBox newSpeedBox = NumberBox("140");
        private readonly TextBox newCallsignBox = new() { Width = 70, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Empty: the next free callsign of the prefix." };

        private readonly ListBox list = new() { Height = 130, FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        private readonly TextBlock nowText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, Foreground = ReadoutText, Background = ReadoutBack, Padding = new Thickness(6, 4, 6, 4), TextWrapping = TextWrapping.Wrap };
        private readonly TextBox headingBox = NumberBox("");
        private readonly RadioButton halfRate = new() { Content = "1.5°/s", GroupName = "InstructorTurn", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
        private readonly RadioButton fullRate = new() { Content = "3°/s", GroupName = "InstructorTurn", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        private readonly TextBox altitudeBox = NumberBox("");
        private readonly ComboBox rateBox = new() { Width = 70, Margin = new Thickness(4, 0, 4, 0), ItemsSource = VerticalRates.Select(r => r.ToString("0", CultureInfo.InvariantCulture)).ToArray(), SelectedIndex = 2, ToolTip = "Vertical rate to reach the altitude (ft/min)." };
        private readonly TextBlock altitudeUnit = Caption("ft QNH");
        private readonly TextBox speedBox = NumberBox("");
        private readonly CheckBox freezeCheck = new() { Content = "Freeze", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Holds the aircraft where it is (to explain a situation to the student); it keeps its commands." };
        private readonly TextBox renameBox = new() { Width = 70, Height = 22, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Panel selectedPanel;

        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(0.25) };
        /// <summary>The fields of the selected aircraft are filled with its commanded values at the next refresh.</summary>
        private bool fillFields = true;
        private bool updating;

        public InstructorWindow(InstructorBridge bridge, InstructorSettings settings, bool qfe, Func<Runway> radarRunway, Func<double> variation,
            Func<IEnumerable<string>> otherCallsigns, Func<Aircraft, bool> isSeen, Action save)
        {
            this.bridge = bridge;
            this.settings = settings;
            this.radarRunway = radarRunway;
            this.variation = variation;
            this.otherCallsigns = otherCallsigns;
            this.isSeen = isSeen;
            this.save = save;
            Title = "Aurora PAR - Instructor";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.CanMinimize;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.Manual;

            StackPanel root = new() { Margin = new Thickness(10), Width = 520 };

            // Reference runway and general settings.
            StackPanel referenceRow = new() { Orientation = Orientation.Horizontal };
            referenceRow.Children.Add(Caption("Runway"));
            referenceRow.Children.Add(referenceText);
            Button useRadar = SmallButton("Use the radar's runway");
            useRadar.Margin = new Thickness(0);
            useRadar.ToolTip = "Takes the runway now shown on the radar as the reference of the simulation (with the heading of the runway file). The simulated aircraft are removed.";
            useRadar.Click += (s, e) => UseRadarRunway();
            referenceRow.Children.Add(useRadar);
            WrapPanel optionsRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            pauseButton.Margin = new Thickness(0, 0, 6, 0);
            pauseButton.Click += (s, e) =>
            {
                bridge.Engine.Paused = !bridge.Engine.Paused;
                pauseButton.Content = bridge.Engine.Paused ? "Resume" : "Pause";
            };
            optionsRow.Children.Add(pauseButton);
            optionsRow.Children.Add(qnhRadio);
            optionsRow.Children.Add(qfeRadio);
            optionsRow.Children.Add(auroraCheck);
            optionsRow.Children.Add(Caption("   Callsign prefix"));
            optionsRow.Children.Add(prefixBox);
            StackPanel generalPanel = new();
            generalPanel.Children.Add(referenceRow);
            generalPanel.Children.Add(optionsRow);
            root.Children.Add(Group("Simulation", generalPanel));
            qnhRadio.IsChecked = !qfe;
            qfeRadio.IsChecked = qfe;
            qnhRadio.Checked += (s, e) => { fillFields = true; Refresh(); };
            qfeRadio.Checked += (s, e) => { fillFields = true; Refresh(); };
            auroraCheck.IsChecked = settings.AuroraLike;
            bridge.AuroraLike = settings.AuroraLike;
            auroraCheck.Checked += (s, e) => SetAuroraLike(true);
            auroraCheck.Unchecked += (s, e) => SetAuroraLike(false);
            prefixBox.Text = settings.CallsignPrefix;
            try { bridge.Engine.CallsignPrefix = settings.CallsignPrefix; } catch (ArgumentException) { }
            prefixBox.LostFocus += (s, e) => ApplyPrefix();
            prefixBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) ApplyPrefix(); };

            // New aircraft on the final.
            WrapPanel newRow = new();
            newRow.Children.Add(Caption("Distance"));
            newRow.Children.Add(distanceBox);
            newRow.Children.Add(Caption("NM  offset"));
            newRow.Children.Add(offsetBox);
            newRow.Children.Add(Caption("NM (+R/−L)  altitude"));
            newRow.Children.Add(newAltitudeBox);
            newRow.Children.Add(Caption("ft"));
            WrapPanel newRow2 = new() { Margin = new Thickness(0, 6, 0, 0) };
            newRow2.Children.Add(Caption("Speed"));
            newRow2.Children.Add(newSpeedBox);
            newRow2.Children.Add(Caption("kt  callsign"));
            newRow2.Children.Add(newCallsignBox);
            Button add = new() { Content = "New aircraft on final", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), FontWeight = FontWeights.SemiBold };
            add.Click += (s, e) => AddOnFinal();
            newRow2.Children.Add(add);
            newAltitudeBox.ToolTip = "Empty: on the glide path at that distance. With QFE selected: height above the threshold.";
            offsetBox.ToolTip = "Offset from the extended centreline, to the right (+) or left (−) as seen by the pilot.";
            StackPanel newPanel = new();
            newPanel.Children.Add(newRow);
            newPanel.Children.Add(newRow2);
            root.Children.Add(Group("New aircraft on final (heading of the runway)", newPanel));

            // Aircraft.
            StackPanel aircraftPanel = new();
            aircraftPanel.Children.Add(new TextBlock { Text = "Callsign  HDG  ALT/HGT ft   SPD    NM  PAR", FontFamily = new FontFamily("Consolas"), FontSize = 13, Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 2) });
            aircraftPanel.Children.Add(list);
            StackPanel removeRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            Button remove = SmallButton("Remove");
            remove.Click += (s, e) => { if (SelectedCallsign is string callsign) bridge.Engine.Remove(callsign); Refresh(); };
            Button removeAll = SmallButton("Remove all");
            removeAll.Click += (s, e) => { bridge.Clear(); Refresh(); };
            removeRow.Children.Add(remove);
            removeRow.Children.Add(removeAll);
            aircraftPanel.Children.Add(removeRow);
            root.Children.Add(Group("Aircraft", aircraftPanel));
            list.SelectionChanged += (s, e) =>
            {
                if (updating) return;
                fillFields = true;
                Refresh();
            };

            // Selected aircraft.
            StackPanel selected = new();
            selected.Children.Add(nowText);
            WrapPanel headingRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            headingRow.Children.Add(Caption("Heading"));
            headingRow.Children.Add(headingBox);
            headingRow.Children.Add(Caption("°M"));
            Button left = SmallButton("◀ Left");
            left.Click += (s, e) => SetHeading(TurnDirection.Left);
            Button shortest = SmallButton("Turn");
            shortest.ToolTip = "Turns to the heading the shorter way (also Enter in the heading box).";
            shortest.Click += (s, e) => SetHeading(TurnDirection.Shortest);
            Button right = SmallButton("Right ▶");
            right.Click += (s, e) => SetHeading(TurnDirection.Right);
            foreach (Button button in new[] { left, shortest, right }) { button.Margin = new Thickness(0, 0, 4, 0); headingRow.Children.Add(button); }
            headingRow.Children.Add(Caption("  rate"));
            headingRow.Children.Add(halfRate);
            headingRow.Children.Add(fullRate);
            halfRate.Checked += (s, e) => SetTurnRate(1.5);
            fullRate.Checked += (s, e) => SetTurnRate(3);
            headingBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) SetHeading(TurnDirection.Shortest); };
            selected.Children.Add(headingRow);

            WrapPanel altitudeRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            altitudeRow.Children.Add(Caption("Altitude"));
            altitudeRow.Children.Add(altitudeBox);
            altitudeRow.Children.Add(altitudeUnit);
            altitudeRow.Children.Add(Caption("at"));
            altitudeRow.Children.Add(rateBox);
            altitudeRow.Children.Add(Caption("ft/min"));
            Button setAltitude = SmallButton("Set");
            setAltitude.Margin = new Thickness(6, 0, 0, 0);
            setAltitude.Click += (s, e) => SetAltitude();
            altitudeRow.Children.Add(setAltitude);
            altitudeBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) SetAltitude(); };
            selected.Children.Add(altitudeRow);

            WrapPanel speedRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            speedRow.Children.Add(Caption("Speed"));
            speedRow.Children.Add(speedBox);
            speedRow.Children.Add(Caption("kt"));
            Button setSpeed = SmallButton("Set");
            setSpeed.Margin = new Thickness(6, 0, 16, 0);
            setSpeed.Click += (s, e) => SetSpeed();
            speedBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) SetSpeed(); };
            speedRow.Children.Add(setSpeed);
            speedRow.Children.Add(freezeCheck);
            freezeCheck.Checked += (s, e) => SetFrozen(true);
            freezeCheck.Unchecked += (s, e) => SetFrozen(false);
            speedRow.Children.Add(Caption("Callsign"));
            speedRow.Children.Add(renameBox);
            Button rename = SmallButton("Rename");
            rename.Margin = new Thickness(4, 0, 0, 0);
            rename.Click += (s, e) => Rename();
            renameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) Rename(); };
            speedRow.Children.Add(rename);
            selected.Children.Add(speedRow);
            selectedPanel = selected;
            root.Children.Add(Group("Selected aircraft", selected));

            root.Children.Add(new TextBlock
            {
                Text = "Provisional window: the plan view, the PAR monitor and the full commands (modes, go-around, start positions) come in the next steps. Commands are carried out at once (no pilot reaction yet). Closing the window removes the simulated aircraft.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(2, 8, 2, 0)
            });

            Content = root;
            if (bridge.Reference == null) bridge.SetReference(radarRunway());
            refresh.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { refresh.Start(); Refresh(); };
            Closed += (s, e) => refresh.Stop();
            Refresh();
        }

        /// <summary>Callsign of the selected aircraft, or null.</summary>
        private string? SelectedCallsign => (list.SelectedItem as ListBoxItem)?.Tag as string;

        private bool Qfe => qfeRadio.IsChecked == true;

        private double Elevation => bridge.Reference?.Elevation ?? 0;

        private void SetAuroraLike(bool on)
        {
            bridge.AuroraLike = on;
            settings.AuroraLike = on;
            save();
        }

        private void ApplyPrefix()
        {
            string text = prefixBox.Text.Trim().ToUpperInvariant();
            try
            {
                bridge.Engine.CallsignPrefix = text;
                settings.CallsignPrefix = bridge.Engine.CallsignPrefix;
                save();
            }
            catch (ArgumentException)
            {
                // Invalid: back to the one in use.
            }
            prefixBox.Text = bridge.Engine.CallsignPrefix;
        }

        private void UseRadarRunway()
        {
            Runway runway = radarRunway();
            if (bridge.Count > 0 && MessageBox.Show(this, $"Use {runway.DisplayName} as the reference runway? The simulated aircraft are removed.",
                "Instructor", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            bridge.SetReference(runway);
            fillFields = true;
            Refresh();
        }

        private void AddOnFinal()
        {
            Runway? runway = bridge.Reference;
            if (runway == null) return;
            if (!TryRead(distanceBox, out double distance) || distance < 0.5 || distance > 60)
            {
                Warn("Distance: 0.5 to 60 NM from touchdown.");
                return;
            }
            double offset = TryRead(offsetBox, out double o) ? o : 0;
            if (Math.Abs(offset) > 20)
            {
                Warn("Offset: at most 20 NM.");
                return;
            }
            if (!TryRead(newSpeedBox, out double speed) || speed < 40 || speed > 400)
            {
                Warn("Speed: 40 to 400 kt.");
                return;
            }
            double altitude;
            if (string.IsNullOrWhiteSpace(newAltitudeBox.Text))
            {
                altitude = runway.Elevation + runway.GlidePathHeight(distance);
            }
            else if (TryRead(newAltitudeBox, out double typed))
            {
                altitude = Qfe ? typed + runway.Elevation : typed;
            }
            else
            {
                Warn("Altitude: a number, or empty for the glide path.");
                return;
            }
            string? callsign = string.IsNullOrWhiteSpace(newCallsignBox.Text) ? null : newCallsignBox.Text;
            try
            {
                (double x, double y) = bridge.OnFinal(distance, offset);
                string name = bridge.Add(x, y, altitude, runway.Heading, speed, callsign, otherCallsigns());
                newCallsignBox.Text = "";
                Refresh();
                Select(name);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Warn(ex.Message);
            }
        }

        private void SetHeading(TurnDirection direction)
        {
            if (SelectedCallsign is not string callsign) return;
            if (!TryRead(headingBox, out double magnetic) || magnetic < 0 || magnetic > 360)
            {
                Warn("Heading: 0 to 360 (magnetic).");
                return;
            }
            bridge.Engine.SetHeading(callsign, SimEngine.Normalize(magnetic + variation()), direction);
        }

        private void SetTurnRate(double rate)
        {
            if (updating || SelectedCallsign is not string callsign) return;
            bridge.Engine.SetTurnRate(callsign, rate);
        }

        private void SetAltitude()
        {
            if (SelectedCallsign is not string callsign) return;
            if (!TryRead(altitudeBox, out double value) || value < (Qfe ? -Elevation : 0) || value > 40000)
            {
                Warn("Altitude: 0 to 40000 ft.");
                return;
            }
            double rate = VerticalRates[Math.Clamp(rateBox.SelectedIndex, 0, VerticalRates.Length - 1)];
            bridge.Engine.SetAltitude(callsign, Qfe ? value + Elevation : value, rate);
        }

        private void SetSpeed()
        {
            if (SelectedCallsign is not string callsign) return;
            if (!TryRead(speedBox, out double speed) || speed < 40 || speed > 400)
            {
                Warn("Speed: 40 to 400 kt.");
                return;
            }
            bridge.Engine.SetSpeed(callsign, speed);
        }

        private void SetFrozen(bool frozen)
        {
            if (updating || SelectedCallsign is not string callsign) return;
            bridge.Engine.SetFrozen(callsign, frozen);
        }

        private void Rename()
        {
            if (SelectedCallsign is not string callsign) return;
            string name = renameBox.Text.Trim().ToUpperInvariant();
            if (name == callsign) return;
            if (otherCallsigns().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Warn($"{name} is already flying (real or test traffic).");
                return;
            }
            try
            {
                if (!bridge.Engine.Rename(callsign, name))
                {
                    Warn($"{name} is already in the simulation.");
                    return;
                }
            }
            catch (ArgumentException ex)
            {
                Warn(ex.Message);
                return;
            }
            Refresh();
            Select(name);
        }

        private void Select(string callsign)
        {
            ListBoxItem? item = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, callsign, StringComparison.OrdinalIgnoreCase));
            if (item != null) list.SelectedItem = item;
        }

        /// <summary>Updates the reference text, the list (keeping the selection) and the selected aircraft.</summary>
        private void Refresh()
        {
            Runway? runway = bridge.Reference;
            double magneticVariation = variation();
            referenceText.Text = runway == null ? "none" : $"{runway.DisplayName}  (elevation {runway.Elevation:0} ft)";
            altitudeUnit.Text = Qfe ? "ft QFE" : "ft QNH";
            IReadOnlyList<AircraftState> states = bridge.Engine.Snapshot();
            updating = true;
            try
            {
                string? selected = SelectedCallsign;
                // Items by callsign: changed in place, so the selection stays.
                List<ListBoxItem> items = list.Items.OfType<ListBoxItem>().ToList();
                foreach (ListBoxItem item in items.Where(i => !states.Any(s => string.Equals(s.Callsign, i.Tag as string, StringComparison.OrdinalIgnoreCase))))
                {
                    list.Items.Remove(item);
                }
                foreach (AircraftState state in states)
                {
                    ListBoxItem? item = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, state.Callsign, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                    {
                        item = new ListBoxItem { Tag = state.Callsign };
                        list.Items.Add(item);
                    }
                    Aircraft? position = bridge.LastPosition(state.Callsign);
                    string distance = position != null && runway != null ? position.DistanceFromTouchdown(runway).ToString("0.0", CultureInfo.InvariantCulture) : "--";
                    string seen = position != null && isSeen(position) ? "●" : "○";
                    double altitude = Qfe ? state.AltitudeFt - Elevation : state.AltitudeFt;
                    string climbing = state.VerticalSpeedFpm > 0 ? "↑" : state.VerticalSpeedFpm < 0 ? "↓" : " ";
                    item.Content = string.Format(CultureInfo.InvariantCulture, "{0,-8}  {1:000}  {2,6:0}{3}  {4,4:0}  {5,5}  {6}{7}",
                        state.Callsign, Magnetic(state.HeadingTrue, magneticVariation), altitude, climbing, state.SpeedKt, distance, seen, state.Frozen ? "  FROZEN" : "");
                }
                if (list.SelectedItem == null && list.Items.Count > 0 && selected == null) list.SelectedIndex = 0;
            }
            finally
            {
                updating = false;
            }
            ShowSelected(states, magneticVariation);
        }

        private void ShowSelected(IReadOnlyList<AircraftState> states, double magneticVariation)
        {
            AircraftState? state = states.FirstOrDefault(s => string.Equals(s.Callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase));
            selectedPanel.IsEnabled = state != null;
            if (state == null)
            {
                nowText.Text = "No aircraft selected.";
                return;
            }
            double altitude = Qfe ? state.AltitudeFt - Elevation : state.AltitudeFt;
            double target = Qfe ? state.TargetAltitudeFt - Elevation : state.TargetAltitudeFt;
            string turn = state.TurnRateDps == 0 ? "" : $"  turning {(state.TurnRateDps > 0 ? "R" : "L")} {Math.Abs(state.TurnRateDps):0.0}°/s";
            nowText.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}{1}\nHDG {2:000}°M → {3:000}{4}\nALT {5:0} → {6:0} ft {7}  VS {8:+0;-0;0} ft/min\nSPD {9:0} → {10:0} kt",
                state.Callsign, state.Frozen ? "  (frozen)" : "",
                Magnetic(state.HeadingTrue, magneticVariation), Magnetic(state.TargetHeading, magneticVariation), turn,
                altitude, target, Qfe ? "QFE" : "QNH", state.VerticalSpeedFpm,
                state.SpeedKt, state.TargetSpeedKt);
            if (fillFields)
            {
                fillFields = false;
                updating = true;
                try
                {
                    headingBox.Text = Magnetic(state.TargetHeading, magneticVariation).ToString("000", CultureInfo.InvariantCulture);
                    altitudeBox.Text = target.ToString("0", CultureInfo.InvariantCulture);
                    speedBox.Text = state.TargetSpeedKt.ToString("0", CultureInfo.InvariantCulture);
                    renameBox.Text = state.Callsign;
                    freezeCheck.IsChecked = state.Frozen;
                    halfRate.IsChecked = state.SelectedTurnRateDps < 2.25;
                    fullRate.IsChecked = state.SelectedTurnRateDps >= 2.25;
                    int rate = Array.FindIndex(VerticalRates, r => Math.Abs(r - state.SelectedVerticalRateFpm) < 1);
                    if (rate >= 0) rateBox.SelectedIndex = rate;
                }
                finally
                {
                    updating = false;
                }
            }
        }

        /// <summary>Magnetic heading, 1–360 (as on a compass: 360, never 000).</summary>
        private static int Magnetic(double trueHeading, double magneticVariation)
        {
            int degrees = (int)Math.Round(SimEngine.Normalize(trueHeading - magneticVariation), MidpointRounding.AwayFromZero) % 360;
            return degrees == 0 ? 360 : degrees;
        }

        private void Warn(string text) => MessageBox.Show(this, text, "Instructor", MessageBoxButton.OK, MessageBoxImage.Information);

        private static bool TryRead(TextBox box, out double value)
        {
            string text = box.Text.Trim().Replace(',', '.').Replace('−', '-');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }

        private static GroupBox Group(string header, UIElement content) => new()
        {
            Header = header,
            Content = content,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static TextBox NumberBox(string text) => new()
        {
            Text = text,
            Width = 50,
            Height = 22,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        private static Button SmallButton(string text) => new() { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };

        private static TextBlock Caption(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    }
}
