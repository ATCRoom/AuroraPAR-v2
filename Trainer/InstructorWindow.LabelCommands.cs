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
    /// AuroraPAR v2 Trainer, Instructor+: commands on the label of the selected aircraft on the plan view. Its label
    /// is larger, three lines: callsign, class, ROUTE / MANUAL; altitude now (hundreds of ft) with ↑ / ↓ and the altitude
    /// given, the speed; the heading (grey in ROUTE), → the next point, the rate. The values given are blue: a click on
    /// one opens its list near the label (a click on a value gives it, after the pilot reaction), the wheel over one
    /// changes it in steps; a right click on a label or a track opens the menu of the aircraft.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private Popup? fieldPopup;

        private void AttachLabelCommands(PlanView view)
        {
            view.LabelCommands = true;
            view.ExtendedLabel = LabelLines;
            view.LabelFieldClicked += OpenFieldList;
            view.LabelFieldWheel += FieldWheel;
            view.LabelMenuRequested += OpenAircraftMenu;
        }

        /// <summary>The lines of the larger label: only for the selected aircraft.</summary>
        private IReadOnlyList<IReadOnlyList<LabelPart>>? LabelLines(string callsign)
        {
            if (plan == null || !string.Equals(callsign, plan.SelectedCallsign, StringComparison.OrdinalIgnoreCase) || bridge.Engine.Get(callsign) is not AircraftState state) return null;
            double magneticVariation = variation();
            string navigation = state.Navigation switch
            {
                NavigationMode.Route => "ROUTE",
                NavigationMode.RouteArmed => "ROUTE?",
                _ => "MANUAL"
            };
            // Altitudes as the window shows them (QFE: heights above the threshold).
            string Hundreds(double feet) => Math.Max(0, Math.Round((Qfe ? feet - Elevation : feet) / 100)).ToString("000", CultureInfo.InvariantCulture);
            string arrow = state.VerticalSpeedFpm > 50 ? "↑" : state.VerticalSpeedFpm < -50 ? "↓" : " ";
            string next = state.Route != null && state.RouteNext >= 0 && state.RouteNext < state.Route.Count
                ? "→" + PointLabel(state.Route[state.RouteNext], magneticVariation) : "→---";
            string rate = state.VerticalMode switch
            {
                VerticalMode.GlidePath => "NOR",
                VerticalMode.Rate => FormatRate(Math.Round(state.CommandedVerticalSpeedFpm / 10) * 10),
                _ => FormatRate(Math.Round(state.VerticalSpeedFpm / 100) * 100)
            };
            bool manual = state.Navigation == NavigationMode.Manual;
            return new List<IReadOnlyList<LabelPart>>
            {
                new List<LabelPart>
                {
                    new(state.Callsign),
                    new("  " + (state.PerformanceClass.Length > 0 ? state.PerformanceClass[..1] : ""), Dim: true),
                    new("  " + (state.Frozen ? "PAUSED" : navigation), Dim: !manual && state.Navigation == NavigationMode.RouteArmed)
                },
                new List<LabelPart>
                {
                    new(Hundreds(state.AltitudeFt) + arrow + " "),
                    new(Hundreds(state.TargetAltitudeFt), "ALT", Given: true),
                    new("  "),
                    new(state.TargetSpeedKt.ToString("0", CultureInfo.InvariantCulture), "SPD", Given: true),
                    new(" kt", Dim: true)
                },
                new List<LabelPart>
                {
                    new("H" + Magnetic(state.TargetHeading, magneticVariation).ToString("000", CultureInfo.InvariantCulture), "HDG", Given: true, Dim: !manual),
                    new("  "),
                    new(next, "WPT", Given: true),
                    new("  "),
                    new(rate, "VS", Given: true)
                }
            };
        }

        /// <summary>The wheel over a field of the label: heading 5°, altitude 100 ft, speed 5 kt, rate 100 ft/min.</summary>
        private void FieldWheel(string callsign, string field, int sign)
        {
            if (!string.Equals(callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase) || Selected() is not AircraftState state) return;
            switch (field)
            {
                // Each step from the value the label shows (not from a field of the panel typed before).
                case "HDG":
                    headingBox.Text = Magnetic(state.TargetHeading, variation()).ToString("000", CultureInfo.InvariantCulture);
                    StepHeading(sign * 5);
                    break;
                case "ALT":
                    double altitude = Math.Round((Qfe ? state.TargetAltitudeFt - Elevation : state.TargetAltitudeFt) / 100) * 100;
                    altitudeBox.Text = (altitude + sign * 100).ToString("0", CultureInfo.InvariantCulture);
                    GiveAltitude();
                    break;
                case "SPD":
                    speedBox.Text = Math.Clamp(Math.Round(state.TargetSpeedKt) + sign * 5, 40, 400).ToString("0", CultureInfo.InvariantCulture);
                    GiveSpeed();
                    break;
                case "VS":
                    double now = state.VerticalMode == VerticalMode.Rate ? state.CommandedVerticalSpeedFpm : state.VerticalSpeedFpm;
                    rateBox.Text = FormatRate(Math.Round(now / 100) * 100);
                    StepRate(sign * 100);
                    break;
            }
            plan?.InvalidateVisual();
        }

        // ----- The lists of the fields --------------------------------------------------------------------------------

        private void ClosePopup()
        {
            if (fieldPopup != null) fieldPopup.IsOpen = false;
            fieldPopup = null;
        }

        /// <summary>A small dark panel near the label: a title, a list of values, a box + Go, buttons.</summary>
        private void ShowFieldPopup(Point at, string title, IReadOnlyList<(string Text, Action Give, bool Current, bool Dim)> values,
            string? typedStart, Action<string>? typedGive, params (string Text, Action Act)[] buttons)
        {
            ClosePopup();
            if (plan == null) return;
            StackPanel panel = new() { Width = 210 };
            panel.Children.Add(new Border
            {
                Background = Frozen(Color.FromRgb(0x2A, 0x3A, 0x4A)),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock { Text = title, Foreground = Frozen(Color.FromRgb(0xBB, 0xCC, 0xDD)), FontWeight = FontWeights.SemiBold, FontSize = 11 }
            });
            ListBox list = new()
            {
                MaxHeight = 170,
                Background = Frozen(Color.FromRgb(0x1D, 0x23, 0x2A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13
            };
            foreach ((string text, Action give, bool current, bool dim) in values)
            {
                ListBoxItem item = new() { Content = text, Tag = give, Foreground = dim ? Brushes.Gray : Brushes.White };
                list.Items.Add(item);
                if (current) list.SelectedItem = item;
            }
            // A click on a value gives it.
            list.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem item && item.Tag is Action give)
                {
                    ClosePopup();
                    give();
                }
            };
            // Enter on the list gives the value highlighted.
            list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && list.SelectedItem is ListBoxItem item && item.Tag is Action give)
                {
                    ClosePopup();
                    give();
                    e.Handled = true;
                }
            };
            if (values.Count > 0) panel.Children.Add(list);
            TextBox? typedBox = null;
            WrapPanel bottom = new() { Margin = new Thickness(6, 4, 6, 6) };
            if (typedGive != null)
            {
                TextBox box = new() { Width = 70, Text = typedStart ?? "", FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(0, 0, 4, 4) };
                typedBox = box;
                Button go = new() { Content = "Go", FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 4) };
                void Give()
                {
                    string text = box.Text.Trim();
                    ClosePopup();
                    typedGive(text);
                }
                go.Click += (s, e) => Give();
                box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Give(); e.Handled = true; } };
                bottom.Children.Add(box);
                bottom.Children.Add(go);
            }
            foreach ((string text, Action act) in buttons)
            {
                Button button = new() { Content = text, FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 4) };
                button.Click += (s, e) => { ClosePopup(); act(); };
                bottom.Children.Add(button);
            }
            panel.Children.Add(bottom);
            Border frame = new()
            {
                Background = Frozen(Color.FromRgb(0x1D, 0x23, 0x2A)),
                BorderBrush = Frozen(Color.FromRgb(0x8F, 0xA8, 0xC0)),
                BorderThickness = new Thickness(1),
                Child = panel
            };
            frame.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { ClosePopup(); e.Handled = true; } };
            Popup popup = new()
            {
                PlacementTarget = plan,
                Placement = PlacementMode.Relative,
                HorizontalOffset = at.X + 14,
                VerticalOffset = at.Y + 10,
                StaysOpen = false,
                Child = frame
            };
            popup.Closed += (s, e) => { if (ReferenceEquals(fieldPopup, popup)) fieldPopup = null; };
            // The keyboard in the list (Esc closes, Enter gives), or in the box when there is no list.
            popup.Opened += (s, e) =>
            {
                if (values.Count > 0) list.Focus();
                else if (typedBox != null) { typedBox.Focus(); typedBox.SelectAll(); }
            };
            fieldPopup = popup;
            popup.IsOpen = true;
            list.Dispatcher.InvokeAsync(() => { if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); });
        }

        /// <summary>A click on a field of the label: its list.</summary>
        private void OpenFieldList(string callsign, string field, Point at)
        {
            if (!string.Equals(callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase) || Selected() is not AircraftState state) return;
            double magneticVariation = variation();
            switch (field)
            {
                case "HDG":
                {
                    int now = Magnetic(state.TargetHeading, magneticVariation);
                    int start = (int)Math.Round(now / 5.0) * 5;
                    List<(string, Action, bool, bool)> values = [];
                    for (int step = 12; step >= -12; step--)
                    {
                        int heading = ((start + step * 5) % 360 + 360) % 360;
                        if (heading == 0) heading = 360;
                        string text = heading.ToString("000", CultureInfo.InvariantCulture);
                        values.Add((text, () => { headingBox.Text = text; GiveHeading(); }, step == 0, false));
                    }
                    ShowFieldPopup(at, $"HEADING °M — {callsign}", values, now.ToString("000", CultureInfo.InvariantCulture),
                        text => { headingBox.Text = text; GiveHeading(); },
                        ("◀ 5", () => StepHeading(-5)), ("5 ▶", () => StepHeading(5)), ("STOP TURN", StopTurn), ("Final CRS", FinalCourse));
                    break;
                }
                case "ALT":
                {
                    double target = Qfe ? state.TargetAltitudeFt - Elevation : state.TargetAltitudeFt;
                    double start = Math.Round(target / 100) * 100;
                    double lowest = Qfe ? 0 : Math.Ceiling(Elevation / 100) * 100;
                    List<(string, Action, bool, bool)> values = [];
                    for (int step = 10; step >= -10; step--)
                    {
                        double altitude = step == 0 ? start : Math.Round((start + step * 500) / 500) * 500;
                        if (altitude < lowest || altitude > 40000 || (step != 0 && Math.Abs(altitude - start) < 1)) continue;
                        string text = altitude.ToString("0", CultureInfo.InvariantCulture);
                        values.Add((text, () => { altitudeBox.Text = text; GiveAltitude(); }, step == 0, false));
                    }
                    ShowFieldPopup(at, $"ALTITUDE ft {(Qfe ? "QFE" : "QNH")} — {callsign}", values, start.ToString("0", CultureInfo.InvariantCulture),
                        text => { altitudeBox.Text = text; GiveAltitude(); },
                        ("STOP CLIMB/DESCENT", StopVertical));
                    break;
                }
                case "VS":
                {
                    double now = state.VerticalMode == VerticalMode.Rate ? state.CommandedVerticalSpeedFpm : state.VerticalSpeedFpm;
                    double start = Math.Round(now / 100) * 100;
                    List<(string, Action, bool, bool)> values = [];
                    for (double rate = 2000; rate >= -3000; rate -= 100)
                    {
                        string text = FormatRate(rate);
                        values.Add((text, () => { rateBox.Text = text; GiveRate(); }, Math.Abs(rate - start) < 1, false));
                    }
                    double best = Math.Round(state.BestVerticalSpeedFpm / 10) * 10;
                    ShowFieldPopup(at, $"RATE ft/min — {callsign}", values, FormatRate(start),
                        text => { rateBox.Text = text; GiveRate(); },
                        ($"Normal ({FormatRate(best)})", NormalRate), ("STOP", StopVertical));
                    break;
                }
                case "SPD":
                {
                    double start = Math.Round(state.TargetSpeedKt / 5) * 5;
                    List<(string, Action, bool, bool)> values = [];
                    for (int step = 12; step >= -12; step--)
                    {
                        double speed = start + step * 5;
                        if (speed < 40 || speed > 400) continue;
                        string text = speed.ToString("0", CultureInfo.InvariantCulture);
                        values.Add((text, () => { speedBox.Text = text; GiveSpeed(); }, step == 0, false));
                    }
                    List<(string Text, Action Act)> speedButtons = [];
                    if (FinalSpeed(state) is double final) speedButtons.Add(($"Final ({final:0})", SlowToFinal));
                    ShowFieldPopup(at, $"SPEED kt — {callsign}", values, Math.Round(state.TargetSpeedKt).ToString("0", CultureInfo.InvariantCulture),
                        text => { speedBox.Text = text; GiveSpeed(); }, speedButtons.ToArray());
                    break;
                }
                case "WPT":
                {
                    List<(string, Action, bool, bool)> values = [];
                    if (state.Route != null)
                    {
                        for (int i = 0; i < state.Route.Count; i++)
                        {
                            int index = i;
                            bool passed = i < state.RouteNext;
                            string mark = state.Navigation == NavigationMode.Route && i == state.RouteNext ? "→ " : "  ";
                            values.Add((mark + PointLabel(state.Route[i], magneticVariation) + (passed ? "  passed" : ""), () =>
                            {
                                bridge.Engine.SetNavigation(state.Callsign, true);
                                bridge.Engine.DirectTo(state.Callsign, index);
                            }, i == state.RouteNext, passed));
                        }
                    }
                    ShowFieldPopup(at, $"DIRECT TO — {callsign}", values, "",
                        text => { directBox.Text = text; DirectToTyped(); },
                        ("Next WPT", NextWaypoint), ("MANUAL", () => ChooseNavigation(false)), ("Edit route…", OpenRouteWindow));
                    break;
                }
            }
        }

        // ----- The menu of an aircraft --------------------------------------------------------------------------------

        /// <summary>A right click on a label or a track: the menu of that aircraft (it is selected first).</summary>
        private void OpenAircraftMenu(string callsign, Point at)
        {
            if (plan == null || bridge.Engine.Get(callsign) is not AircraftState state) return;
            ClosePopup();
            if (!string.Equals(callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase)) SelectCallsign(callsign);
            ContextMenu menu = new() { PlacementTarget = plan, Placement = PlacementMode.MousePoint };
            void Add(string text, Action act)
            {
                MenuItem item = new() { Header = text };
                item.Click += (s, e) => act();
                menu.Items.Add(item);
            }
            Add(state.Frozen ? "Unpause" : "Pause", () => { if (bridge.Engine.Get(callsign) is AircraftState now) bridge.Engine.SetFrozen(callsign, !now.Frozen); Refresh(); });
            if (flightPlans.Any(p => string.Equals(p.Active, callsign, StringComparison.OrdinalIgnoreCase))) Add("Reactivate (from the first point)", () => Reactivate(callsign));
            Add(state.Navigation == NavigationMode.Manual ? "ROUTE" : "MANUAL", () =>
            {
                if (!bridge.Engine.SetNavigation(callsign, state.Navigation == NavigationMode.Manual))
                {
                    Warn("This aircraft has no route to fly: make one with Edit route… (or start a flight plan from the TRAFFIC LIST).");
                }
                Refresh();
            });
            Add("Edit route…", () => { SelectCallsign(callsign); OpenRouteWindow(); });
            Add("Rename…", () => { renameBox.Focus(); renameBox.SelectAll(); });
            Add(plan.IsLabelHidden(callsign) ? "Show label" : "Hide label", () => plan.SetLabelHidden(callsign, !plan.IsLabelHidden(callsign)));
            menu.Items.Add(new Separator());
            Add("Remove", () => { bridge.Engine.Remove(callsign); Refresh(); });
            menu.IsOpen = true;
        }
    }
}
