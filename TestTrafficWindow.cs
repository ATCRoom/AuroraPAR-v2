using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// Window of the test traffic (key T). Top: new aircraft (distance, speed, SSR, initial offsets), the wind and the
    /// list. Control: the readouts the instructor needs while flying (best vertical speed of the glide path, actual
    /// vertical speed, actual turn rate, heading, drift), the turn rate (1.5°/s, 3°/s, Free), the rate of descent keys
    /// (reduce / normal / increase, in steps aligned on the best vertical speed) beside the stick, ▲ UP / ▼ DN and
    /// ◀ L / Final CRS / R ▶ under it, and the heading given by the controller. Closing the window removes the test
    /// traffic.
    /// </summary>
    internal sealed class TestTrafficWindow : Window, IJoystickTarget
    {
        private const double PadSize = 150;
        private const double KnobSize = 22;
        private const double PadReach = (PadSize - KnobSize) / 2;
        private static readonly Brush ReadoutBack = Frozen(Color.FromRgb(0x1B, 0x1C, 0x19));
        private static readonly Brush ReadoutText = Frozen(Color.FromRgb(0xFF, 0xB8, 0x40));
        private static readonly Brush ReadoutLabel = Frozen(Color.FromRgb(0x9A, 0x9A, 0x90));
        private static readonly Brush SelectedBack = Frozen(Color.FromRgb(0x00, 0x78, 0xD4));
        /// <summary>Wind in a METAR: 27015KT, 27015G25KT, VRB03KT, 27008MPS.</summary>
        private static readonly Regex MetarWind = new(@"\b(\d{3}|VRB)(\d{2,3})(?:G(\d{2,3}))?(KT|MPS|KMH)\b");

        private readonly TestTraffic traffic;
        private readonly Func<double> variation;
        private readonly Func<string?> metar;
        private readonly TextBox distanceBox = NumberBox("10");
        private readonly TextBox speedBox = NumberBox("140");
        private readonly TextBox squawkBox = NumberBox("7001");
        private readonly TextBox lateralBox = NumberBox("0");
        private readonly TextBox heightBox = NumberBox("0");
        private readonly ComboBox startBox = new() { Width = 130, Margin = new Thickness(4, 0, 0, 0), ItemsSource = new[] { "On final", "30° from left", "30° from right", "45° from left", "45° from right", "90° from left", "90° from right" }, SelectedIndex = 0, ToolTip = "Start: on the final course (as before), or heading towards the point of the centreline at the Distance (NM), with this intercept angle, about 3 NM off the centreline and level below the glide path (the height offset is measured at that point). The Normal button then starts the descent." };
        private readonly TextBox windFromBox = NumberBox("0");
        private readonly TextBox windSpeedBox = NumberBox("0");
        private readonly TextBox windGustBox = NumberBox("0");
        private readonly TextBlock windMagnetic = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 4, 0) };
        private readonly TextBox headingBox = NumberBox("");
        private readonly TextBox rateBox = NumberBox("");
        private readonly TextBox reactionBox = NumberBox("0.5");
        private readonly Button finalCourseButton = new() { Content = "Final CRS", FontWeight = FontWeights.SemiBold, Padding = new Thickness(2, 3, 2, 3), ToolTip = "The final course in the heading box, carried out after the pilot reaction (with a crosswind the aircraft then drifts: correct the heading)." };
        private readonly Button normalButton = new() { Content = "Normal", FontWeight = FontWeights.SemiBold, Padding = new Thickness(2, 3, 2, 3), ToolTip = "Normal rate of descent: the best rate for this GP and ground speed, kept also when the speed, heading or wind change; after the pilot reaction. The offset from the glide path stays." };
        /// <summary>Pilot reaction: instructions are carried out this long after they are given.</summary>
        private readonly DispatcherTimer headingReaction = new();
        private readonly DispatcherTimer rateReaction = new();
        private string? pendingHeadingCallsign;
        private string? pendingRateCallsign;
        private bool pendingNormal;
        /// <summary>The fields are filled with the values of the selected aircraft at the next refresh.</summary>
        private bool fillFields = true;
        private readonly ListBox list = new() { Height = 64, Width = 110 };
        private readonly TextBlock info = new() { Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock bestLabel = ReadoutCaption("BEST VS");
        private readonly TextBlock bestValue = ReadoutNumber();
        private readonly TextBlock actualValue = ReadoutNumber();
        private readonly TextBlock turnValue = ReadoutNumber();
        private readonly TextBlock headingValue = ReadoutNumber();
        private readonly TextBlock driftValue = ReadoutNumber();
        private readonly Button[] turnButtons = new Button[3];
        private readonly CheckBox autoCheck = new() { Content = "Auto (intercepts centreline and GP)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        private readonly CheckBox auroraCheck = new()
        {
            Content = "Aurora-like data (positions every 0.5 s, altitude in steps)",
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "As the real data from Aurora, to test the track smoothing. Off: perfect positions."
        };
        private readonly Button pauseButton = SmallButton("Pause");
        private readonly Canvas pad = new() { Width = PadSize, Height = PadSize, Background = Frozen(Color.FromRgb(0x22, 0x24, 0x22)), Cursor = Cursors.Hand };
        private readonly Ellipse knob = new() { Width = KnobSize, Height = KnobSize, Fill = Frozen(Color.FromRgb(0x70, 0xD0, 0x70)), Stroke = Brushes.Black, IsHitTestVisible = false };
        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(0.25) };
        private bool updatingList;

        /// <summary>Rain clutter forced for the test (null: from the METAR) and CLUTTER filter step 0 to 10.</summary>
        public event Action<RainLevel?>? RainChanged;
        public event Action<int>? ClutterFilterChanged;
        private readonly ComboBox rainBox = new() { Width = 120, Margin = new Thickness(4, 0, 0, 0), ItemsSource = new[] { "From METAR", "None", "Light", "Moderate", "Heavy", "Thunderstorm" }, SelectedIndex = 0, ToolTip = "Rain clutter of the analog scope: from the METAR of the airport, or forced to try it (also with the option off in the Settings)." };
        private readonly Slider filterSlider = new() { Width = 110, Minimum = 0, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "CLUTTER filter (as the knob of the analog console): reduces the clutter, but dims the tracks too." };

        /// <summary>Shows the current rain choice (index of the list: 0 METAR, 1 None...) and filter step.</summary>
        public void SetRain(int index, int filterStep)
        {
            rainBox.SelectedIndex = Math.Clamp(index, 0, 5);
            filterSlider.Value = Math.Clamp(filterStep, 0, 10);
        }

        internal TestTrafficWindow(TestTraffic traffic, Action<Window>? openJoystick = null, Func<double>? variation = null, Func<string?>? metar = null)
        {
            this.traffic = traffic;
            this.variation = variation ?? (() => 0);
            this.metar = metar ?? (() => null);
            Title = "Aurora PAR - Test traffic";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            StackPanel root = new() { Margin = new Thickness(10), Width = 486 };

            // New aircraft.
            StackPanel newPanel = new();
            WrapPanel first = new();
            first.Children.Add(Caption("Distance"));
            first.Children.Add(distanceBox);
            first.Children.Add(Caption("NM   Speed"));
            first.Children.Add(speedBox);
            first.Children.Add(Caption("kt   SSR A"));
            first.Children.Add(squawkBox);
            newPanel.Children.Add(first);
            DockPanel second = new() { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
            Button addButton = new() { Content = "Add", Padding = new Thickness(14, 2, 14, 2), FontWeight = FontWeights.SemiBold };
            addButton.Click += (s, e) => AddPlane();
            DockPanel.SetDock(addButton, Dock.Right);
            second.Children.Add(addButton);
            StackPanel offsets = new() { Orientation = Orientation.Horizontal };
            offsets.Children.Add(Caption("Offset"));
            offsets.Children.Add(lateralBox);
            offsets.Children.Add(Caption("m (+R/−L)"));
            offsets.Children.Add(heightBox);
            offsets.Children.Add(Caption("ft (+above/−below GP)"));
            second.Children.Add(offsets);
            newPanel.Children.Add(second);
            StackPanel startRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            startRow.Children.Add(Caption("Start"));
            startRow.Children.Add(startBox);
            startRow.Children.Add(Caption("aimed at the Distance, level below the GP"));
            newPanel.Children.Add(startRow);
            startBox.SelectionChanged += (s, e) =>
            {
                bool intercept = startBox.SelectedIndex > 0;
                lateralBox.IsEnabled = !intercept;
                if (intercept && heightBox.Text.Trim() == "0") heightBox.Text = "-300";
                else if (!intercept && heightBox.Text.Trim() == "-300") heightBox.Text = "0";
            };
            root.Children.Add(Group("New aircraft", newPanel));

            // Wind (all the test aircraft).
            WrapPanel windPanel = new();
            windPanel.Children.Add(Caption("From"));
            windPanel.Children.Add(windFromBox);
            windPanel.Children.Add(Caption("°T"));
            windPanel.Children.Add(windMagnetic);
            windPanel.Children.Add(windSpeedBox);
            windPanel.Children.Add(Caption("kt   gusts +"));
            windPanel.Children.Add(windGustBox);
            windPanel.Children.Add(Caption("kt"));
            Button fromMetar = new() { Content = "From METAR", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1), ToolTip = "Copies the wind of the METAR of the airport in use (received from Aurora). A METAR gives the direction in degrees true." };
            fromMetar.Click += (s, e) => WindFromMetar();
            windPanel.Children.Add(fromMetar);
            windFromBox.Text = traffic.WindFrom.ToString("000", CultureInfo.InvariantCulture);
            windSpeedBox.Text = traffic.WindSpeed.ToString("0", CultureInfo.InvariantCulture);
            windGustBox.Text = traffic.WindGust.ToString("0", CultureInfo.InvariantCulture);
            foreach (TextBox box in new[] { windFromBox, windSpeedBox, windGustBox }) box.TextChanged += (s, e) => ApplyWind();
            GroupBox windGroup = Group("Wind (rough: the same at all heights)", windPanel);
            windGroup.ToolTip = "The aircraft fly their heading through the air: a crosswind makes them drift off the centreline unless the heading is corrected, a headwind lowers the ground speed and so the rate of descent on the glide path. Gusts: the wind grows at random up to this much more.";
            root.Children.Add(windGroup);

            // Rain clutter of the analog scope.
            WrapPanel rainPanel = new();
            rainPanel.Children.Add(Caption("Rain"));
            rainPanel.Children.Add(rainBox);
            rainPanel.Children.Add(Caption("   CLUTTER filter"));
            rainPanel.Children.Add(filterSlider);
            rainBox.SelectionChanged += (s, e) => RainChanged?.Invoke(rainBox.SelectedIndex <= 0 ? null : (RainLevel)(rainBox.SelectedIndex - 1));
            filterSlider.ValueChanged += (s, e) => ClutterFilterChanged?.Invoke((int)filterSlider.Value);
            root.Children.Add(Group("Rain clutter (analog scope)", rainPanel));

            // Aircraft.
            StackPanel aircraftPanel = new() { Orientation = Orientation.Horizontal };
            aircraftPanel.Children.Add(list);
            aircraftPanel.Children.Add(info);
            list.SelectionChanged += (s, e) =>
            {
                // Keys or stick held on the previous aircraft: let it go. The joystick held over goes to the aircraft now selected.
                ReleaseManual();
                ApplyJoystick();
                fillFields = true;
                if (!updatingList) ShowSelected();
            };
            // Remove keys at the top of the Aircraft panel.
            StackPanel aircraftGroup = new();
            StackPanel removeRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            Button remove = SmallButton("Remove");
            remove.Click += (s, e) =>
            {
                if (list.SelectedItem is string callsign) traffic.Remove(callsign);
                RefreshList();
            };
            removeRow.Children.Add(remove);
            Button removeAll = SmallButton("Remove all");
            removeAll.Click += (s, e) =>
            {
                traffic.Clear();
                RefreshList();
            };
            removeRow.Children.Add(removeAll);
            aircraftGroup.Children.Add(removeRow);
            aircraftGroup.Children.Add(aircraftPanel);
            root.Children.Add(Group("Aircraft", aircraftGroup));

            // Control: readouts, then the instructions of the controller (left) and the manual stick (right).
            StackPanel control = new();
            Grid readouts = new() { Background = ReadoutBack, Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < 5; i++) readouts.ColumnDefinitions.Add(new ColumnDefinition());
            AddReadout(readouts, 0, bestLabel, bestValue, "ft/min");
            AddReadout(readouts, 1, ReadoutCaption("ACTUAL VS"), actualValue, "ft/min");
            AddReadout(readouts, 2, ReadoutCaption("TURN"), turnValue, "°/s");
            AddReadout(readouts, 3, ReadoutCaption("HDG"), headingValue, "°M");
            AddReadout(readouts, 4, ReadoutCaption("DRIFT"), driftValue, "° (wind)");
            control.Children.Add(readouts);

            Grid columns = new();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Instructions: heading (◀ L, box, R ▶) and rate (▲ UP, box, ▼ DN), carried out after the pilot reaction.
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
            RepeatButton up = StepKey("▲ UP", "Rate 50 ft/min up: less descent (held: repeats; stops once at the best rate). Carried out after the pilot reaction.");
            up.Click += (s, e) => StepRate(RateKeyStep);
            up.HorizontalAlignment = HorizontalAlignment.Stretch;
            rateColumn.Children.Add(up);
            rateBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            rateBox.Margin = new Thickness(0, 3, 0, 3);
            rateColumn.Children.Add(rateBox);
            RepeatButton down = StepKey("▼ DN", "Rate 50 ft/min down: more descent (held: repeats; stops once at the best rate). Carried out after the pilot reaction.");
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
                box.Margin = box == headingBox ? new Thickness(3, 0, 3, 0) : box.Margin;
            }
            headingBox.MaxLength = 3;
            headingBox.ToolTip = "Heading given by the controller, magnetic. Mouse wheel: 1° per step (+Shift: 10°). Enter: carry out. The aircraft turns the shortest way after the pilot reaction.";
            rateBox.MaxLength = 5;
            rateBox.ToolTip = "Rate (vertical speed) given by the controller, ft/min (negative = descent). Mouse wheel: 100 ft/min per step (+Shift: 500), stopping once at the best rate. Enter: carry out, after the pilot reaction.";
            headingBox.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                GiveHeading();
                e.Handled = true;
            };
            rateBox.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                GiveRate(normal: false);
                e.Handled = true;
            };
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
            headingReaction.Tick += (s, e) =>
            {
                headingReaction.Stop();
                TurnToHeading(pendingHeadingCallsign);
            };
            rateReaction.Tick += (s, e) =>
            {
                rateReaction.Stop();
                ApplyRate(pendingRateCallsign, pendingNormal);
            };

            Border wheelNote = new()
            {
                Background = Frozen(Color.FromRgb(0xEE, 0xF5, 0xFC)),
                BorderBrush = Frozen(Color.FromRgb(0xB9, 0xD5, 0xEF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 8, 0, 0),
                Child = new TextBlock
                {
                    Text = "Mouse wheel over the HEADING and RATE fields changes the value: heading 1° (+Shift 10°), rate 100 ft/min (+Shift 500).",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    Foreground = Frozen(Color.FromRgb(0x00, 0x4C, 0x8C))
                }
            };
            instructions.Children.Add(wheelNote);
            StackPanel reactionRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            reactionRow.ToolTip = "Pilots are not instantaneous: every instruction (heading, rate, Final CRS, Normal) is carried out this long after it is given; with the wheel or held keys, after the last step (0 to 5 s).";
            reactionRow.Children.Add(Caption("Pilot reaction"));
            reactionRow.Children.Add(reactionBox);
            reactionRow.Children.Add(Caption("s"));
            instructions.Children.Add(reactionRow);
            GroupBox instructionsGroup = Group("Instructions (controller)", instructions);
            instructionsGroup.Margin = new Thickness(0, 0, 8, 0);
            columns.Children.Add(instructionsGroup);

            // Manual: turn rate and the stick (also a real joystick), at once.
            StackPanel manual = new() { Width = PadSize + 4 };
            manual.Children.Add(new TextBlock { Text = "Turn rate", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            UniformGrid turnRow = new() { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            (string Text, TestTurnMode Mode, string Tip)[] modes =
            [
                ("1.5°/s", TestTurnMode.Half, "Half rate: 1.5°/s with the stick at its edge, and for the heading instructions."),
                ("3°/s", TestTurnMode.Standard, "Rate one: 3°/s with the stick at its edge, and for the heading instructions."),
                ("Free", TestTurnMode.Free, "The longer the stick is held at its edge, the faster the turn (up to 10°/s); heading instructions at 3°/s.")
            ];
            for (int i = 0; i < modes.Length; i++)
            {
                TestTurnMode mode = modes[i].Mode;
                Button button = new() { Content = modes[i].Text, ToolTip = modes[i].Tip, Padding = new Thickness(2, 2, 2, 2) };
                button.Click += (s, e) =>
                {
                    traffic.TurnMode = mode;
                    ShowTurnMode();
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
            GroupBox controlGroup = Group("Control", control);
            root.Children.Add(controlGroup);

            // Service buttons.
            WrapPanel service = new() { Margin = new Thickness(0, 2, 0, 0) };
            Button onPath = SmallButton("On GP / CL");
            onPath.ToolTip = "Puts the aircraft exactly on the centreline and the glide path at once, with the heading that holds the final course in this wind.";
            onPath.Click += (s, e) => OnPath();
            service.Children.Add(onPath);
            autoCheck.Click += (s, e) => ChangeSelected(p =>
            {
                p.Auto = autoCheck.IsChecked == true;
                p.TargetHeading = double.NaN;
                fillFields = true;
            });
            service.Children.Add(autoCheck);
            pauseButton.Click += (s, e) => TogglePause();
            service.Children.Add(pauseButton);
            Button joystickButton = SmallButton("Joystick...");
            joystickButton.ToolTip = "A real joystick or gamepad for the test aircraft and the antenna tilt: device, buttons, live test.";
            joystickButton.Click += (s, e) => openJoystick?.Invoke(this);
            if (openJoystick != null) service.Children.Add(joystickButton);
            root.Children.Add(service);

            root.Children.Add(auroraCheck);
            auroraCheck.IsChecked = traffic.AuroraLike;
            auroraCheck.Click += (s, e) => traffic.AuroraLike = auroraCheck.IsChecked == true;
            Content = root;

            ShowTurnMode();
            ApplyWind();
            refresh.Tick += (s, e) => RefreshList();
            refresh.Start();
            Closed += (s, e) =>
            {
                refresh.Stop();
                headingReaction.Stop();
                rateReaction.Stop();
                traffic.Clear();
                traffic.Paused = false;
            };
            RefreshList();
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
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = content
        };

        private static TextBox NumberBox(string text) => new()
        {
            Text = text,
            Width = 42,
            Height = 22,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        /// <summary>Steps of the rate keys (ft/min).</summary>
        private const double RateKeyStep = 50;

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

        /// <summary>Aircraft moved by the keys or the stick of the window (released there, even if the selection changed).</summary>
        private string? manualCallsign;

        /// <summary>The selection changed while the stick was held: the previous aircraft keeps its heading and vertical speed.</summary>
        private void ReleaseManual()
        {
            if (manualCallsign == null || manualCallsign == list.SelectedItem as string) return;
            traffic.Change(manualCallsign, p =>
            {
                p.StickX = 0;
                p.StickY = 0;
                p.StickHeld = 0;
            });
            manualCallsign = null;
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
        }

        private static Button SmallButton(string text) => new() { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 2, 8, 2) };

        private static TextBlock Caption(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };

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

        private static void AddReadout(Grid grid, int column, TextBlock caption, TextBlock value, string unit)
        {
            StackPanel cell = new() { Margin = new Thickness(2, 6, 2, 6) };
            cell.Children.Add(caption);
            cell.Children.Add(value);
            cell.Children.Add(new TextBlock { Text = unit, FontSize = 10, Foreground = ReadoutLabel, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        private static TextBlock PadText(string text, double x, double y)
        {
            TextBlock block = new() { Text = text, Foreground = Brushes.Gray, FontSize = 10, IsHitTestVisible = false };
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, y);
            return block;
        }

        private static bool TryNumber(TextBox box, double min, double max, out double value)
        {
            return double.TryParse(box.Text.Trim().Replace(',', '.').Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value >= min && value <= max;
        }

        /// <summary>Wind of the boxes to the traffic (each box only when valid); the magnetic direction beside it.</summary>
        private void ApplyWind()
        {
            if (TryNumber(windFromBox, 0, 360, out double from)) traffic.WindFrom = from % 360;
            if (TryNumber(windSpeedBox, 0, 100, out double speed)) traffic.WindSpeed = speed;
            if (TryNumber(windGustBox, 0, 50, out double gust)) traffic.WindGust = gust;
            ShowWindMagnetic();
        }

        /// <summary>Magnetic direction of the wind (the variation follows the runway in use).</summary>
        private void ShowWindMagnetic()
        {
            int magnetic = (int)Math.Round(traffic.WindFrom - variation(), MidpointRounding.AwayFromZero);
            magnetic = (magnetic % 360 + 360) % 360;
            windMagnetic.Text = $"({(magnetic == 0 ? 360 : magnetic):000}°M)";
        }

        private void WindFromMetar()
        {
            Match match = metar() is string text ? MetarWind.Match(text) : Match.Empty;
            if (!match.Success)
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show(this, "No METAR wind received from Aurora for the airport in use.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            double factor = match.Groups[4].Value switch
            {
                "MPS" => 1.94384,
                "KMH" => 0.539957,
                _ => 1
            };
            double speed = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * factor;
            double gust = match.Groups[3].Success ? double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * factor - speed : 0;
            // Variable: the direction stays.
            if (match.Groups[1].Value != "VRB") windFromBox.Text = match.Groups[1].Value;
            windSpeedBox.Text = Math.Round(speed).ToString(CultureInfo.InvariantCulture);
            windGustBox.Text = Math.Round(Math.Max(0, gust)).ToString(CultureInfo.InvariantCulture);
            ApplyWind();
        }

        private void AddPlane()
        {
            if (!TryNumber(distanceBox, 0.5, 40, out double distance) || !TryNumber(speedBox, 40, 400, out double speed)
                || !TryNumber(lateralBox, -10000, 10000, out double lateral) || !TryNumber(heightBox, -3000, 5000, out double height))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            string code = squawkBox.Text.Trim();
            string? squawk = code.Length == 4 && code.All(c => c >= '0' && c <= '7') && code != "0000" ? code : null;
            int start = startBox.SelectedIndex;
            double intercept = start <= 0 ? 0 : new[] { 30.0, 45.0, 90.0 }[(start - 1) / 2];
            int side = start <= 0 ? 0 : start % 2 == 1 ? -1 : 1;
            string callsign = traffic.Add(distance, speed, squawk, lateral, height, intercept, side);
            RefreshList();
            list.SelectedItem = callsign;
        }

        /// <summary>An aircraft is selected (the joystick moves it in the automatic role).</summary>
        public bool HasSelection => list.SelectedItem is string;

        /// <summary>Final CRS: the final course in the heading box, carried out after the pilot reaction.</summary>
        private void FinalCourse()
        {
            TestTraffic.Plane? plane = Selected();
            if (plane == null || plane.GroundSpeed <= 0) return;
            headingBox.Text = Magnetic(plane.FinalTrue).ToString("000", CultureInfo.InvariantCulture);
            GiveHeading();
        }

        /// <summary>The selected aircraft (a copy), or null.</summary>
        private TestTraffic.Plane? Selected() => traffic.List().FirstOrDefault(p => p.Callsign == list.SelectedItem as string);

        /// <summary>Magnetic direction 1–360 of a true one.</summary>
        private int Magnetic(double trueDegrees)
        {
            int magnetic = (int)Math.Round(trueDegrees - variation(), MidpointRounding.AwayFromZero);
            magnetic = (magnetic % 360 + 360) % 360;
            return magnetic == 0 ? 360 : magnetic;
        }

        /// <summary>Heading box changed by keys or wheel (degrees, + right), then carried out after the pilot reaction.</summary>
        private void StepHeading(int degrees)
        {
            if (!int.TryParse(headingBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int heading) || heading < 0 || heading > 360)
            {
                if (Selected() is not TestTraffic.Plane plane)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                heading = Magnetic(plane.HeadingTrue);
            }
            heading = ((heading + degrees) % 360 + 360) % 360;
            if (heading == 0) heading = 360;
            headingBox.Text = heading.ToString("000", CultureInfo.InvariantCulture);
            headingBox.CaretIndex = headingBox.Text.Length;
            GiveHeading();
        }

        private double ReactionSeconds => TryNumber(reactionBox, 0, 5, out double seconds) ? seconds : 0.5;

        /// <summary>A heading instruction: carried out after the pilot reaction; a new one (or one more step) replaces it.</summary>
        private void GiveHeading()
        {
            if (list.SelectedItem is not string callsign)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            pendingHeadingCallsign = callsign;
            headingReaction.Stop();
            if (ReactionSeconds <= 0)
            {
                TurnToHeading(callsign);
                return;
            }
            headingReaction.Interval = TimeSpan.FromSeconds(ReactionSeconds);
            headingReaction.Start();
        }

        /// <summary>Turns the aircraft the shortest way to the heading of the box (magnetic).</summary>
        private void TurnToHeading(string? callsign)
        {
            if (!int.TryParse(headingBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int heading) || heading < 0 || heading > 360 || callsign == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            double magneticVariation = variation();
            traffic.Change(callsign, p =>
            {
                p.TargetHeading = TestTraffic.Wrap(heading + magneticVariation - p.FinalTrue);
                p.TargetTurn = 0;
                p.Auto = false;
            });
        }

        /// <summary>
        /// Rate box changed by keys or wheel (ft/min, + up), stopping once at the best rate when a step would cross it;
        /// then carried out after the pilot reaction.
        /// </summary>
        private void StepRate(double step)
        {
            if (Selected() is not TestTraffic.Plane plane)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            double current = TryNumber(rateBox, TestTraffic.MaxDescent, TestTraffic.MaxClimb, out double value) ? value : plane.VerticalSpeed;
            double best = Math.Round(plane.BestVerticalSpeed / 10) * 10;
            double next = current + step;
            if ((current < best && next > best) || (current > best && next < best)) next = best;
            next = Math.Clamp(next, TestTraffic.MaxDescent, TestTraffic.MaxClimb);
            rateBox.Text = FormatRate(next);
            rateBox.CaretIndex = rateBox.Text.Length;
            GiveRate(normal: false);
        }

        /// <summary>Normal rate of descent: the best rate in the box, kept while speed, heading or wind change.</summary>
        private void NormalRate()
        {
            if (Selected() is not TestTraffic.Plane plane) return;
            rateBox.Text = FormatRate(Math.Round(plane.BestVerticalSpeed / 10) * 10);
            GiveRate(normal: true);
        }

        private static string FormatRate(double feetPerMinute) => feetPerMinute.ToString("+0;-0;0", CultureInfo.InvariantCulture);

        /// <summary>A rate instruction: carried out after the pilot reaction; a new one replaces it.</summary>
        private void GiveRate(bool normal)
        {
            if (list.SelectedItem is not string callsign)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            pendingRateCallsign = callsign;
            pendingNormal = normal;
            rateReaction.Stop();
            if (ReactionSeconds <= 0)
            {
                ApplyRate(callsign, normal);
                return;
            }
            rateReaction.Interval = TimeSpan.FromSeconds(ReactionSeconds);
            rateReaction.Start();
        }

        private void ApplyRate(string? callsign, bool normal)
        {
            if (callsign == null) return;
            if (normal)
            {
                traffic.Change(callsign, p =>
                {
                    p.HoldGlidePath = true;
                    p.Auto = false;
                });
                return;
            }
            if (!TryNumber(rateBox, TestTraffic.MaxDescent, TestTraffic.MaxClimb, out double rate))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            traffic.Change(callsign, p =>
            {
                p.VerticalSpeed = rate;
                p.HoldGlidePath = false;
                p.Auto = false;
            });
        }

        private void OnPath() => ChangeSelected(p =>
        {
            fillFields = true;
            p.Lateral = 0;
            p.Height = double.NaN;
            p.VerticalSpeed = 0;
            p.Heading = p.CrabHeading;
            p.TurnRate = 0;
            p.TargetHeading = double.NaN;
            p.HoldGlidePath = true;
        });

        private void TogglePause()
        {
            traffic.Paused = !traffic.Paused;
            pauseButton.Content = traffic.Paused ? "Resume" : "Pause";
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
                case JoystickCommand.OnPath: OnPath(); break;
                case JoystickCommand.Pause: TogglePause(); break;
                case JoystickCommand.TurnRate:
                    traffic.TurnMode = traffic.TurnMode switch
                    {
                        TestTurnMode.Half => TestTurnMode.Standard,
                        TestTurnMode.Standard => TestTurnMode.Free,
                        _ => TestTurnMode.Half
                    };
                    ShowTurnMode();
                    break;
                case JoystickCommand.NextAircraft:
                    if (list.ItemsSource is List<string> callsigns && callsigns.Count > 0)
                    {
                        int index = list.SelectedItem is string selected ? callsigns.IndexOf(selected) : -1;
                        list.SelectedItem = callsigns[(index + 1) % callsigns.Count];
                    }
                    break;
            }
        }

        // Joystick deflection last given (pad: x right, y up) and the aircraft it moves.
        private double joystickX;
        private double joystickY;
        private string? joystickCallsign;

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

        private void ApplyJoystick()
        {
            if (pad.IsMouseCaptured) return;
            string? selected = list.SelectedItem as string;
            // Another aircraft was moved: let it go first.
            if (joystickCallsign != null && joystickCallsign != selected)
            {
                traffic.Change(joystickCallsign, p =>
                {
                    p.StickX = 0;
                    p.StickY = 0;
                    p.StickHeld = 0;
                });
                joystickCallsign = null;
                CentreKnob();
            }
            if (selected == null) return;
            double x = joystickX;
            double y = joystickY;
            if (x == 0 && y == 0)
            {
                if (joystickCallsign == null) return;
                joystickCallsign = null;
                fillFields = true;
                CentreKnob();
                traffic.Change(selected, p =>
                {
                    p.StickX = 0;
                    p.StickY = 0;
                    p.StickHeld = 0;
                });
                return;
            }
            joystickCallsign = selected;
            ShowKnob(x, y);
            traffic.Change(selected, p =>
            {
                p.StickX = x;
                p.StickY = y;
                p.Auto = false;
                if (x != 0) p.TargetHeading = double.NaN;
                if (y != 0) p.HoldGlidePath = false;
            });
        }

        private void ChangeSelected(Action<TestTraffic.Plane> change)
        {
            if (list.SelectedItem is string callsign) traffic.Change(callsign, change);
        }

        private void ShowTurnMode()
        {
            TestTurnMode[] modes = [TestTurnMode.Half, TestTurnMode.Standard, TestTurnMode.Free];
            for (int i = 0; i < turnButtons.Length; i++)
            {
                bool on = traffic.TurnMode == modes[i];
                if (on)
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

        /// <summary>The list follows the aircraft (landed ones go away); the selection stays.</summary>
        private void RefreshList()
        {
            List<string> callsigns = traffic.List().Select(p => p.Callsign).ToList();
            if (list.ItemsSource is not List<string> current || !current.SequenceEqual(callsigns))
            {
                string? selected = list.SelectedItem as string;
                updatingList = true;
                list.ItemsSource = callsigns;
                list.SelectedItem = selected != null && callsigns.Contains(selected) ? selected : callsigns.LastOrDefault();
                updatingList = false;
            }
            ShowSelected();
            ShowWindMagnetic();
        }

        private void ShowSelected()
        {
            TestTraffic.Plane? plane = traffic.List().FirstOrDefault(p => p.Callsign == list.SelectedItem as string);
            if (plane == null)
            {
                finalCourseButton.Content = "Final CRS";
                normalButton.Content = "Normal";
                info.Text = traffic.Count == 0 ? "No test aircraft:\nAdd one above." : "";
                bestValue.Text = actualValue.Text = turnValue.Text = headingValue.Text = driftValue.Text = "---";
                bestLabel.Text = "BEST VS";
                return;
            }
            info.Text = string.Format(CultureInfo.InvariantCulture, "{0}\n{1:0.0} NM from touchdown\n{2:0} kt, ground speed {3:0} kt{4}",
                plane.Callsign, plane.Distance, plane.Speed, plane.GroundSpeed, plane.Squawk != null ? "\nSSR A" + plane.Squawk : "");
            if (!double.IsNaN(plane.GpSeconds)) info.Text += string.Format(CultureInfo.InvariantCulture, "\nGP in {0:0.0} NM (about {1:0} s)", plane.GpRemainingNM, Math.Max(0, plane.GpSeconds));
            bestLabel.Text = string.Format(CultureInfo.InvariantCulture, "BEST VS {0:0.0#}°", plane.GlideSlope);
            bestValue.Text = Rounded(plane.BestVerticalSpeed);
            actualValue.Text = Rounded(plane.VerticalSpeed);
            turnValue.Text = Math.Abs(plane.TurnRate) < 0.05 ? "0.0"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", Math.Abs(plane.TurnRate), plane.TurnRate > 0 ? "R" : "L");
            int magnetic = (int)Math.Round(plane.HeadingTrue - variation(), MidpointRounding.AwayFromZero);
            magnetic = (magnetic % 360 + 360) % 360;
            headingValue.Text = (magnetic == 0 ? 360 : magnetic).ToString("000", CultureInfo.InvariantCulture);
            int drift = (int)Math.Round(plane.Drift);
            driftValue.Text = drift == 0 ? "0" : $"{Math.Abs(drift)} {(drift > 0 ? "R" : "L")}";
            if (autoCheck.IsChecked != plane.Auto) autoCheck.IsChecked = plane.Auto;
            double best = Math.Round(plane.BestVerticalSpeed / 10) * 10;
            if (plane.GroundSpeed > 0)
            {
                finalCourseButton.Content = $"Final CRS ({Magnetic(plane.FinalTrue):000})";
                normalButton.Content = $"Normal ({FormatRate(best).Replace('-', '−')})";
            }
            // The fields start from the values of the aircraft (instructed heading and rate), not while typing in them.
            if (fillFields && plane.GroundSpeed > 0)
            {
                fillFields = false;
                double heading = double.IsNaN(plane.TargetHeading) ? plane.HeadingTrue : plane.FinalTrue + plane.TargetHeading;
                if (!headingBox.IsKeyboardFocused) headingBox.Text = Magnetic(heading).ToString("000", CultureInfo.InvariantCulture);
                if (!rateBox.IsKeyboardFocused) rateBox.Text = FormatRate(plane.HoldGlidePath ? best : Math.Round(plane.VerticalSpeed / 10) * 10);
            }
        }

        private static string Rounded(double feetPerMinute)
        {
            double value = Math.Round(feetPerMinute / 10) * 10;
            return value.ToString("+0;−0;0", CultureInfo.InvariantCulture);
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
            if (list.SelectedItem is not string) return;
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
            manualCallsign = list.SelectedItem as string;
            ChangeSelected(p =>
            {
                p.StickX = x;
                p.StickY = y;
                if (x != 0 || y != 0) p.Auto = false;
                if (x != 0) p.TargetHeading = double.NaN;
                if (y != 0) p.HoldGlidePath = false;
            });
        }

        private void ReleaseKnob()
        {
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
            // The heading and the vertical speed reached stay.
            if ((manualCallsign ?? list.SelectedItem as string) is string released)
            {
                traffic.Change(released, p =>
                {
                    p.StickX = 0;
                    p.StickY = 0;
                    p.StickHeld = 0;
                });
            }
            manualCallsign = null;
            fillFields = true;
            // A real joystick still deflected takes over again.
            if (joystickCallsign != null || joystickX != 0 || joystickY != 0)
            {
                joystickCallsign = null;
                ApplyJoystick();
            }
        }
    }
}
