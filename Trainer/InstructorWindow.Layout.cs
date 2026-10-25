using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the plan view over the whole window and, floating over it, four panels that
    /// are moved by their bar, closed to the bar alone and opened again (<see cref="FloatingPanel"/>; places saved):
    /// SIMULATION (Pause and a summary always on its bar): runway, wind, QNH / QFE, Aurora-like data, pilot reaction,
    ///   callsign prefix, Remove all.
    /// TRAFFIC GENERATION: "Quick" (an aircraft at once: on final, 30° / 45° / 90° from left or right, downwind left
    ///   or right) and "Advanced" (flight plans, scenarios, the route of a flight plan).
    /// AIRCRAFT CONTROL, narrow: the aircraft flying and the commands of the selected one, a row each (heading, turn
    ///   rate, altitude, rate, speed, MANUAL / ROUTE with Next WPT and Direct to, the stick that can be shown, rename).
    /// MINI-PAR: the PAR of the student, resizable.
    /// The controls are those of the window (built by the shared code, with their events): they are moved here.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly TextBlock simulationSummary = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDA)), Margin = new Thickness(8, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250 };
        private readonly ToggleButton[] startButtons = new ToggleButton[9];
        private int quickStart;
        private readonly Border stickArea = new();
        private readonly Button stickToggle = new() { Padding = new Thickness(6, 1, 6, 1), Focusable = false };
        /// <summary>Under the commands, only in ROUTE: the route's altitudes and speeds are flown until one is given.</summary>
        private readonly TextBlock routeValuesHint = new() { FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) };
        private readonly TextBlock altitudeUnit = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) };
        private FrameworkElement? controlArea;

        /// <summary>Takes an element out of the panel or border it is in.</summary>
        private static T Detach<T>(T element) where T : FrameworkElement
        {
            switch (element.Parent)
            {
                case Panel panel: panel.Children.Remove(element); break;
                case Decorator decorator: decorator.Child = null; break;
                case ContentControl content: content.Content = null; break;
            }
            return element;
        }

        /// <summary>A field with its name and unit (or a few buttons) in one piece: a narrow panel wraps between the pieces, never inside one.</summary>
        private static StackPanel Piece(params UIElement[] parts)
        {
            StackPanel piece = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 3) };
            foreach (UIElement part in parts) piece.Children.Add(part);
            return piece;
        }

        /// <summary>The parts of a wrapping row put again in pieces (a field with its name and unit together).</summary>
        private static void Regroup(WrapPanel row, params int[] sizes)
        {
            List<UIElement> parts = row.Children.Cast<UIElement>().ToList();
            row.Children.Clear();
            int at = 0;
            foreach (int size in sizes)
            {
                if (at >= parts.Count) break;
                row.Children.Add(Piece(parts.Skip(at).Take(size).ToArray()));
                at += size;
            }
            foreach (UIElement rest in parts.Skip(at)) row.Children.Add(rest);
        }

        /// <summary>A row of the narrow panel: its name on the left (a fixed column), then the controls.</summary>
        private static DockPanel Line(string name, params UIElement[] children)
        {
            DockPanel line = new() { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
            TextBlock caption = new() { Text = name, Width = 40, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left);
            line.Children.Add(caption);
            WrapPanel row = new() { VerticalAlignment = VerticalAlignment.Center };
            foreach (UIElement child in children)
            {
                if (child is FrameworkElement element && element.Margin == default) element.Margin = new Thickness(0, 0, 3, 0);
                row.Children.Add(child);
            }
            line.Children.Add(row);
            return line;
        }

        /// <summary>The place of a panel (saved by name) and what saves it.</summary>
        private PanelPlacement Placement(string name, bool open)
        {
            settings.Panels ??= new Dictionary<string, PanelPlacement>(StringComparer.OrdinalIgnoreCase);
            if (!settings.Panels.TryGetValue(name, out PanelPlacement? placement) || placement == null)
            {
                placement = new PanelPlacement { Open = open };
                settings.Panels[name] = placement;
            }
            return placement;
        }

        /// <summary>The plan view (with its CENTRE selector) and, over it, the floating panels.</summary>
        private UIElement BuildTrainerView(PlanView view, MiniParView par)
        {
            Grid root = new();
            Grid planHost = new();
            planHost.Children.Add(view);
            CentreBar centre = new(view);
            centre.PlaceTop();
            view.ShowBeam = settings.BeamShown;
            centre.AddSwitch("BEAM", "Where the student's PAR sees the aircraft (from above): the azimuth beam filled, the scan limits dashed. The height limits are in the TRAINEE PAR.",
                settings.BeamShown, on => { settings.BeamShown = on; save(); view.ShowBeam = on; view.InvalidateVisual(); });
            planHost.Children.Add(centre);
            root.Children.Add(planHost);
            // The panels: on a canvas over the view (no background: a click beside the panels reaches the view).
            Canvas overlay = new() { ClipToBounds = true };
            root.Children.Add(overlay);
            const double left = 10;
            const double first = 52;
            const double step = 30;

            // ----- Simulation ------------------------------------------------------------------------------------------
            StackPanel simulation = new() { Margin = new Thickness(0, 0, 0, 2) };
            simulation.Children.Add(Detach((FrameworkElement)referenceText.Parent));
            if (windFromBox.Parent is WrapPanel windRow)
            {
                // "kt   gusts +" in two: the speed and the gusts each with their unit.
                TextBlock? gusts = windRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text.Contains("gusts"));
                if (gusts != null)
                {
                    gusts.Text = "kt";
                    windRow.Children.Insert(windRow.Children.IndexOf(gusts) + 1, Caption("gusts +"));
                }
                // Wind from [°T] (°M), [speed kt], gusts + [kt], From METAR.
                Regroup(windRow, 4, 2, 3);
                simulation.Children.Add(Detach(windRow));
                // The QNH under the wind (Trainer/InstructorWindow.Qnh.cs).
                simulation.Children.Add(BuildQnhRow());
                // From METAR: in the row, or (regrouped) in one of its pieces.
                if ((windRow.Children.OfType<Button>().FirstOrDefault() ?? windRow.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<Button>()).FirstOrDefault()) is Button fromMetar)
                {
                    fromMetar.ToolTip = "The wind (and without Aurora the QNH) of the METAR of the airport: from Aurora, or without Aurora from the internet (aviationweather.gov). A METAR gives the direction in degrees true.";
                }
            }
            if (pauseButton.Parent is WrapPanel optionsRow)
            {
                Detach(pauseButton);
                // QNH, QFE, Aurora-like, the callsign prefix with its box.
                Regroup(optionsRow, 1, 1, 1, 2);
                simulation.Children.Add(Detach(optionsRow));
            }
            if (reactionBox.Parent is StackPanel reactionRow)
            {
                Detach(reactionRow);
                reactionRow.HorizontalAlignment = HorizontalAlignment.Left;
                simulation.Children.Add(reactionRow);
            }
            simulation.Children.Add(Detach(noticeText));
            Button performance = new() { Content = "Aircraft performance…", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "The performance classes (speeds, initial and final speed, climb, descent, turns): a table to change them." };
            performance.Click += (s, e) => OpenPerformanceWindow();
            simulation.Children.Add(performance);
            overlay.Children.Add(new FloatingPanel("SIMULATION SETTINGS", simulation, simulationSummary, 440, Placement("Simulation", false),
                (area, size) => new Point(left, first), save));

            // ----- Traffic generation ----------------------------------------------------------------------------------
            TabControl tabs = new();
            tabs.Items.Add(new TabItem { Header = "Quick (starts)", Content = BuildQuick() });
            StackPanel advanced = new();
            GroupBox plans = BuildFlightPlans();
            UIElement plansContent = (UIElement)plans.Content;
            plans.Content = null;
            advanced.Children.Add(plansContent);
            GroupBox route = BuildRoute();
            routeEditor = (FrameworkElement)route.Content;
            route.Content = null;
            routeEditorHost.Margin = new Thickness(0, 8, 0, 0);
            routeEditorHost.Child = routeEditor;
            advanced.Children.Add(routeEditorHost);
            tabs.Items.Add(new TabItem { Header = "Advanced (flight plans, scenarios)", Content = new Border { Padding = new Thickness(4), Child = advanced } });
            overlay.Children.Add(new FloatingPanel("TRAFFIC GENERATION", tabs, null, 460, Placement("Traffic", false),
                (area, size) => new Point(left, first + step), save));

            // ----- Aircraft control ------------------------------------------------------------------------------------
            StackPanel aircraftBar = new() { Orientation = Orientation.Horizontal };
            aircraftBar.Children.Add(aircraftTitle);
            aircraftBar.Children.Add(KeysButton());
            overlay.Children.Add(new FloatingPanel("AIRCRAFT CONTROL", BuildAircraftControl(), aircraftBar, 376, Placement("Aircraft", true),
                (area, size) => new Point(left, first + 2 * step), save));

            // ----- Traffic list ------------------------------------------------------------------------------------------
            overlay.Children.Add(new FloatingPanel("TRAFFIC LIST", BuildTrafficList(), trafficSummary, 400, Placement("TrafficList", true),
                (area, size) => new Point(area.Width - size.Width - 12, 84), save));

            // ----- Trainee PAR (the mini-PAR) ---------------------------------------------------------------------------
            overlay.Children.Add(new FloatingPanel("TRAINEE PAR", par, null, 640, Placement("MiniPar", true),
                (area, size) => new Point(area.Width - size.Width - 12, area.Height - size.Height - 12), save, resizable: true, height: 260));
            return root;
        }

        private FrameworkElement KeysButton()
        {
            Button keys = new() { Content = "Keys…", Padding = new Thickness(6, 0, 6, 0), Focusable = false, ToolTip = "Keys for the commands of the selected aircraft (also for a macro keyboard), and their steps." };
            keys.Click += (s, e) => OpenKeysWindow();
            return keys;
        }

        // ----- Quick starts ---------------------------------------------------------------------------------------------

        private static readonly string[] StartNames = ["On final", "30° L", "30° R", "45° L", "45° R", "90° L", "90° R", "Downwind L", "Downwind R"];

        private UIElement BuildQuick()
        {
            StackPanel panel = new() { Margin = new Thickness(4) };
            WrapPanel starts = new();
            starts.Children.Add(Caption("Start"));
            for (int i = 0; i < StartNames.Length; i++)
            {
                int index = i;
                ToggleButton button = new() { Content = StartNames[i], Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 3, 3), IsChecked = i == 0 };
                button.ToolTip = i == 0 ? "On the final course, on the centreline, at the distance from touchdown."
                    : i <= 6 ? $"Heading towards the point of the centreline at the distance, with an intercept angle of {StartNames[i][..3]}, from the {(i % 2 == 1 ? "left" : "right")} as seen by the pilot, at the distance off the centreline."
                    : $"Radar downwind: parallel to the runway, heading opposite to the final, on the {(i == 7 ? "left" : "right")} as seen by the pilot, off the centreline by that distance, abeam the distance from touchdown.";
                button.Click += (s, e) => ChooseStart(index);
                startButtons[i] = button;
                starts.Children.Add(button);
            }
            panel.Children.Add(starts);

            Detach(distanceBox);
            Detach(offsetBox);
            Detach(newAltitudeBox);
            Detach(newSpeedBox);
            Detach(newCallsignBox);
            Detach(classBox);
            offsetBox.Text = "3.0";
            offsetBox.ToolTip = "Distance from the extended centreline at the start (NM; 30° / 45° / 90° and downwind). Mouse wheel: 0.1 NM (+Shift: 1).";
            distanceBox.ToolTip = "Distance from touchdown (NM): where the aircraft starts on the final, the point of the centreline it heads for, or where it is abeam on the downwind. Mouse wheel: 0.5 NM (+Shift: 1).";
            WrapPanel row1 = new() { Margin = new Thickness(0, 3, 0, 0) };
            row1.Children.Add(Piece(Caption("Distance"), distanceBox, Caption("NM from touchdown")));
            row1.Children.Add(Piece(Caption("off the centreline"), offsetBox, Caption("NM")));
            row1.Children.Add(Piece(Caption("altitude"), newAltitudeBox, Caption("ft (empty: glide path)")));
            panel.Children.Add(row1);
            WrapPanel row2 = new() { Margin = new Thickness(0, 3, 0, 0) };
            row2.Children.Add(Piece(Caption("Speed"), newSpeedBox, Caption("kt")));
            row2.Children.Add(Piece(Caption("class"), classBox, PerformanceButton()));
            row2.Children.Add(Piece(Caption("callsign"), newCallsignBox));
            panel.Children.Add(row2);
            WrapPanel row3 = new() { Margin = new Thickness(0, 6, 0, 0) };
            Button create = new() { Content = "Create & start", FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), ToolTip = "The aircraft is created and starts flying now." };
            create.Click += (s, e) => CreateQuick(start: true);
            Button toList = new() { Content = "Move to TFC list", FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 3, 12, 3), ToolTip = "The aircraft waits in the TRAFFIC LIST (only you see it, grey) until you start it (▶ or Start all)." };
            toList.Click += (s, e) => CreateQuick(start: false);
            row3.Children.Add(create);
            row3.Children.Add(toList);
            panel.Children.Add(row3);
            panel.Children.Add(new TextBlock { Text = "The aircraft starts level, in MANUAL: heading, rate, altitude and speed are yours (Normal starts the descent).", FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) });
            ChooseStart(0);
            return panel;
        }

        private void ChooseStart(int index)
        {
            quickStart = index;
            for (int i = 0; i < startButtons.Length; i++)
            {
                if (startButtons[i] != null) startButtons[i].IsChecked = i == index;
            }
            offsetBox.IsEnabled = index != 0;
        }

        /// <summary>A new aircraft from the start chosen: flying at once, or waiting in the TRAFFIC LIST.</summary>
        private void CreateQuick(bool start)
        {
            Runway? runway = bridge.Reference;
            if (runway == null) return;
            if (!TryRead(distanceBox, out double distance) || distance < 0.5 || distance > 60)
            {
                Warn("Distance: 0.5 to 60 NM from touchdown.");
                return;
            }
            double off = 0;
            if (quickStart != 0 && (!TryRead(offsetBox, out off) || off < 0.1 || off > 20))
            {
                Warn("Off the centreline: 0.1 to 20 NM.");
                return;
            }
            if (!TryRead(newSpeedBox, out double speed) || speed < 40 || speed > 400)
            {
                Warn("Speed: 40 to 400 kt.");
                return;
            }
            double altitude;
            if (string.IsNullOrWhiteSpace(newAltitudeBox.Text)) altitude = runway.Elevation + runway.GlidePathHeight(distance);
            else if (TryRead(newAltitudeBox, out double typed)) altitude = Qfe ? typed + runway.Elevation : typed;
            else
            {
                Warn("Altitude: a number, or empty for the glide path.");
                return;
            }
            // Side as seen by the pilot on the final: −1 left, +1 right.
            int side = quickStart == 0 ? 0 : quickStart <= 6 ? (quickStart % 2 == 1 ? -1 : 1) : (quickStart == 7 ? -1 : 1);
            double x, y, heading;
            if (quickStart == 0)
            {
                (x, y) = bridge.OnFinal(distance, 0);
                heading = runway.Heading;
            }
            else if (quickStart <= 6)
            {
                double angle = new[] { 30.0, 45.0, 90.0 }[(quickStart - 1) / 2];
                double radians = angle * Math.PI / 180;
                // Back along the track towards the point of the centreline at the distance, the offset away from it.
                double back = angle >= 90 ? 0 : off / Math.Tan(radians);
                (x, y) = bridge.OnFinal(distance + back, side * off);
                heading = SimEngine.Normalize(runway.Heading - side * angle);
            }
            else
            {
                (x, y) = bridge.OnFinal(distance, side * off);
                heading = SimEngine.Normalize(runway.Heading + 180);
            }
            string? callsign = string.IsNullOrWhiteSpace(newCallsignBox.Text) ? null : newCallsignBox.Text;
            if (!start)
            {
                // Waiting in the TRAFFIC LIST: a plan of one point (the start) with its heading.
                string typed = (callsign ?? "").Trim().ToUpperInvariant();
                if (typed.Length > 0 && !ValidCallsign(typed))
                {
                    Warn("Callsign: 2 to 10 letters or digits, or empty for the next free one.");
                    return;
                }
                if (typed.Length > 0 && (CallsignInPlans(typed, null) || bridge.Engine.IsCallsignTaken(typed) || otherCallsigns().Contains(typed, StringComparer.OrdinalIgnoreCase)))
                {
                    typed = NextCallsign(typed);
                }
                FlightPlan waiting = NewPlan(typed, classBox.SelectedItem as string ?? ProposedClass(), [new RoutePoint(x, y, altitude, speed)]);
                waiting.Quick = true;
                waiting.Queued = true;
                waiting.StartHeading = heading;
                flightPlans.Add(waiting);
                newCallsignBox.Text = "";
                trafficShownKey = "";
                Refresh();
                return;
            }
            try
            {
                string name = bridge.Add(x, y, altitude, heading, speed, callsign, otherCallsigns(), classBox.SelectedItem as string);
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

        // ----- Aircraft control -----------------------------------------------------------------------------------------

        private FrameworkElement BuildAircraftControl()
        {
            // As wide as the panel (narrower when its scroll bar shows).
            StackPanel panel = new();
            // The information in two columns (the shared one, with lines that come and go, is not used here).
            Detach(info);
            Grid information = new();
            information.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            information.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(infoRight, 1);
            information.Children.Add(infoLeft);
            information.Children.Add(infoRight);
            panel.Children.Add(information);

            StackPanel control = new();
            if (bestValue.Parent is FrameworkElement stack && stack.Parent is FrameworkElement border && border.Parent is FrameworkElement readouts)
            {
                Detach(readouts);
                readouts.Margin = new Thickness(0, 0, 0, 4);
                control.Children.Add(readouts);
            }

            // Heading and turn rate.
            RepeatButton headingLeft = StepKey("◀", "Heading 1° left (held: repeats). After the pilot reaction. A heading puts the aircraft in MANUAL.");
            headingLeft.Click += (s, e) => StepHeading(-1);
            RepeatButton headingRight = StepKey("▶", "Heading 1° right (held: repeats). After the pilot reaction. A heading puts the aircraft in MANUAL.");
            headingRight.Click += (s, e) => StepHeading(1);
            headingLeft.MinWidth = headingRight.MinWidth = 26;
            Detach(headingBox);
            headingBox.Margin = new Thickness(0, 0, 3, 0);
            Detach(finalCourseButton);
            finalCourseButton.Padding = new Thickness(6, 2, 6, 2);
            control.Children.Add(Line("HDG", headingLeft, headingBox, headingRight, finalCourseButton));
            // 3°/s, FREE, 1.5°/s (the buttons of the shared code: 0 half, 1 rate one, 2 free), STOP TURN.
            List<UIElement> turns = [];
            string[] turnTexts = ["1.5°", "3°", "FREE"];
            foreach (int i in new[] { 1, 2, 0 })
            {
                Button button = Detach(turnButtons[i]);
                button.Content = turnTexts[i];
                button.MinWidth = 38;
                button.Padding = new Thickness(4, 2, 4, 2);
                button.Margin = new Thickness(0, 0, 1, 0);
                turns.Add(button);
            }
            Button stopTurn = new() { Content = "STOP TURN", FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Stop turn: the aircraft rolls out on the heading it has (after the pilot reaction); in ROUTE it goes to MANUAL at once." };
            stopTurn.Click += (s, e) => StopTurn();
            turns.Add(stopTurn);
            control.Children.Add(Line("", turns.ToArray()));

            // Altitude (with the rate to reach it).
            Detach(altitudeBox);
            Detach(altitudeRateBox);
            altitudeBox.Margin = new Thickness(0);
            altitudeRateBox.Margin = new Thickness(0, 0, 3, 0);
            Button giveAltitude = SmallButton("Go");
            giveAltitude.FontWeight = FontWeights.SemiBold;
            giveAltitude.Margin = new Thickness(3, 0, 0, 0);
            giveAltitude.Click += (s, e) => GiveAltitude();
            control.Children.Add(Line("ALT", altitudeBox, altitudeUnit, altitudeRateBox, Caption("ft/min"), giveAltitude));

            // Rate.
            RepeatButton up = StepKey("▲", "Rate up: less descent (held: repeats; stops once at the Normal rate). After the pilot reaction.");
            up.Click += (s, e) => StepRate(RateKeyStep);
            RepeatButton down = StepKey("▼", "Rate down: more descent (held: repeats; stops once at the Normal rate). After the pilot reaction.");
            down.Click += (s, e) => StepRate(-RateKeyStep);
            up.MinWidth = down.MinWidth = 26;
            Detach(rateBox);
            rateBox.Width = 54;
            rateBox.Margin = new Thickness(0, 0, 3, 0);
            Detach(normalButton);
            normalButton.Padding = new Thickness(6, 2, 6, 2);
            Button stopVertical = new() { Content = "STOP", FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 2, 6, 2), ToolTip = "Stop climb / descent: the aircraft levels off where it is (after the pilot reaction). From then on the route's altitudes are not flown." };
            stopVertical.Click += (s, e) => StopVertical();
            control.Children.Add(Line("RATE", up, rateBox, down, normalButton, stopVertical));

            // Speed.
            Detach(speedBox);
            Button giveSpeed = SmallButton("Go");
            giveSpeed.FontWeight = FontWeights.SemiBold;
            giveSpeed.Margin = new Thickness(3, 0, 0, 0);
            giveSpeed.Click += (s, e) => GiveSpeed();
            speedBox.Margin = new Thickness(0);
            Button slowToFinal = new() { Content = "Slow dwn to final", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0), ToolTip = "The final speed of the aircraft's class (Aircraft performance…), after the pilot reaction." };
            slowToFinal.Click += (s, e) => SlowToFinal();
            control.Children.Add(Line("SPD", speedBox, Caption("kt"), giveSpeed, slowToFinal));

            // Navigation (Trainer/InstructorWindow.Navigation.cs).
            control.Children.Add(BuildNavigation());
            routeValuesHint.Text = "In ROUTE the route's altitudes and speeds are flown until you give one.";
            control.Children.Add(routeValuesHint);

            // The stick (shown or not), a real joystick, the name.
            if (pad.Parent is FrameworkElement padBorder)
            {
                Detach(padBorder);
                padBorder.HorizontalAlignment = HorizontalAlignment.Center;
                padBorder.Margin = new Thickness(0, 4, 0, 2);
                stickArea.Child = padBorder;
            }
            stickToggle.Click += (s, e) =>
            {
                settings.StickShown = !settings.StickShown;
                save();
                ShowStick();
            };
            List<UIElement> stickRow = [stickToggle];
            if (openJoystick != null)
            {
                Button joystick = SmallButton("Joystick…");
                joystick.Padding = new Thickness(6, 1, 6, 1);
                joystick.Margin = new Thickness(4, 0, 0, 0);
                joystick.ToolTip = "A real joystick or gamepad: device, buttons, live test.";
                joystick.Click += (s, e) => openJoystick(this);
                stickRow.Add(joystick);
            }
            DockPanel stickLine = Line("STICK", stickRow.ToArray());
            stickLine.Margin = new Thickness(0, 6, 0, 2);
            control.Children.Add(stickLine);
            control.Children.Add(stickArea);
            ShowStick();

            Detach(renameBox);
            renameBox.Margin = new Thickness(0);
            Button rename = SmallButton("Rename");
            rename.Padding = new Thickness(6, 1, 6, 1);
            rename.Margin = new Thickness(3, 0, 0, 0);
            rename.Click += (s, e) => Rename();
            control.Children.Add(Line("NAME", renameBox, rename));
            controlArea = control;
            panel.Children.Add(control);
            panel.Children.Add(new TextBlock
            {
                Text = "Also on the radar: click or wheel on the label, right click on an aircraft (Pause, Rename, Remove…), drag the route points.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 0, 0)
            });
            return panel;
        }

        /// <summary>Stop turn: MANUAL at once (in ROUTE), the roll-out after the pilot reaction.</summary>
        private void StopTurn()
        {
            if (SelectedCallsign is not string callsign) return;
            bridge.Engine.SetNavigation(callsign, false);
            bridge.Engine.StopTurn(callsign);
            fillFields = true;
        }

        /// <summary>Stop climb / descent: level off where it is, after the pilot reaction.</summary>
        private void StopVertical()
        {
            if (SelectedCallsign is not string callsign) return;
            bridge.Engine.StopVertical(callsign);
            fillFields = true;
        }

        private void ShowStick()
        {
            stickToggle.Content = settings.StickShown ? "Hide ▴" : "Show ▾";
            stickToggle.ToolTip = settings.StickShown ? "Hide the stick (the real joystick still works)." : "Show the stick.";
            if (stickArea.Child != null) stickArea.Child.Visibility = settings.StickShown ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The summary on the bar of the Simulation area, the hints of the selected aircraft (each refresh).</summary>
        private void RefreshTrainerLayout(AircraftState? state)
        {
            Runway? runway = bridge.Reference;
            (double from, double windSpeed, double gust) = bridge.Engine.Wind;
            string wind = windSpeed > 0 ? string.Format(CultureInfo.InvariantCulture, "wind {0:000}/{1:0}{2}", from, windSpeed, gust > 0 ? $" G{gust:0}" : "") : "no wind";
            simulationSummary.Text = string.Format(CultureInfo.InvariantCulture, "{0} · {1} · {2} · reaction {3:0.0} s",
                runway?.DisplayName ?? "no runway", wind, Qfe ? "QFE" : "QNH", bridge.Engine.ReactionSeconds);
            if (controlArea != null) controlArea.IsEnabled = state != null;
            bool routeValues = state != null && state.Route != null && state.Navigation != NavigationMode.Manual && (state.RouteAltitude || state.RouteSpeed);
            // At the bottom of the commands: nothing moves when it comes and goes.
            routeValuesHint.Visibility = routeValues ? Visibility.Visible : Visibility.Collapsed;
            altitudeUnit.Text = Qfe ? "QFE at" : "QNH at";
            // While the route turns the aircraft the heading box follows, so that the keys and ◀ L / R ▶ start from the
            // heading it is flying (a heading given puts it in MANUAL at once, and then the box is left as typed).
            if (state != null && state.Navigation != NavigationMode.Manual && !headingBox.IsKeyboardFocused)
            {
                headingBox.Text = Magnetic(state.TargetHeading, variation()).ToString("000", CultureInfo.InvariantCulture);
            }
            ShowInformation(state);
            RefreshTrafficList(state);
            ShowQnh();
            // An instruction carried out by the pilot (one less waiting): the fields show the new values (after STOP TURN,
            // STOP CLIMB/DESCENT, a heading…), so the keys and buttons start from them.
            int pending = state?.PendingCommands ?? 0;
            if (state != null && state.Callsign == pendingFor && pending < lastPending) fillFields = true;
            pendingFor = state?.Callsign;
            lastPending = pending;
        }

        private string? pendingFor;
        private int lastPending;

        private readonly TextBlock infoLeft = new() { FontSize = 11, Height = 45, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 3, 0, 3) };
        private readonly TextBlock infoRight = new() { FontSize = 11, Height = 45, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 3, 0, 3) };

        /// <summary>The information of the selected aircraft in two columns of three lines each (always three: nothing moves).</summary>
        private void ShowInformation(AircraftState? state)
        {
            if (state == null)
            {
                infoLeft.Text = bridge.Count == 0 ? "No aircraft flying: Traffic\ngeneration (Create & start), or\n▶ in the TRAFFIC LIST." : "";
                infoRight.Text = "";
                return;
            }
            Runway? runway = bridge.Reference;
            Aircraft? position = bridge.LastPosition(state.Callsign);
            string distance = position != null && runway != null ? position.DistanceFromTouchdown(runway).ToString("0.0", CultureInfo.InvariantCulture) : "--";
            double altitude = Qfe ? state.AltitudeFt - Elevation : state.AltitudeFt;
            string mode = state.StickActive ? "stick" : state.VerticalMode switch
            {
                VerticalMode.GlidePath => "Normal rate",
                VerticalMode.Rate => "rate",
                _ => "altitude"
            };
            infoLeft.Text = string.Format(CultureInfo.InvariantCulture, "{0}  {1}{2}\n{3} NM from touchdown\nground speed {4:0} kt",
                state.Callsign, state.PerformanceClass, state.Frozen ? "  (PAUSED)" : "", distance, state.GroundSpeedKt);
            infoRight.Text = string.Format(CultureInfo.InvariantCulture, "{0:0} ft {1}, {2:0} kt\nvertical: {3}\n{4}",
                altitude, Qfe ? "QFE" : "QNH", state.SpeedKt, mode,
                state.PendingCommands > 0 ? $"pilot: {state.PendingCommands} pending" : "");
        }

        /// <summary>The header of the aircraft list (as the rows of <see cref="ShowAircraftRow"/>).</summary>
        private static readonly string AircraftHeader = string.Format(CultureInfo.InvariantCulture, "{0,-7} {1,6} {2,3} {3,4} {4} {5}", "Call", "ALT", "SPD", "NM", "P", "NAV");

        /// <summary>
        /// A row of the aircraft list, short for the narrow panel (callsign, altitude with ↑↓, speed, distance from
        /// touchdown, seen by the PAR ● / ○, navigation) and, on the right, II / ▶ (Pause / Unpause: only this aircraft
        /// held where it is), ↺ (Reactivate: an aircraft of a flight plan starts again from the first point of its plan)
        /// and ✕ (Remove). The row is made once and then only changed, so a click on its buttons is never lost under a
        /// refresh. (<paramref name="text"/>: the long row of the shared window, not used here.)
        /// </summary>
        private void ShowAircraftRow(ListBoxItem item, AircraftState state, string text)
        {
            string callsign = state.Callsign;
            Runway? runway = bridge.Reference;
            Aircraft? position = bridge.LastPosition(callsign);
            string distance = position != null && runway != null ? position.DistanceFromTouchdown(runway).ToString("0.0", CultureInfo.InvariantCulture) : "--";
            string seen = position != null && isSeen(position) ? "●" : "○";
            double altitude = Qfe ? state.AltitudeFt - Elevation : state.AltitudeFt;
            string climbing = state.VerticalSpeedFpm > 50 ? "↑" : state.VerticalSpeedFpm < -50 ? "↓" : " ";
            string navigation = state.Contact switch
            {
                GroundContact.Landed => "LANDED",
                GroundContact.OnGround => "GROUND",
                _ => state.Frozen ? "PAUSED" : NavShort(state)
            };
            text = string.Format(CultureInfo.InvariantCulture, "{0,-7} {1,5:0}{2} {3,3:0} {4,4} {5} {6}",
                callsign.Length > 7 ? callsign[..7] : callsign, altitude, climbing, state.SpeedKt, distance, seen, navigation);
            if (item.Content is not DockPanel { Tag: RowParts parts })
            {
                TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Button pause = RowButton("II");
                Button reactivate = RowButton("↺");
                Button remove = RowButton("✕");
                pause.Click += (s, e) => { if (item.Tag is string name && bridge.Engine.Get(name) is AircraftState now) bridge.Engine.SetFrozen(name, !now.Frozen); Refresh(); };
                reactivate.Click += (s, e) => { if (item.Tag is string name) Reactivate(name); };
                remove.Click += (s, e) => { if (item.Tag is string name) RemoveAircraft(name); Refresh(); };
                reactivate.ToolTip = "Reactivate: the aircraft of a flight plan starts again from the first point of its plan.";
                remove.ToolTip = "Remove this aircraft (its flight plan, if any, waits again).";
                DockPanel row = new() { LastChildFill = true };
                foreach (Button button in new[] { remove, reactivate, pause })
                {
                    DockPanel.SetDock(button, Dock.Right);
                    row.Children.Add(button);
                }
                row.Children.Add(label);
                parts = new RowParts(label, pause, reactivate);
                row.Tag = parts;
                item.Content = row;
                item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            }
            if (parts.Label.Text != text) parts.Label.Text = text;
            parts.Label.ToolTip = $"{callsign}: {(state.Frozen ? "PAUSED" : NavText(state))}";
            parts.Pause.Content = state.Frozen ? "▶" : "II";
            parts.Pause.ToolTip = state.Frozen ? "Unpause: it goes on from where it is." : "Pause: only this aircraft stays where it is (the others go on).";
            parts.Reactivate.Visibility = flightPlans.Any(p => string.Equals(p.Active, callsign, StringComparison.OrdinalIgnoreCase)) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The NAV column of the narrow list: →point (ROUTE), RTE? (ROUTE waiting for a point), MAN.</summary>
        private static string NavShort(AircraftState state)
        {
            if (state.Navigation == NavigationMode.Route && state.Route != null && state.RouteNext >= 0 && state.RouteNext < state.Route.Count)
            {
                string name = state.Route[state.RouteNext].Name.Length > 0 ? state.Route[state.RouteNext].Name : $"P{state.RouteNext + 1}";
                return "→" + (name.Length > 6 ? name[..6] : name);
            }
            return state.Navigation == NavigationMode.RouteArmed ? "RTE?" : "MAN";
        }

        /// <summary>The parts of a row of the aircraft list that change.</summary>
        private sealed record RowParts(TextBlock Label, Button Pause, Button Reactivate);

        private static Button RowButton(string text) => new()
        {
            Content = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Padding = new Thickness(4, 0, 4, 0),
            MinWidth = 20,
            Margin = new Thickness(2, 1, 0, 1),
            Focusable = false
        };

        /// <summary>Forgets the tracks and history dots of an aircraft (null: of all) on every view (given by the main window).</summary>
        private Action<string?>? forgetTracks;

        /// <summary>The main window: how to forget the tracks of an aircraft removed or started again.</summary>
        internal void AttachForget(Action<string?> forget) => forgetTracks = forget;

        /// <summary>An aircraft removed (its old track and history dots go from every view).</summary>
        private void RemoveAircraft(string callsign)
        {
            bridge.Engine.Remove(callsign);
            forgetTracks?.Invoke(callsign);
        }

        /// <summary>Reactivate: the aircraft of a flight plan removed and started again from the first point of its plan.</summary>
        private void Reactivate(string callsign)
        {
            FlightPlan? plan = flightPlans.FirstOrDefault(p => string.Equals(p.Active, callsign, StringComparison.OrdinalIgnoreCase));
            if (plan == null) return;
            // The old flight's track and history dots go: the new one starts clean, even under the same callsign.
            RemoveAircraft(callsign);
            plan.Active = null;
            Activate(plan);
            // Not started again (a question answered No): an aircraft of Quick waits in the TRAFFIC LIST instead of being lost.
            if (plan.Active == null && plan.Quick) plan.Queued = true;
        }

        /// <summary>The NAV column of the list: MANUAL, ROUTE → point, or ROUTE waiting for a point.</summary>
        private static string NavText(AircraftState state) => state.Navigation switch
        {
            NavigationMode.Route when state.Route != null && state.RouteNext >= 0 && state.RouteNext < state.Route.Count =>
                "ROUTE → " + (state.Route[state.RouteNext].Name.Length > 0 ? state.Route[state.RouteNext].Name : $"point {state.RouteNext + 1}"),
            NavigationMode.RouteArmed => "ROUTE (choose a point)",
            _ => state.Route != null && state.RouteNext < state.Route.Count ? "MANUAL (route kept)" : "MANUAL"
        };
    }
}
