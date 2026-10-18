using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// Window of the Instructor (key I, after turning it on in the Settings). PROVISIONAL LAYOUT (step 2b of the plan):
    /// the reference runway, new aircraft on the final, the list of the simulated aircraft, and the controls of the
    /// selected one on the model of the test traffic window: instructions (heading ◀ L / R ▶ and Final CRS, rate
    /// ▲ UP / ▼ DN and Normal, altitude, speed) carried out after the pilot reaction, and the stick (also a real
    /// joystick) at once. The mouse wheel over any number field changes it. The plan view and the PAR monitor come
    /// in the next steps. Closing the window removes the simulated aircraft.
    /// </summary>
    internal sealed class InstructorWindow : Window, IJoystickTarget
    {
        private const double PadSize = 150;
        private const double KnobSize = 22;
        private const double PadReach = (PadSize - KnobSize) / 2;
        /// <summary>Steps of the rate keys (ft/min).</summary>
        private const double RateKeyStep = 50;
        private static readonly Brush ReadoutBack = Frozen(Color.FromRgb(0x1B, 0x1C, 0x19));
        private static readonly Brush ReadoutText = Frozen(Color.FromRgb(0xFF, 0xB8, 0x40));
        private static readonly Brush ReadoutLabel = Frozen(Color.FromRgb(0x9A, 0x9A, 0x90));
        private static readonly Brush SelectedBack = Frozen(Color.FromRgb(0x00, 0x78, 0xD4));
        private static readonly double[] VerticalRates = [500, 700, 1000, 1500];

        private readonly InstructorBridge bridge;
        private readonly InstructorSettings settings;
        private readonly Func<Runway> radarRunway;
        private readonly Func<double> variation;
        private readonly Func<IEnumerable<string>> otherCallsigns;
        private readonly Func<Aircraft, bool> isSeen;
        private readonly Action save;
        private readonly Action<Window>? openJoystick;
        private readonly Func<string?> metar;

        // Simulation.
        private readonly TextBlock referenceText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        private readonly Button pauseButton = SmallButton("Pause");
        private readonly RadioButton qnhRadio = new() { Content = "QNH", GroupName = "InstructorPressure", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0), ToolTip = "Altitudes above mean sea level, as on the radar with QNH." };
        private readonly RadioButton qfeRadio = new() { Content = "QFE", GroupName = "InstructorPressure", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Heights above the threshold, as on the radar with QFE." };
        private readonly CheckBox auroraCheck = new() { Content = "Aurora-like data", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Positions every about 0.5 s and the altitude in steps every 3 s, as the real data from Aurora (the student sees realistic tracks). Off: perfect positions." };
        private readonly TextBox prefixBox = new() { Width = 54, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Start of the callsigns given automatically: 1 to 6 letters or digits (SIM → SIM1, SIM2...)." };

        // New aircraft.
        private readonly TextBox distanceBox = NumberBox("10");
        private readonly TextBox offsetBox = NumberBox("0");
        private readonly TextBox newAltitudeBox = NumberBox("");
        private readonly TextBox newSpeedBox = NumberBox("140");
        private readonly TextBox newCallsignBox = new() { Width = 70, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Empty: the next free callsign of the prefix." };
        private readonly ComboBox classBox = new() { Width = 90, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Performance class: speeds, climb and descent limits, accelerations, bank limit (file performance.json). Choosing it puts its approach speed in the Speed box." };

        // Wind (all the simulated aircraft).
        private readonly TextBox windFromBox = NumberBox("0");
        private readonly TextBox windSpeedBox = NumberBox("0");
        private readonly TextBox windGustBox = NumberBox("0");
        private readonly TextBlock windMagnetic = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 4, 0) };
        private readonly TextBlock noticeText = new() { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 0, 0), Visibility = Visibility.Collapsed };

        // Aircraft and readouts.
        private readonly ListBox list = new() { Height = 110, FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        private readonly TextBlock info = new() { Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock bestLabel = ReadoutCaption("BEST VS");
        private readonly TextBlock bestValue = ReadoutNumber();
        private readonly TextBlock actualValue = ReadoutNumber();
        private readonly TextBlock turnValue = ReadoutNumber();
        private readonly TextBlock headingValue = ReadoutNumber();
        private readonly TextBlock driftValue = ReadoutNumber();

        // Instructions.
        private readonly TextBox headingBox = new();
        private readonly TextBox rateBox = new();
        private readonly TextBox altitudeBox = NumberBox("");
        private readonly ComboBox altitudeRateBox = new() { Width = 64, Margin = new Thickness(4, 0, 4, 0), ItemsSource = VerticalRates.Select(r => r.ToString("0", CultureInfo.InvariantCulture)).ToArray(), SelectedIndex = 2, ToolTip = "Vertical rate to reach the altitude (ft/min)." };
        private readonly TextBlock altitudeCaption = FieldCaption("ALTITUDE ft QNH");
        private readonly TextBox speedBox = NumberBox("");
        private readonly TextBox reactionBox = NumberBox("1.0");
        private readonly Button finalCourseButton = new() { Content = "Final CRS", FontWeight = FontWeights.SemiBold, Padding = new Thickness(2, 3, 2, 3), ToolTip = "The final course in the heading box, carried out after the pilot reaction." };
        private readonly Button normalButton = new() { Content = "Normal", FontWeight = FontWeights.SemiBold, Padding = new Thickness(2, 3, 2, 3), ToolTip = "Normal rate of descent: the rate that keeps the glide path at the aircraft's ground speed, kept also when the speed or heading change; after the pilot reaction. The offset from the glide path stays." };

        // Manual.
        private readonly Button[] turnButtons = new Button[3];
        private readonly Canvas pad = new() { Width = PadSize, Height = PadSize, Background = Frozen(Color.FromRgb(0x22, 0x24, 0x22)), Cursor = Cursors.Hand };
        private readonly Ellipse knob = new() { Width = KnobSize, Height = KnobSize, Fill = Frozen(Color.FromRgb(0x70, 0xD0, 0x70)), Stroke = Brushes.Black, IsHitTestVisible = false };
        private readonly TextBox renameBox = new() { Width = 70, Height = 22, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly GroupBox controlPanel;

        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(0.25) };
        /// <summary>The fields of the selected aircraft are filled with its instructed values at the next refresh.</summary>
        private bool fillFields = true;
        private bool updating;
        /// <summary>Aircraft moved by the stick of the window (released there, even if the selection changed).</summary>
        private string? manualCallsign;
        // Real joystick: deflection last given (x right, y up) and the aircraft it moves.
        private double joystickX;
        private double joystickY;
        private string? joystickCallsign;

        public InstructorWindow(InstructorBridge bridge, InstructorSettings settings, bool qfe, Func<Runway> radarRunway, Func<double> variation,
            Func<IEnumerable<string>> otherCallsigns, Func<Aircraft, bool> isSeen, Action save, Action<Window>? openJoystick = null,
            Func<string?>? metar = null, string? notice = null)
        {
            this.bridge = bridge;
            this.settings = settings;
            this.radarRunway = radarRunway;
            this.variation = variation;
            this.otherCallsigns = otherCallsigns;
            this.isSeen = isSeen;
            this.save = save;
            this.openJoystick = openJoystick;
            this.metar = metar ?? (() => null);
            if (!string.IsNullOrEmpty(notice))
            {
                noticeText.Text = notice;
                noticeText.Visibility = Visibility.Visible;
            }
            Title = "Aurora PAR - Instructor";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.CanMinimize;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.Manual;

            StackPanel root = new() { Margin = new Thickness(10), Width = 560 };
            root.Children.Add(BuildSimulation(qfe));
            root.Children.Add(BuildNewAircraft());
            root.Children.Add(BuildAircraft());
            controlPanel = BuildControl();
            root.Children.Add(controlPanel);
            root.Children.Add(new TextBlock
            {
                Text = "Provisional layout: the plan view, the PAR monitor, the modes and the start positions come in the next steps. Closing the window removes the simulated aircraft.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(2, 6, 2, 0)
            });
            Content = root;

            reactionBox.Text = settings.ReactionSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            bridge.Engine.ReactionSeconds = settings.ReactionSeconds;
            ShowTurnMode(null);
            pauseButton.Content = bridge.Engine.Paused ? "Resume" : "Pause";
            ShowWindMagnetic();
            refresh.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { refresh.Start(); Refresh(); };
            Closed += (s, e) => refresh.Stop();
            Refresh();
        }

        // ----- Building the window ---------------------------------------------------------------------------------

        private GroupBox BuildSimulation(bool qfe)
        {
            StackPanel referenceRow = new() { Orientation = Orientation.Horizontal };
            referenceRow.Children.Add(Caption("Runway"));
            referenceRow.Children.Add(referenceText);
            referenceRow.ToolTip = "Runway the simulated aircraft are placed from. With no simulated aircraft flying it follows the runway of the radar; while they fly it stays, so they stay where they are if the runway of the radar changes.";
            WrapPanel optionsRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            pauseButton.Click += (s, e) => TogglePause();
            optionsRow.Children.Add(pauseButton);
            optionsRow.Children.Add(qnhRadio);
            optionsRow.Children.Add(qfeRadio);
            optionsRow.Children.Add(auroraCheck);
            optionsRow.Children.Add(Caption("   Callsign prefix"));
            optionsRow.Children.Add(prefixBox);
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
            StackPanel panel = new();
            panel.Children.Add(referenceRow);
            panel.Children.Add(optionsRow);
            // Wind: the same at all heights, for all the simulated aircraft.
            WrapPanel windRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            windRow.Children.Add(Caption("Wind from"));
            windRow.Children.Add(windFromBox);
            windRow.Children.Add(Caption("°T"));
            windRow.Children.Add(windMagnetic);
            windRow.Children.Add(windSpeedBox);
            windRow.Children.Add(Caption("kt   gusts +"));
            windRow.Children.Add(windGustBox);
            windRow.Children.Add(Caption("kt"));
            Button fromMetar = SmallButton("From METAR");
            fromMetar.Margin = new Thickness(6, 0, 0, 0);
            fromMetar.ToolTip = "Copies the wind of the METAR of the airport (received from Aurora). A METAR gives the direction in degrees true.";
            fromMetar.Click += (s, e) => WindFromMetar();
            windRow.Children.Add(fromMetar);
            windRow.ToolTip = "The aircraft fly their heading through the air: a crosswind makes them drift (DRIFT), a headwind lowers the ground speed and so the Normal rate. Gusts: the wind grows at random up to this much more. The same at all heights.";
            (double windFrom, double windSpeed, double windGust) = bridge.Engine.Wind;
            windFromBox.Text = windFrom.ToString("000", CultureInfo.InvariantCulture);
            windSpeedBox.Text = windSpeed.ToString("0", CultureInfo.InvariantCulture);
            windGustBox.Text = windGust.ToString("0", CultureInfo.InvariantCulture);
            // Applied with Enter, when leaving the box, or at each wheel step (not at each key typed: "2" before "250").
            foreach (TextBox box in new[] { windFromBox, windSpeedBox, windGustBox })
            {
                box.KeyDown += (s, e) => { if (e.Key == Key.Enter) ApplyWind(); };
                box.LostFocus += (s, e) => ApplyWind();
            }
            Wheel(windFromBox, 10, 30, 0, 360, "000", ApplyWind);
            Wheel(windSpeedBox, 1, 5, 0, SimEngine.MaxWind, "0", ApplyWind);
            Wheel(windGustBox, 1, 5, 0, SimEngine.MaxGust, "0", ApplyWind);
            panel.Children.Add(windRow);
            panel.Children.Add(noticeText);
            return Group("Simulation", panel);
        }

        private GroupBox BuildNewAircraft()
        {
            WrapPanel row = new();
            row.Children.Add(Caption("Distance"));
            row.Children.Add(distanceBox);
            row.Children.Add(Caption("NM  offset"));
            row.Children.Add(offsetBox);
            row.Children.Add(Caption("NM (+R/−L)  altitude"));
            row.Children.Add(newAltitudeBox);
            row.Children.Add(Caption("ft"));
            WrapPanel row2 = new() { Margin = new Thickness(0, 6, 0, 0) };
            row2.Children.Add(Caption("Speed"));
            row2.Children.Add(newSpeedBox);
            row2.Children.Add(Caption("kt  callsign"));
            row2.Children.Add(newCallsignBox);
            row2.Children.Add(Caption("class"));
            row2.Children.Add(classBox);
            classBox.ItemsSource = bridge.Engine.Classes.Select(c => c.Name).ToList();
            classBox.SelectedItem = bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, PerformanceClass.DefaultName, StringComparison.OrdinalIgnoreCase))?.Name ?? bridge.Engine.Classes[0].Name;
            classBox.SelectionChanged += (s, e) =>
            {
                PerformanceClass? kind = bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, classBox.SelectedItem as string, StringComparison.OrdinalIgnoreCase));
                if (kind != null) newSpeedBox.Text = kind.ApproachSpeedKt.ToString("0", CultureInfo.InvariantCulture);
            };
            if (bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, classBox.SelectedItem as string, StringComparison.OrdinalIgnoreCase)) is PerformanceClass first)
            {
                newSpeedBox.Text = first.ApproachSpeedKt.ToString("0", CultureInfo.InvariantCulture);
            }
            Button add = new() { Content = "New aircraft on final", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), FontWeight = FontWeights.SemiBold };
            add.Click += (s, e) => AddOnFinal();
            row2.Children.Add(add);
            distanceBox.ToolTip = "Distance from touchdown (NM). Mouse wheel: 0.5 NM (+Shift: 1).";
            offsetBox.ToolTip = "Offset from the extended centreline, to the right (+) or left (−) as seen by the pilot (NM). Mouse wheel: 0.1 NM (+Shift: 1).";
            newAltitudeBox.ToolTip = "Empty: on the glide path at that distance. With QFE selected: height above the threshold. Mouse wheel: 100 ft (+Shift: 1000). The aircraft starts level: Normal starts the descent.";
            newSpeedBox.ToolTip = "Speed (kt). Mouse wheel: 5 kt (+Shift: 10).";
            Wheel(distanceBox, 0.5, 1, 0.5, 60, "0.0", null);
            Wheel(offsetBox, 0.1, 1, -20, 20, "0.0", null);
            Wheel(newAltitudeBox, 100, 1000, -2000, 40000, "0", null, NewAircraftAltitude);
            Wheel(newSpeedBox, 5, 10, 40, 400, "0", null);
            StackPanel panel = new();
            panel.Children.Add(row);
            panel.Children.Add(row2);
            return Group("New aircraft on final (heading of the runway)", panel);
        }

        private GroupBox BuildAircraft()
        {
            StackPanel left = new() { Width = 300 };
            left.Children.Add(new TextBlock { Text = "Call      HDG    ALT/HGT   SPD    NM PAR", FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 2) });
            left.Children.Add(list);
            StackPanel removeRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            Button remove = SmallButton("Remove");
            remove.Click += (s, e) => { if (SelectedCallsign is string callsign) bridge.Engine.Remove(callsign); Refresh(); };
            Button removeAll = SmallButton("Remove all");
            removeAll.Click += (s, e) => { bridge.Clear(); Refresh(); };
            removeRow.Children.Add(remove);
            removeRow.Children.Add(removeAll);
            left.Children.Add(removeRow);
            DockPanel panel = new();
            panel.Children.Add(left);
            panel.Children.Add(info);
            list.SelectionChanged += (s, e) =>
            {
                if (updating) return;
                // Stick held on the previous aircraft: let it go. The joystick held over goes to the aircraft now selected.
                ReleaseManual();
                ApplyJoystick();
                fillFields = true;
                Refresh();
            };
            return Group("Aircraft", panel);
        }

        private GroupBox BuildControl()
        {
            StackPanel control = new();
            // Readouts the instructor needs while flying.
            UniformGrid readouts = new() { Columns = 5, Margin = new Thickness(0, 0, 0, 8) };
            readouts.Children.Add(Readout(bestLabel, bestValue, "Vertical speed that keeps the glide path at the aircraft's ground speed (ft/min) — what Normal gives."));
            readouts.Children.Add(Readout(ReadoutCaption("VS"), actualValue, "Vertical speed now (ft/min)."));
            readouts.Children.Add(Readout(ReadoutCaption("TURN °/s"), turnValue, "Turn rate now."));
            readouts.Children.Add(Readout(ReadoutCaption("HDG °M"), headingValue, "Heading now (magnetic)."));
            readouts.Children.Add(Readout(ReadoutCaption("DRIFT °"), driftValue, "Drift by the wind: the track over the ground minus the heading (R: to the right)."));
            control.Children.Add(readouts);

            Grid columns = new();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Instructions: heading (◀ L, box, R ▶) and rate (▲ UP, box, ▼ DN), altitude, speed; after the pilot reaction.
            StackPanel instructions = new();
            Grid fields = new();
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DockPanel headingColumn = new() { Margin = new Thickness(0, 0, 6, 0), LastChildFill = false };
            TextBlock headingCaption = FieldCaption("HEADING °M");
            DockPanel.SetDock(headingCaption, Dock.Top);
            headingColumn.Children.Add(headingCaption);
            DockPanel.SetDock(finalCourseButton, Dock.Bottom);
            finalCourseButton.Click += (s, e) => FinalCourse();
            headingColumn.Children.Add(finalCourseButton);
            TextBlock headingHint = FieldHint("keys 1°");
            DockPanel.SetDock(headingHint, Dock.Bottom);
            headingColumn.Children.Add(headingHint);
            StackPanel headingKeys = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RepeatButton left = StepKey("◀ L", "Heading 1° left (held: repeats). Carried out after the pilot reaction.");
            left.Click += (s, e) => StepHeading(-1);
            RepeatButton right = StepKey("R ▶", "Heading 1° right (held: repeats). Carried out after the pilot reaction.");
            right.Click += (s, e) => StepHeading(1);
            headingKeys.Children.Add(left);
            headingKeys.Children.Add(headingBox);
            headingKeys.Children.Add(right);
            DockPanel.SetDock(headingKeys, Dock.Top);
            headingColumn.Children.Add(headingKeys);
            fields.Children.Add(headingColumn);

            StackPanel rateColumn = new() { Margin = new Thickness(6, 0, 0, 0) };
            rateColumn.Children.Add(FieldCaption("RATE ft/min"));
            RepeatButton up = StepKey("▲ UP", "Rate 50 ft/min up: less descent (held: repeats; stops once at the Normal rate). Carried out after the pilot reaction.");
            up.Click += (s, e) => StepRate(RateKeyStep);
            up.HorizontalAlignment = HorizontalAlignment.Stretch;
            rateColumn.Children.Add(up);
            rateBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            rateBox.Margin = new Thickness(0, 3, 0, 3);
            rateColumn.Children.Add(rateBox);
            RepeatButton down = StepKey("▼ DN", "Rate 50 ft/min down: more descent (held: repeats; stops once at the Normal rate). Carried out after the pilot reaction.");
            down.Click += (s, e) => StepRate(-RateKeyStep);
            down.HorizontalAlignment = HorizontalAlignment.Stretch;
            rateColumn.Children.Add(down);
            rateColumn.Children.Add(FieldHint("keys 50"));
            normalButton.Click += (s, e) => NormalRate();
            rateColumn.Children.Add(normalButton);
            Grid.SetColumn(rateColumn, 1);
            fields.Children.Add(rateColumn);
            instructions.Children.Add(fields);

            foreach (TextBox box in new[] { headingBox, rateBox })
            {
                box.Width = box == headingBox ? 48 : double.NaN;
                box.Height = 26;
                box.FontFamily = new FontFamily("Consolas");
                box.FontSize = 15;
                box.FontWeight = FontWeights.Bold;
                box.HorizontalContentAlignment = HorizontalAlignment.Center;
                box.VerticalContentAlignment = VerticalAlignment.Center;
                box.Margin = box == headingBox ? new Thickness(3, 0, 3, 0) : box.Margin;
            }
            headingBox.MaxLength = 3;
            headingBox.ToolTip = "Heading given by the controller, magnetic. Mouse wheel: 1° per step (+Shift: 10°). Enter: carry out. The aircraft turns the shortest way after the pilot reaction.";
            rateBox.MaxLength = 5;
            rateBox.ToolTip = "Rate (vertical speed) given by the controller, ft/min (negative = descent). Mouse wheel: 100 ft/min per step (+Shift: 500), stopping once at the Normal rate. Enter: carry out, after the pilot reaction.";
            headingBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GiveHeading(); e.Handled = true; } };
            rateBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GiveRate(); e.Handled = true; } };
            headingBox.PreviewMouseWheel += (s, e) =>
            {
                StepHeading((e.Delta > 0 ? 1 : -1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1));
                e.Handled = true;
            };
            rateBox.PreviewMouseWheel += (s, e) =>
            {
                StepRate((e.Delta > 0 ? 1 : -1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 500 : 100));
                e.Handled = true;
            };

            // Altitude and speed.
            Grid more = new() { Margin = new Thickness(0, 8, 0, 0) };
            more.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            more.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel altitudeColumn = new() { Margin = new Thickness(0, 0, 6, 0) };
            altitudeColumn.Children.Add(altitudeCaption);
            StackPanel altitudeRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            altitudeRow.Children.Add(altitudeBox);
            altitudeRow.Children.Add(Caption("at"));
            altitudeRow.Children.Add(altitudeRateBox);
            altitudeColumn.Children.Add(altitudeRow);
            altitudeColumn.Children.Add(FieldHint("wheel 100 (Shift 1000), Enter"));
            altitudeBox.ToolTip = "Altitude to climb or descend to (QNH, or QFE height), at the rate chosen beside it; then the aircraft levels off. Mouse wheel: 100 ft (+Shift: 1000). Enter: carry out, after the pilot reaction.";
            altitudeBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GiveAltitude(); e.Handled = true; } };
            Wheel(altitudeBox, 100, 1000, -2000, 40000, "0", GiveAltitude);
            altitudeRateBox.SelectionChanged += (s, e) =>
            {
                if (updating || SelectedCallsign is not string callsign) return;
                bridge.Engine.SetVerticalRate(callsign, VerticalRates[Math.Clamp(altitudeRateBox.SelectedIndex, 0, VerticalRates.Length - 1)]);
            };
            more.Children.Add(altitudeColumn);
            StackPanel speedColumn = new() { Margin = new Thickness(6, 0, 0, 0) };
            speedColumn.Children.Add(FieldCaption("SPEED kt"));
            speedBox.HorizontalAlignment = HorizontalAlignment.Center;
            speedColumn.Children.Add(speedBox);
            speedColumn.Children.Add(FieldHint("wheel 5 (Shift 10), Enter"));
            speedBox.ToolTip = "Speed (kt). Mouse wheel: 5 kt (+Shift: 10). Enter: carry out, after the pilot reaction.";
            speedBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GiveSpeed(); e.Handled = true; } };
            Wheel(speedBox, 5, 10, 40, 400, "0", GiveSpeed);
            Grid.SetColumn(speedColumn, 1);
            more.Children.Add(speedColumn);
            instructions.Children.Add(more);

            Border wheelNote = new()
            {
                Background = Frozen(Color.FromRgb(0xEE, 0xF5, 0xFC)),
                BorderBrush = Frozen(Color.FromRgb(0xB9, 0xD5, 0xEF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text = "Mouse wheel over a field changes the value and gives the instruction: heading 1° (+Shift 10°), rate 100 ft/min (+Shift 500), altitude 100 ft (+Shift 1000), speed 5 kt (+Shift 10).",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    Foreground = Frozen(Color.FromRgb(0x00, 0x4C, 0x8C))
                }
            };
            instructions.Children.Add(wheelNote);
            StackPanel reactionRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            reactionRow.ToolTip = "Pilots are not instantaneous: every instruction (heading, rate, altitude, speed, Final CRS, Normal) is carried out this long after it is given; with the wheel or held keys, after the last step (0 to 5 s). The stick acts at once.";
            reactionRow.Children.Add(Caption("Pilot reaction"));
            reactionRow.Children.Add(reactionBox);
            reactionRow.Children.Add(Caption("s"));
            reactionBox.TextChanged += (s, e) => ApplyReaction();
            Wheel(reactionBox, 0.1, 1, 0, 5, "0.0", null);
            instructions.Children.Add(reactionRow);
            GroupBox instructionsGroup = Group("Instructions (controller)", instructions);
            instructionsGroup.Margin = new Thickness(0, 0, 8, 0);
            columns.Children.Add(instructionsGroup);

            // Manual: turn rate and the stick (also a real joystick), at once.
            StackPanel manual = new() { Width = PadSize + 4 };
            manual.Children.Add(new TextBlock { Text = "Turn rate", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            UniformGrid turnRow = new() { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            (string Text, TurnRateMode Mode, string Tip)[] modes =
            [
                ("1.5°/s", TurnRateMode.Half, "Half rate: 1.5°/s with the stick at its edge, and for the heading instructions."),
                ("3°/s", TurnRateMode.Standard, "Rate one: 3°/s with the stick at its edge, and for the heading instructions."),
                ("Free", TurnRateMode.Free, "The longer the stick is held at its edge, the faster the turn (up to 10°/s); heading instructions at 3°/s.")
            ];
            for (int i = 0; i < modes.Length; i++)
            {
                TurnRateMode mode = modes[i].Mode;
                Button button = new() { Content = modes[i].Text, ToolTip = modes[i].Tip, Padding = new Thickness(2) };
                button.Click += (s, e) =>
                {
                    if (SelectedCallsign is string callsign) bridge.Engine.SetTurnRateMode(callsign, mode);
                    ShowTurnMode(mode);
                };
                turnButtons[i] = button;
                turnRow.Children.Add(button);
            }
            manual.Children.Add(turnRow);
            Border padBorder = new() { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = pad, HorizontalAlignment = HorizontalAlignment.Left };
            pad.Children.Add(new Line { X1 = PadSize / 2, Y1 = 4, X2 = PadSize / 2, Y2 = PadSize - 4, Stroke = Brushes.DimGray });
            pad.Children.Add(new Line { X1 = 4, Y1 = PadSize / 2, X2 = PadSize - 4, Y2 = PadSize / 2, Stroke = Brushes.DimGray });
            pad.Children.Add(PadText("UP", PadSize / 2 - 8, 2));
            pad.Children.Add(PadText("DN", PadSize / 2 - 8, PadSize - 16));
            pad.Children.Add(PadText("L", 4, PadSize / 2 - 16));
            pad.Children.Add(PadText("R", PadSize - 12, PadSize / 2 - 16));
            pad.Children.Add(knob);
            CentreKnob();
            pad.ToolTip = "Control stick: hold left/right to turn the selected aircraft at the turn rate chosen above, up/down to change its vertical speed, at once (no pilot reaction). Released, the heading and the vertical speed reached stay, and the fields on the left follow.";
            pad.MouseLeftButtonDown += (s, e) =>
            {
                pad.CaptureMouse();
                MoveKnob(e.GetPosition(pad));
                e.Handled = true;
            };
            pad.MouseMove += (s, e) =>
            {
                if (pad.IsMouseCaptured) MoveKnob(e.GetPosition(pad));
            };
            pad.MouseLeftButtonUp += (s, e) => ReleaseKnob();
            pad.LostMouseCapture += (s, e) => ReleaseKnob();
            manual.Children.Add(padBorder);
            manual.Children.Add(new TextBlock
            {
                Text = "Stick or real joystick: at once, no reaction delay. Released, heading and rate stay; the fields follow.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 6, 0, 0)
            });
            GroupBox manualGroup = Group("Manual (pilot flying)", manual);
            Grid.SetColumn(manualGroup, 1);
            columns.Children.Add(manualGroup);
            control.Children.Add(columns);

            // Service: rename, joystick settings. (Freeze of one aircraft: frozen 18 Oct, still in the engine.)
            WrapPanel service = new() { Margin = new Thickness(0, 2, 0, 0) };
            service.Children.Add(Caption("Callsign"));
            service.Children.Add(renameBox);
            Button rename = SmallButton("Rename");
            rename.Margin = new Thickness(4, 0, 12, 0);
            rename.Click += (s, e) => Rename();
            renameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) Rename(); };
            service.Children.Add(rename);
            if (openJoystick != null)
            {
                Button joystickButton = SmallButton("Joystick...");
                joystickButton.ToolTip = "A real joystick or gamepad: device, buttons, live test.";
                joystickButton.Click += (s, e) => openJoystick(this);
                service.Children.Add(joystickButton);
            }
            control.Children.Add(service);
            return Group("Control of the selected aircraft", control);
        }

        // ----- Joystick (IJoystickTarget) --------------------------------------------------------------------------

        /// <summary>An aircraft is selected (the joystick moves it in the automatic role).</summary>
        public bool HasSelection => SelectedCallsign != null;

        /// <summary>
        /// The real joystick moved (only when it changes): as the stick of the window, which follows it. Released
        /// (0, 0), the heading and vertical speed reached stay. Ignored while the stick is held with the mouse.
        /// </summary>
        public void JoystickStick(double x, double y)
        {
            joystickX = x;
            joystickY = y;
            ApplyJoystick();
        }

        /// <summary>A command from a joystick button.</summary>
        public void RunJoystickCommand(JoystickCommand command)
        {
            switch (command)
            {
                case JoystickCommand.FinalCourse: FinalCourse(); break;
                case JoystickCommand.NormalRate: NormalRate(); break;
                case JoystickCommand.ReduceRate: StepRate(100); break;
                case JoystickCommand.IncreaseRate: StepRate(-100); break;
                case JoystickCommand.Pause: TogglePause(); break;
                case JoystickCommand.TurnRate:
                    if (Selected() is AircraftState state)
                    {
                        TurnRateMode next = state.TurnRateMode switch
                        {
                            TurnRateMode.Half => TurnRateMode.Standard,
                            TurnRateMode.Standard => TurnRateMode.Free,
                            _ => TurnRateMode.Half
                        };
                        bridge.Engine.SetTurnRateMode(state.Callsign, next);
                        ShowTurnMode(next);
                    }
                    break;
                case JoystickCommand.NextAircraft:
                    if (list.Items.Count > 0) list.SelectedIndex = (list.SelectedIndex + 1) % list.Items.Count;
                    break;
            }
        }

        private void ApplyJoystick()
        {
            if (pad.IsMouseCaptured) return;
            string? selected = SelectedCallsign;
            // Another aircraft was moved: let it go first.
            if (joystickCallsign != null && joystickCallsign != selected)
            {
                bridge.Engine.SetStick(joystickCallsign, 0, 0);
                joystickCallsign = null;
                CentreKnob();
            }
            if (selected == null) return;
            if (joystickX == 0 && joystickY == 0)
            {
                if (joystickCallsign == null) return;
                joystickCallsign = null;
                fillFields = true;
                CentreKnob();
                bridge.Engine.SetStick(selected, 0, 0);
                return;
            }
            joystickCallsign = selected;
            ShowKnob(joystickX, joystickY);
            bridge.Engine.SetStick(selected, joystickX, joystickY);
        }

        // ----- Commands --------------------------------------------------------------------------------------------

        /// <summary>Callsign of the selected aircraft, or null.</summary>
        private string? SelectedCallsign => (list.SelectedItem as ListBoxItem)?.Tag as string;

        private AircraftState? Selected() => SelectedCallsign is string callsign ? bridge.Engine.Get(callsign) : null;

        private bool Qfe => qfeRadio.IsChecked == true;

        private double Elevation => bridge.Reference?.Elevation ?? 0;

        private void TogglePause()
        {
            bridge.Engine.Paused = !bridge.Engine.Paused;
            pauseButton.Content = bridge.Engine.Paused ? "Resume" : "Pause";
        }

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

        private void ApplyWind()
        {
            if (!TryRead(windFromBox, out double from) || from < 0 || from > 360
                || !TryRead(windSpeedBox, out double speed) || speed < 0 || speed > SimEngine.MaxWind
                || !TryRead(windGustBox, out double gust) || gust < 0 || gust > SimEngine.MaxGust) return;
            bridge.Engine.SetWind(from, speed, gust);
            ShowWindMagnetic();
        }

        private void ShowWindMagnetic()
        {
            (double from, double speed, _) = bridge.Engine.Wind;
            windMagnetic.Text = speed > 0 ? string.Format(CultureInfo.InvariantCulture, "({0:000}°M)", Magnetic(from, variation())) : "";
        }

        private void WindFromMetar()
        {
            if (!MetarWind.TryParse(metar(), out double from, out double speed, out double gust))
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show(this, "No METAR wind received from Aurora for the airport in use.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Variable: the direction stays.
            if (!double.IsNaN(from)) windFromBox.Text = from.ToString("000", CultureInfo.InvariantCulture);
            windSpeedBox.Text = Math.Round(speed).ToString(CultureInfo.InvariantCulture);
            windGustBox.Text = Math.Round(gust).ToString(CultureInfo.InvariantCulture);
            ApplyWind();
        }

        private void ApplyReaction()
        {
            if (!TryRead(reactionBox, out double seconds) || seconds < 0 || seconds > SimEngine.MaxReaction) return;
            bridge.Engine.ReactionSeconds = seconds;
            settings.ReactionSeconds = seconds;
        }

        /// <summary>
        /// With no simulated aircraft flying, the reference runway follows the runway of the radar (another runway,
        /// approach or airport chosen there). While aircraft fly it stays: they stay where they are.
        /// </summary>
        private void FollowRadarRunway()
        {
            if (bridge.Count > 0) return;
            Runway radar = radarRunway();
            Runway? reference = bridge.Reference;
            if (reference != null && reference.RunwayKey == radar.RunwayKey && reference.GlideSlope == radar.GlideSlope
                && reference.Latitude == radar.Latitude && reference.Longitude == radar.Longitude && reference.Heading == radar.DefaultHeading
                && reference.Elevation == radar.Elevation && reference.TCH == radar.TCH && reference.TouchdownM == radar.TouchdownM
                && reference.LengthM == radar.LengthM) return;
            bridge.SetReference(radar);
            fillFields = true;
        }

        /// <summary>Altitude for the wheel of the new aircraft box when it is empty: the glide path at the distance.</summary>
        private double NewAircraftAltitude()
        {
            Runway? runway = bridge.Reference;
            double distance = TryRead(distanceBox, out double d) ? d : 10;
            double height = runway?.GlidePathHeight(distance) ?? 3000;
            return Math.Round((Qfe ? height : height + Elevation) / 100) * 100;
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
                string name = bridge.Add(x, y, altitude, runway.Heading, speed, callsign, otherCallsigns(), classBox.SelectedItem as string);
                bridge.Engine.SetTurnRateMode(name, CurrentTurnMode());
                newCallsignBox.Text = "";
                Refresh();
                Select(name);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Warn(ex.Message);
            }
        }

        /// <summary>The turn rate shown on the buttons (taken by the next new aircraft).</summary>
        private TurnRateMode currentTurnMode = TurnRateMode.Standard;

        private TurnRateMode CurrentTurnMode() => currentTurnMode;

        /// <summary>Final CRS: the final course in the heading box, carried out after the pilot reaction.</summary>
        private void FinalCourse()
        {
            Runway? runway = bridge.Reference;
            if (runway == null || SelectedCallsign is not string callsign) return;
            headingBox.Text = Magnetic(runway.Heading, variation()).ToString("000", CultureInfo.InvariantCulture);
            // The exact final course, not the magnetic heading rounded to a degree (0.5° would be 160 m off at 10 NM).
            bridge.Engine.SetHeading(callsign, runway.Heading);
        }

        /// <summary>Heading box changed by keys or wheel (degrees, + right), then carried out after the pilot reaction.</summary>
        private void StepHeading(int degrees)
        {
            if (!int.TryParse(headingBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int heading) || heading < 0 || heading > 360)
            {
                if (Selected() is not AircraftState state)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                heading = Magnetic(state.HeadingTrue, variation());
            }
            heading = ((heading + degrees) % 360 + 360) % 360;
            if (heading == 0) heading = 360;
            headingBox.Text = heading.ToString("000", CultureInfo.InvariantCulture);
            headingBox.CaretIndex = headingBox.Text.Length;
            GiveHeading();
        }

        /// <summary>A heading instruction (magnetic, the shortest way): after the pilot reaction; a new one replaces it.</summary>
        private void GiveHeading()
        {
            if (SelectedCallsign is not string callsign || !int.TryParse(headingBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int heading) || heading < 0 || heading > 360)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            bridge.Engine.SetHeading(callsign, SimEngine.Normalize(heading + variation()));
        }

        /// <summary>
        /// Rate box changed by keys or wheel (ft/min, + up), stopping once at the Normal rate when a step would cross
        /// it; then carried out after the pilot reaction.
        /// </summary>
        private void StepRate(double step)
        {
            if (Selected() is not AircraftState state)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            double current = TryRead(rateBox, out double value) ? value : Math.Round(state.VerticalSpeedFpm / 10) * 10;
            double best = Math.Round(state.BestVerticalSpeedFpm / 10) * 10;
            double next = current + step;
            if (best != 0 && ((current < best && next > best) || (current > best && next < best))) next = best;
            next = Math.Clamp(next, SimEngine.MaxDescent, SimEngine.MaxClimb);
            rateBox.Text = FormatRate(next);
            rateBox.CaretIndex = rateBox.Text.Length;
            // Stopped on the Normal rate: Normal itself, which follows the speed and the wind.
            if (best != 0 && next == best) bridge.Engine.SetGlidePathRate(state.Callsign);
            else GiveRate();
        }

        /// <summary>A rate instruction (vertical speed to keep): after the pilot reaction; a new one replaces it.</summary>
        private void GiveRate()
        {
            if (SelectedCallsign is not string callsign || !TryRead(rateBox, out double rate) || rate < SimEngine.MaxDescent || rate > SimEngine.MaxClimb)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            bridge.Engine.SetVerticalSpeed(callsign, rate);
        }

        /// <summary>Normal rate: the rate that keeps the glide path, shown in the box, kept while speed or heading change.</summary>
        private void NormalRate()
        {
            if (Selected() is not AircraftState state) return;
            rateBox.Text = FormatRate(Math.Round(state.BestVerticalSpeedFpm / 10) * 10);
            bridge.Engine.SetGlidePathRate(state.Callsign);
        }

        private void GiveAltitude()
        {
            if (SelectedCallsign is not string callsign || !TryRead(altitudeBox, out double value) || value < (Qfe ? 0 : Elevation) || value > 40000)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            double rate = VerticalRates[Math.Clamp(altitudeRateBox.SelectedIndex, 0, VerticalRates.Length - 1)];
            bridge.Engine.SetAltitude(callsign, Qfe ? value + Elevation : value, rate);
        }

        private void GiveSpeed()
        {
            if (SelectedCallsign is not string callsign || !TryRead(speedBox, out double speed) || speed < 40 || speed > 400)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            bridge.Engine.SetSpeed(callsign, speed);
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
            if (manualCallsign == callsign) manualCallsign = name;
            if (joystickCallsign == callsign) joystickCallsign = name;
            Refresh();
            Select(name);
        }

        private void Select(string callsign)
        {
            ListBoxItem? item = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, callsign, StringComparison.OrdinalIgnoreCase));
            if (item != null) list.SelectedItem = item;
        }

        // ----- Stick -----------------------------------------------------------------------------------------------

        /// <summary>The selection changed while the stick was held: the previous aircraft keeps its heading and vertical speed.</summary>
        private void ReleaseManual()
        {
            if (manualCallsign == null || manualCallsign == SelectedCallsign) return;
            bridge.Engine.SetStick(manualCallsign, 0, 0);
            manualCallsign = null;
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
        }

        private void CentreKnob()
        {
            Canvas.SetLeft(knob, PadSize / 2 - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 - KnobSize / 2);
        }

        /// <summary>The knob at a deflection (x right, y up, −1..1), inside the circle of the pad.</summary>
        private void ShowKnob(double x, double y)
        {
            double length = Math.Sqrt(x * x + y * y);
            double scale = length > 1 ? PadReach / length : PadReach;
            Canvas.SetLeft(knob, PadSize / 2 + x * scale - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 - y * scale - KnobSize / 2);
        }

        /// <summary>Stick moved: X turns, Y changes the vertical speed, proportionally (inside a circle).</summary>
        private void MoveKnob(Point point)
        {
            if (SelectedCallsign is not string callsign) return;
            double dx = point.X - PadSize / 2;
            double dy = point.Y - PadSize / 2;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > PadReach)
            {
                dx *= PadReach / length;
                dy *= PadReach / length;
            }
            Canvas.SetLeft(knob, PadSize / 2 + dx - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 + dy - KnobSize / 2);
            double x = dx / PadReach;
            double y = -dy / PadReach;
            // A small dead zone in the middle, so a click on the centre does nothing.
            if (Math.Abs(x) < 0.08) x = 0;
            if (Math.Abs(y) < 0.08) y = 0;
            manualCallsign = callsign;
            bridge.Engine.SetStick(callsign, x, y);
        }

        private void ReleaseKnob()
        {
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
            // The heading and the vertical speed reached stay.
            if ((manualCallsign ?? SelectedCallsign) is string released) bridge.Engine.SetStick(released, 0, 0);
            manualCallsign = null;
            fillFields = true;
            // A real joystick still deflected takes over again.
            if (joystickCallsign != null || joystickX != 0 || joystickY != 0)
            {
                joystickCallsign = null;
                ApplyJoystick();
            }
        }

        // ----- Showing -----------------------------------------------------------------------------------------------

        private void ShowTurnMode(TurnRateMode? mode)
        {
            if (mode is TurnRateMode chosen) currentTurnMode = chosen;
            TurnRateMode[] modes = [TurnRateMode.Half, TurnRateMode.Standard, TurnRateMode.Free];
            for (int i = 0; i < turnButtons.Length; i++)
            {
                if (turnButtons[i] == null) continue;
                if (currentTurnMode == modes[i])
                {
                    turnButtons[i].Background = SelectedBack;
                    turnButtons[i].Foreground = Brushes.White;
                }
                else
                {
                    turnButtons[i].ClearValue(BackgroundProperty);
                    turnButtons[i].ClearValue(ForegroundProperty);
                }
            }
        }

        /// <summary>Updates the reference text, the list (keeping the selection) and the selected aircraft.</summary>
        /// <summary>Callsign of the aircraft whose values the fields show.</summary>
        private string? shownCallsign;

        private void Refresh()
        {
            FollowRadarRunway();
            Runway? runway = bridge.Reference;
            double magneticVariation = variation();
            referenceText.Text = runway == null ? "none" : $"{runway.DisplayName}  (elevation {runway.Elevation:0} ft)";
            altitudeCaption.Text = Qfe ? "ALTITUDE ft QFE" : "ALTITUDE ft QNH";
            IReadOnlyList<AircraftState> states = bridge.Engine.Snapshot();
            updating = true;
            try
            {
                string? selected = SelectedCallsign;
                // Items by callsign: changed in place, so the selection stays.
                foreach (ListBoxItem item in list.Items.OfType<ListBoxItem>().ToList())
                {
                    if (!states.Any(s => string.Equals(s.Callsign, item.Tag as string, StringComparison.OrdinalIgnoreCase))) list.Items.Remove(item);
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
                        state.Callsign, Magnetic(state.HeadingTrue, magneticVariation), altitude, climbing, state.SpeedKt, distance, seen,
                        state.Contact switch { GroundContact.Landed => " LANDED", GroundContact.OnGround => " GROUND", _ => state.Frozen ? " FRZ" : "" });
                    item.Foreground = state.Contact == GroundContact.OnGround ? Brushes.Red : Brushes.Black;
                }
                if (list.SelectedItem == null && list.Items.Count > 0 && selected == null) list.SelectedIndex = 0;
            }
            finally
            {
                updating = false;
            }
            // The selection changed by itself (an aircraft removed or landed, the first one added): as a click.
            if (SelectedCallsign != shownCallsign)
            {
                shownCallsign = SelectedCallsign;
                ReleaseManual();
                ApplyJoystick();
                fillFields = true;
            }
            ShowSelected(states.FirstOrDefault(s => string.Equals(s.Callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase)), runway, magneticVariation);
        }

        private void ShowSelected(AircraftState? state, Runway? runway, double magneticVariation)
        {
            controlPanel.IsEnabled = state != null;
            if (state == null)
            {
                finalCourseButton.Content = "Final CRS";
                normalButton.Content = "Normal";
                info.Text = bridge.Count == 0 ? "No simulated aircraft:\nadd one above." : "";
                bestValue.Text = actualValue.Text = turnValue.Text = headingValue.Text = driftValue.Text = "---";
                return;
            }
            Aircraft? position = bridge.LastPosition(state.Callsign);
            double altitude = Qfe ? state.AltitudeFt - Elevation : state.AltitudeFt;
            string mode = state.StickActive ? "stick" : state.VerticalMode switch
            {
                VerticalMode.GlidePath => "Normal rate",
                VerticalMode.Rate => "rate",
                _ => "altitude"
            };
            info.Text = string.Format(CultureInfo.InvariantCulture, "{0}  {8}{1}\n{2} NM from touchdown\n{3:0} ft {4}, {5:0} kt, ground speed {9:0} kt\nvertical: {6}{7}",
                state.Callsign, state.Frozen ? "  (frozen)" : "",
                position != null && runway != null ? position.DistanceFromTouchdown(runway).ToString("0.0", CultureInfo.InvariantCulture) : "--",
                altitude, Qfe ? "QFE" : "QNH", state.SpeedKt, mode,
                state.PendingCommands > 0 ? $"\npilot: {state.PendingCommands} instruction(s) coming" : "",
                state.PerformanceClass, state.GroundSpeedKt);
            bestLabel.Text = runway != null ? string.Format(CultureInfo.InvariantCulture, "BEST VS {0:0.0#}°", runway.GlideSlope) : "BEST VS";
            bestValue.Text = Rounded(state.BestVerticalSpeedFpm);
            actualValue.Text = Rounded(state.VerticalSpeedFpm);
            turnValue.Text = Math.Abs(state.TurnRateDps) < 0.05 ? "0.0"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", Math.Abs(state.TurnRateDps), state.TurnRateDps > 0 ? "R" : "L");
            headingValue.Text = Magnetic(state.HeadingTrue, magneticVariation).ToString("000", CultureInfo.InvariantCulture);
            int drift = (int)Math.Round(state.DriftDegrees);
            driftValue.Text = drift == 0 ? "0" : $"{Math.Abs(drift)} {(drift > 0 ? "R" : "L")}";
            double best = Math.Round(state.BestVerticalSpeedFpm / 10) * 10;
            if (runway != null) finalCourseButton.Content = $"Final CRS ({Magnetic(runway.Heading, magneticVariation):000})";
            normalButton.Content = $"Normal ({FormatRate(best).Replace('-', '−')})";
            if (currentTurnMode != state.TurnRateMode) ShowTurnMode(state.TurnRateMode);
            // While the stick moves the aircraft the fields follow it.
            if (state.StickActive) fillFields = true;
            // The fields start from the values of the aircraft (instructed heading and rate), not while typing in them.
            if (fillFields)
            {
                fillFields = false;
                updating = true;
                try
                {
                    if (!headingBox.IsKeyboardFocused) headingBox.Text = Magnetic(state.TargetHeading, magneticVariation).ToString("000", CultureInfo.InvariantCulture);
                    double rate = state.VerticalMode switch
                    {
                        VerticalMode.GlidePath => best,
                        VerticalMode.Rate => Math.Round(state.CommandedVerticalSpeedFpm / 10) * 10,
                        _ => Math.Round(state.VerticalSpeedFpm / 10) * 10
                    };
                    if (!rateBox.IsKeyboardFocused) rateBox.Text = FormatRate(rate);
                    double target = Qfe ? state.TargetAltitudeFt - Elevation : state.TargetAltitudeFt;
                    if (!altitudeBox.IsKeyboardFocused) altitudeBox.Text = (Math.Round(target / 100) * 100).ToString("0", CultureInfo.InvariantCulture);
                    if (!speedBox.IsKeyboardFocused) speedBox.Text = state.TargetSpeedKt.ToString("0", CultureInfo.InvariantCulture);
                    renameBox.Text = state.Callsign;
                    int index = Array.FindIndex(VerticalRates, r => Math.Abs(r - state.SelectedVerticalRateFpm) < 1);
                    if (index >= 0) altitudeRateBox.SelectedIndex = index;
                }
                finally
                {
                    updating = false;
                }
            }
        }

        // ----- Helpers -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Mouse wheel over a number field: changes it by a step (+Shift: a bigger step) within limits; then
        /// <paramref name="give"/> (the instruction), if any. An empty field starts from <paramref name="start"/>.
        /// </summary>
        private void Wheel(TextBox box, double step, double shiftStep, double min, double max, string format, Action? give, Func<double>? start = null)
        {
            box.PreviewMouseWheel += (s, e) =>
            {
                double value = TryRead(box, out double current) ? current : start?.Invoke() ?? 0;
                double size = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? shiftStep : step;
                double next = Math.Clamp(Math.Round((value + (e.Delta > 0 ? size : -size)) / step) * step, min, max);
                box.Text = next.ToString(format, CultureInfo.InvariantCulture);
                box.CaretIndex = box.Text.Length;
                give?.Invoke();
                e.Handled = true;
            };
        }

        /// <summary>Magnetic heading, 1–360 (as on a compass: 360, never 000).</summary>
        private static int Magnetic(double trueHeading, double magneticVariation)
        {
            int degrees = (int)Math.Round(SimEngine.Normalize(trueHeading - magneticVariation), MidpointRounding.AwayFromZero) % 360;
            return degrees == 0 ? 360 : degrees;
        }

        private static string FormatRate(double feetPerMinute) => feetPerMinute.ToString("+0;-0;0", CultureInfo.InvariantCulture);

        private static string Rounded(double feetPerMinute)
        {
            double value = Math.Round(feetPerMinute / 10) * 10;
            return value.ToString("+0;−0;0", CultureInfo.InvariantCulture);
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

        private static RepeatButton StepKey(string text, string tip) => new()
        {
            Content = text,
            ToolTip = tip,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(4, 3, 4, 3),
            MinWidth = 36,
            Delay = 400,
            Interval = 120
        };

        private static TextBlock FieldCaption(string text) => new()
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3)
        };

        private static TextBlock FieldHint(string text) => new()
        {
            Text = text,
            FontSize = 11,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 6)
        };

        private static TextBlock ReadoutCaption(string text) => new()
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ReadoutLabel,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private static TextBlock ReadoutNumber() => new()
        {
            Text = "---",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = ReadoutText,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private static Border Readout(TextBlock caption, TextBlock value, string tip)
        {
            StackPanel stack = new();
            stack.Children.Add(caption);
            stack.Children.Add(value);
            return new Border { Background = ReadoutBack, Padding = new Thickness(4, 3, 4, 4), Margin = new Thickness(1, 0, 1, 0), Child = stack, ToolTip = tip };
        }

        private static TextBlock PadText(string text, double left, double top)
        {
            TextBlock block = new() { Text = text, Foreground = Brushes.Gray, FontSize = 10, IsHitTestVisible = false };
            Canvas.SetLeft(block, left);
            Canvas.SetTop(block, top);
            return block;
        }
    }
}
