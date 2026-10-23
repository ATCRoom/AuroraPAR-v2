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
    /// AuroraPAR v2 Trainer, Instructor+: the column beside the plan view, in three areas.
    /// 1 Simulation (opens and closes; Pause and a summary always on its bar): runway, wind, QNH / QFE, Aurora-like
    ///   data, pilot reaction, callsign prefix.
    /// 2 Traffic generation: "Quick" (an aircraft at once: on final, 30° / 45° / 90° from left or right, downwind left
    ///   or right) and "Advanced" (flight plans, scenarios, the route of a flight plan).
    /// 3 Aircraft control: the aircraft flying and everything to fly the selected one (heading, turn rate, rate,
    ///   altitude, speed, the stick that can be hidden, MANUAL / ROUTE with its points, Next WPT, Direct to, Edit
    ///   route…, rename, remove).
    /// The controls are those of the window (built by the shared code, with their events): they are moved here.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly TextBlock simulationSummary = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(6, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ToggleButton[] startButtons = new ToggleButton[9];
        private int quickStart;
        private readonly Border stickArea = new();
        private readonly Button stickToggle = new() { FontSize = 10, Padding = new Thickness(6, 0, 6, 0), Focusable = false };
        private readonly TextBlock altitudeHint = Hint("the route's altitudes until you give one");
        private readonly TextBlock speedHint = Hint("the route's speeds until you give one");
        private readonly TextBlock headingHintTrainer = Hint("a heading puts the aircraft in MANUAL");
        private FrameworkElement? controlArea;

        private static TextBlock Hint(string text) => new() { Text = text, FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };

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

        private static Border Area(string title, UIElement content, FrameworkElement? headerRight = null)
        {
            DockPanel header = new() { Margin = new Thickness(0, 0, 0, 3) };
            if (headerRight != null)
            {
                DockPanel.SetDock(headerRight, Dock.Right);
                header.Children.Add(headerRight);
            }
            header.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            StackPanel stack = new();
            stack.Children.Add(header);
            stack.Children.Add(content);
            return new Border
            {
                BorderBrush = Frozen(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(1),
                Background = Frozen(Color.FromRgb(0xFB, 0xFB, 0xFB)),
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 5),
                Child = stack
            };
        }

        private static StackPanel Row(params UIElement[] children)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            foreach (UIElement child in children) row.Children.Add(child);
            return row;
        }

        private static Expander Section(string number, string title, UIElement content, bool open, UIElement? extra = null)
        {
            DockPanel header = new() { LastChildFill = true };
            StackPanel left = new() { Orientation = Orientation.Horizontal };
            left.Children.Add(new Border
            {
                Background = Frozen(Color.FromRgb(0x3A, 0x5F, 0x8A)),
                CornerRadius = new CornerRadius(9),
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = number, Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            left.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(left, Dock.Left);
            header.Children.Add(left);
            if (extra != null) header.Children.Add(extra);
            Expander expander = new()
            {
                Header = header,
                Content = new Border { Padding = new Thickness(6, 4, 2, 4), Child = content },
                IsExpanded = open,
                Margin = new Thickness(0, 0, 0, 6),
                Background = Frozen(Color.FromRgb(0xF7, 0xF7, 0xF7)),
                BorderBrush = Frozen(Color.FromRgb(0xB8, 0xB8, 0xB8)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            // The header across the whole bar (its buttons on the right).
            expander.SizeChanged += (s, e) => header.Width = Math.Max(0, expander.ActualWidth - 36);
            return expander;
        }

        /// <summary>The column of the three areas, made from the controls of the window.</summary>
        private UIElement BuildTrainerColumn()
        {
            StackPanel column = new() { Margin = new Thickness(8, 8, 8, 8) };

            // ----- 1 Simulation ---------------------------------------------------------------------------------------
            StackPanel simulation = new();
            simulation.Children.Add(Detach((FrameworkElement)referenceText.Parent));
            if (windFromBox.Parent is FrameworkElement windRow) simulation.Children.Add(Detach(windRow));
            if (pauseButton.Parent is FrameworkElement optionsRow)
            {
                Detach(pauseButton);
                simulation.Children.Add(Detach(optionsRow));
            }
            if (reactionBox.Parent is StackPanel reactionRow)
            {
                Detach(reactionRow);
                reactionRow.HorizontalAlignment = HorizontalAlignment.Left;
                simulation.Children.Add(reactionRow);
            }
            simulation.Children.Add(Detach(noticeText));
            DockPanel simulationExtra = new();
            pauseButton.FontWeight = FontWeights.SemiBold;
            pauseButton.Margin = new Thickness(6, 0, 4, 0);
            DockPanel.SetDock(pauseButton, Dock.Right);
            simulationExtra.Children.Add(pauseButton);
            simulationExtra.Children.Add(simulationSummary);
            Expander simulationSection = Section("1", "Simulation", simulation, settings.SimulationOpen, simulationExtra);
            simulationSection.Expanded += (s, e) => { settings.SimulationOpen = true; save(); };
            simulationSection.Collapsed += (s, e) => { settings.SimulationOpen = false; save(); };
            column.Children.Add(simulationSection);

            // ----- 2 Traffic generation -------------------------------------------------------------------------------
            TabControl tabs = new() { Margin = new Thickness(0, 2, 0, 0) };
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
            column.Children.Add(Section("2", "Traffic generation", tabs, true));

            // ----- 3 Aircraft control ---------------------------------------------------------------------------------
            column.Children.Add(Section("3", "Aircraft control", BuildAircraftControl(), true, KeysButton()));
            return new ScrollViewer
            {
                Content = column,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private FrameworkElement KeysButton()
        {
            Button keys = new() { Content = "Keys…", Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Right, Focusable = false, ToolTip = "Keys for the commands of the selected aircraft (also for a macro keyboard), and their steps." };
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
            row1.Children.Add(Caption("Distance"));
            row1.Children.Add(distanceBox);
            row1.Children.Add(Caption("NM from touchdown   off the centreline"));
            row1.Children.Add(offsetBox);
            row1.Children.Add(Caption("NM   altitude"));
            row1.Children.Add(newAltitudeBox);
            row1.Children.Add(Caption("ft (empty: glide path)"));
            panel.Children.Add(row1);
            WrapPanel row2 = new() { Margin = new Thickness(0, 3, 0, 0) };
            row2.Children.Add(Caption("Speed"));
            row2.Children.Add(newSpeedBox);
            row2.Children.Add(Caption("kt   class"));
            row2.Children.Add(classBox);
            row2.Children.Add(Caption("callsign"));
            row2.Children.Add(newCallsignBox);
            Button create = new() { Content = "Create aircraft", FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(10, 0, 0, 0) };
            create.Click += (s, e) => CreateQuick();
            row2.Children.Add(create);
            panel.Children.Add(row2);
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

        /// <summary>A new aircraft at once, from the start chosen.</summary>
        private void CreateQuick()
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

        private UIElement BuildAircraftControl()
        {
            StackPanel panel = new();
            panel.Children.Add(new TextBlock { Text = "Call      HDG    ALT/HGT   SPD    NM PAR  NAV", FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 2) });
            Detach(list);
            list.Height = 104;
            list.FontSize = 12;
            list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(list);
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
                readouts.Margin = new Thickness(0, 0, 0, 6);
                control.Children.Add(readouts);
            }

            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: heading, turn rate, rate, altitude, speed.
            StackPanel left = new() { Margin = new Thickness(0, 0, 4, 0) };
            RepeatButton headingLeft = StepKey("◀ L", "Heading 1° left (held: repeats). After the pilot reaction.");
            headingLeft.Click += (s, e) => StepHeading(-1);
            RepeatButton headingRight = StepKey("R ▶", "Heading 1° right (held: repeats). After the pilot reaction.");
            headingRight.Click += (s, e) => StepHeading(1);
            Detach(headingBox);
            Detach(finalCourseButton);
            finalCourseButton.Margin = new Thickness(4, 0, 0, 0);
            finalCourseButton.Padding = new Thickness(6, 3, 6, 3);
            StackPanel turns = Row();
            // 3°/s, FREE, 1.5°/s (the buttons of the shared code: 0 half, 1 rate one, 2 free).
            string[] turnTexts = ["1.5°/s", "3°/s", "FREE"];
            foreach (int i in new[] { 1, 2, 0 })
            {
                Button button = Detach(turnButtons[i]);
                button.Content = turnTexts[i];
                button.MinWidth = 54;
                button.Margin = new Thickness(2, 0, 2, 0);
                turns.Children.Add(button);
            }
            Button stopTurn = new() { Content = "STOP TURN", FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 2, 6, 2), ToolTip = "Stop turn: the aircraft rolls out on the heading it has (after the pilot reaction); in ROUTE it goes to MANUAL at once." };
            stopTurn.Click += (s, e) => StopTurn();
            StackPanel heading = new();
            heading.Children.Add(Row(headingLeft, headingBox, headingRight, finalCourseButton));
            heading.Children.Add(turns);
            heading.Children.Add(Row(stopTurn));
            heading.Children.Add(headingHintTrainer);
            left.Children.Add(Area("HEADING °M", heading));

            RepeatButton up = StepKey("▲ UP", "Rate 50 ft/min up: less descent (held: repeats; stops once at the Normal rate). After the pilot reaction.");
            up.Click += (s, e) => StepRate(RateKeyStep);
            RepeatButton down = StepKey("▼ DN", "Rate 50 ft/min down: more descent (held: repeats; stops once at the Normal rate). After the pilot reaction.");
            down.Click += (s, e) => StepRate(-RateKeyStep);
            Detach(rateBox);
            rateBox.Width = 60;
            rateBox.Margin = new Thickness(3, 0, 3, 0);
            Detach(normalButton);
            normalButton.Margin = new Thickness(4, 0, 0, 0);
            normalButton.Padding = new Thickness(6, 3, 6, 3);
            Button stopVertical = new() { Content = "STOP CLIMB/DESCENT", FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 2, 6, 2), ToolTip = "Stop climb / descent: the aircraft levels off where it is (after the pilot reaction). From then on the route's altitudes are not flown." };
            stopVertical.Click += (s, e) => StopVertical();
            StackPanel rate = new();
            rate.Children.Add(Row(up, rateBox, down, normalButton));
            rate.Children.Add(Row(stopVertical));
            left.Children.Add(Area("RATE ft/min", rate));

            Detach(altitudeBox);
            Detach(altitudeRateBox);
            Detach(altitudeCaption);
            Button giveAltitude = SmallButton("Go");
            giveAltitude.FontWeight = FontWeights.SemiBold;
            giveAltitude.Margin = new Thickness(4, 0, 0, 0);
            giveAltitude.Click += (s, e) => GiveAltitude();
            StackPanel altitude = new();
            altitude.Children.Add(altitudeCaption);
            altitude.Children.Add(Row(altitudeBox, Caption("at"), altitudeRateBox, Caption("ft/min"), giveAltitude));
            altitude.Children.Add(altitudeHint);
            left.Children.Add(Area("", altitude));

            Detach(speedBox);
            Button giveSpeed = SmallButton("Go");
            giveSpeed.FontWeight = FontWeights.SemiBold;
            giveSpeed.Margin = new Thickness(4, 0, 0, 0);
            giveSpeed.Click += (s, e) => GiveSpeed();
            StackPanel speed = new();
            speed.Children.Add(Row(speedBox, giveSpeed));
            speed.Children.Add(speedHint);
            left.Children.Add(Area("SPEED kt", speed));
            grid.Children.Add(left);

            // Right: the stick (can be hidden), navigation, service.
            StackPanel right = new() { Margin = new Thickness(4, 0, 0, 0) };
            if (pad.Parent is FrameworkElement padBorder)
            {
                Detach(padBorder);
                padBorder.HorizontalAlignment = HorizontalAlignment.Center;
                stickArea.Child = padBorder;
            }
            stickToggle.Click += (s, e) =>
            {
                settings.StickShown = !settings.StickShown;
                save();
                ShowStick();
            };
            right.Children.Add(Area("STICK (also a joystick)", stickArea, stickToggle));
            ShowStick();
            right.Children.Add(Area("NAVIGATION", BuildNavigation()));

            WrapPanel service = new() { HorizontalAlignment = HorizontalAlignment.Center };
            Detach(renameBox);
            Button rename = SmallButton("Rename");
            rename.Click += (s, e) => Rename();
            Button remove = SmallButton("Remove");
            remove.Click += (s, e) => { if (SelectedCallsign is string callsign) bridge.Engine.Remove(callsign); Refresh(); };
            Button removeAll = SmallButton("Remove all");
            removeAll.Click += (s, e) => { bridge.Clear(); Refresh(); };
            service.Children.Add(renameBox);
            service.Children.Add(rename);
            service.Children.Add(remove);
            service.Children.Add(removeAll);
            if (openJoystick != null)
            {
                Button joystick = SmallButton("Joystick...");
                joystick.ToolTip = "A real joystick or gamepad: device, buttons, live test.";
                joystick.Click += (s, e) => openJoystick(this);
                service.Children.Add(joystick);
            }
            foreach (UIElement child in service.Children) if (child is FrameworkElement element) element.Margin = new Thickness(2, 2, 2, 2);
            right.Children.Add(service);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            control.Children.Add(grid);
            controlArea = control;
            panel.Children.Add(control);
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
            bool routeValues = state != null && state.Route != null && state.Navigation != NavigationMode.Manual;
            // The hints keep their place when not shown (Hidden, not Collapsed): the controls never move.
            altitudeHint.Visibility = routeValues && state!.RouteAltitude ? Visibility.Visible : Visibility.Hidden;
            speedHint.Visibility = routeValues && state!.RouteSpeed ? Visibility.Visible : Visibility.Hidden;
            headingHintTrainer.Visibility = state != null && state.Navigation != NavigationMode.Manual ? Visibility.Visible : Visibility.Hidden;
            // While the route turns the aircraft the heading box follows, so that the keys and ◀ L / R ▶ start from the
            // heading it is flying (a heading given puts it in MANUAL at once, and then the box is left as typed).
            if (state != null && state.Navigation != NavigationMode.Manual && !headingBox.IsKeyboardFocused)
            {
                headingBox.Text = Magnetic(state.TargetHeading, variation()).ToString("000", CultureInfo.InvariantCulture);
            }
            ShowInformation(state);
            // An instruction carried out by the pilot (one less waiting): the fields show the new values (after STOP TURN,
            // STOP CLIMB/DESCENT, a heading…), so the keys and buttons start from them.
            int pending = state?.PendingCommands ?? 0;
            if (state != null && state.Callsign == pendingFor && pending < lastPending) fillFields = true;
            pendingFor = state?.Callsign;
            lastPending = pending;
        }

        private string? pendingFor;
        private int lastPending;

        private readonly TextBlock infoLeft = new() { Height = 51, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 17, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 4, 0, 4) };
        private readonly TextBlock infoRight = new() { Height = 51, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 17, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 4, 0, 4) };

        /// <summary>The information of the selected aircraft in two columns of three lines each (always three: nothing moves).</summary>
        private void ShowInformation(AircraftState? state)
        {
            if (state == null)
            {
                infoLeft.Text = bridge.Count == 0 ? "No simulated aircraft:\ncreate one (Quick) or activate a flight plan." : "";
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

        /// <summary>
        /// A row of the aircraft list: its text and, on the right, Pause / Unpause (only this aircraft held where it is),
        /// Reactivate (an aircraft of a flight plan starts again from the first point of its plan) and Remove. The row is
        /// made once and then only changed, so a click on its buttons is never lost under a refresh.
        /// </summary>
        private void ShowAircraftRow(ListBoxItem item, AircraftState state, string text)
        {
            string callsign = state.Callsign;
            if (state.Frozen) text = text.Replace(" FRZ", "");
            text = $"{text}  {(state.Frozen ? "PAUSED" : NavText(state))}";
            if (item.Content is not DockPanel { Tag: RowParts parts })
            {
                TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
                Button pause = RowButton("Pause");
                Button reactivate = RowButton("Reactivate");
                Button remove = RowButton("Remove");
                pause.Click += (s, e) => { if (item.Tag is string name && bridge.Engine.Get(name) is AircraftState now) bridge.Engine.SetFrozen(name, !now.Frozen); Refresh(); };
                reactivate.Click += (s, e) => { if (item.Tag is string name) Reactivate(name); };
                remove.Click += (s, e) => { if (item.Tag is string name) bridge.Engine.Remove(name); Refresh(); };
                pause.ToolTip = "Pause: only this aircraft stays where it is (the others go on); Unpause: it goes on from there.";
                reactivate.ToolTip = "The aircraft of a flight plan starts again from the first point of its plan.";
                remove.ToolTip = "Removes this aircraft (its flight plan, if any, waits again).";
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
            parts.Pause.Content = state.Frozen ? "Unpause" : "Pause";
            parts.Reactivate.Visibility = flightPlans.Any(p => string.Equals(p.Active, callsign, StringComparison.OrdinalIgnoreCase)) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The parts of a row of the aircraft list that change.</summary>
        private sealed record RowParts(TextBlock Label, Button Pause, Button Reactivate);

        private static Button RowButton(string text) => new()
        {
            Content = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(3, 1, 0, 1),
            Focusable = false
        };

        /// <summary>Reactivate: the aircraft of a flight plan removed and started again from the first point of its plan.</summary>
        private void Reactivate(string callsign)
        {
            FlightPlan? plan = flightPlans.FirstOrDefault(p => string.Equals(p.Active, callsign, StringComparison.OrdinalIgnoreCase));
            if (plan == null) return;
            bridge.Engine.Remove(callsign);
            plan.Active = null;
            Activate(plan);
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
