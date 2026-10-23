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
    /// AuroraPAR v2 Trainer, Instructor+: NAVIGATION of the selected aircraft. MANUAL | ROUTE (one or the other):
    /// MANUAL flies the headings and the stick (a route is kept); ROUTE keeps the heading until a point is chosen, then
    /// flies the points. In ROUTE the points of the route are listed (to look at; a double-click flies direct to one,
    /// also one already passed), Next WPT flies direct to the next point, Direct to to a point of the route or of the
    /// maps (a point off the route becomes the next one). Edit route… changes the points in a small window.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly ToggleButton manualButton = new() { Content = "MANUAL", FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 3, 16, 3), Focusable = false, ToolTip = "MANUAL: the aircraft flies the headings you give (and the stick). Its route, if any, is kept." };
        private readonly ToggleButton routeButton = new() { Content = "ROUTE", FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 3, 16, 3), Focusable = false, ToolTip = "ROUTE: the aircraft keeps its heading until you choose a point (double-click in the list, Next WPT, Direct to), then flies the points. A heading or the stick puts it back in MANUAL." };
        private readonly TextBlock navigationStatus = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 3), Height = 34, TextTrimming = TextTrimming.CharacterEllipsis, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 16 };
        private readonly ListBox navigationList = new() { Height = 84, FontFamily = new FontFamily("Consolas"), FontSize = 12, ToolTip = "The points of the route (to look at). Double-click a point: direct to it, after the pilot reaction." };
        private readonly TextBlock navigationHint = new() { Text = "double-click a point: direct to it", FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Button nextWaypointButton = new() { Content = "Next WPT", FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 2, 8, 2), ToolTip = "Direct to the next point of the route (after the one it flies to), after the pilot reaction." };
        private readonly ComboBox directBox = new() { IsEditable = true, IsTextSearchEnabled = false, Width = 104, Height = 22, Margin = new Thickness(4, 0, 4, 0), ToolTip = "A point of the route, or of the maps (or an airport): Go flies direct to it. A point off the route becomes the next one; then the rest of the route." };
        private readonly Button directGoButton = new() { Content = "Go", FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 2, 8, 2) };
        private readonly StackPanel directRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
        private string navigationShownKey = "";
        private string navigationNamesKey = "";

        private UIElement BuildNavigation()
        {
            StackPanel panel = new();
            StackPanel modes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            modes.Children.Add(manualButton);
            modes.Children.Add(routeButton);
            panel.Children.Add(modes);
            panel.Children.Add(navigationStatus);
            panel.Children.Add(navigationList);
            panel.Children.Add(navigationHint);
            directRow.Children.Add(nextWaypointButton);
            directRow.Children.Add(Caption("Direct to"));
            directRow.Children.Add(directBox);
            directRow.Children.Add(directGoButton);
            panel.Children.Add(directRow);
            Button edit = new() { Content = "Edit route…", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Add, remove and change the points still to fly of the selected aircraft (in a small window)." };
            edit.Click += (s, e) => OpenRouteWindow();
            panel.Children.Add(edit);

            manualButton.Click += (s, e) => ChooseNavigation(false);
            routeButton.Click += (s, e) => ChooseNavigation(true);
            navigationList.MouseDoubleClick += (s, e) =>
            {
                if ((navigationList.SelectedItem as ListBoxItem)?.Tag is int index && SelectedCallsign is string callsign)
                {
                    bridge.Engine.DirectTo(callsign, index);
                }
            };
            nextWaypointButton.Click += (s, e) => NextWaypoint();
            directGoButton.Click += (s, e) => DirectToTyped();
            directBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                directBox.IsDropDownOpen = false;
                DirectToTyped();
                e.Handled = true;
            };
            return panel;
        }

        /// <summary>MANUAL (false) or ROUTE (true) for the selected aircraft, at once.</summary>
        private void ChooseNavigation(bool route)
        {
            if (SelectedCallsign is not string callsign) return;
            if (!bridge.Engine.SetNavigation(callsign, route))
            {
                Warn("This aircraft has no route to fly: make one with Edit route… (or start a flight plan with Activate).");
            }
            navigationShownKey = "";
            Refresh();
        }

        /// <summary>The key MANUAL / ROUTE: the other one.</summary>
        private void ToggleNavigation()
        {
            if (Selected() is not AircraftState state) return;
            ChooseNavigation(state.Navigation == NavigationMode.Manual);
        }

        private void NextWaypoint()
        {
            if (Selected() is not AircraftState state || state.Navigation == NavigationMode.Manual || state.Route == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            int index = state.Navigation == NavigationMode.Route ? state.RouteNext + 1 : state.RouteNext;
            if (index < 0 || index >= state.Route.Count)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            bridge.Engine.NextWaypoint(state.Callsign);
        }

        /// <summary>Direct to the point typed or chosen: of the route (the next one with that name, else any), else of the maps.</summary>
        private void DirectToTyped()
        {
            if (Selected() is not AircraftState state) return;
            string name = (directBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            double magneticVariation = variation();
            if (state.Route != null)
            {
                List<int> matches = Enumerable.Range(0, state.Route.Count)
                    .Where(i => string.Equals(PointLabel(state.Route[i], magneticVariation), name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 0)
                {
                    int index = matches.FirstOrDefault(i => i >= state.RouteNext, matches[0]);
                    bridge.Engine.SetNavigation(state.Callsign, true);
                    bridge.Engine.DirectTo(state.Callsign, index);
                    return;
                }
            }
            if (FindNamedPoint(name) is not { } found)
            {
                Warn($"No point named {name} in the route, in the maps or among the airports.");
                return;
            }
            // ROUTE at once when it has a route (heading kept until the pilot turns to the point).
            bridge.Engine.SetNavigation(state.Callsign, true);
            bridge.Engine.DirectToPoint(state.Callsign, new RoutePoint(found.X, found.Y, double.NaN, double.NaN, found.Name));
        }

        /// <summary>The NAVIGATION area of the selected aircraft (each refresh).</summary>
        private void RefreshNavigation(AircraftState? state, double magneticVariation)
        {
            bool manual = state == null || state.Navigation == NavigationMode.Manual;
            manualButton.IsChecked = state != null && manual;
            routeButton.IsChecked = state != null && !manual;
            int count = state?.Route?.Count ?? 0;
            int next = state?.RouteNext ?? 0;
            string heading = state == null ? "" : Magnetic(state.HeadingTrue, magneticVariation).ToString("000", CultureInfo.InvariantCulture);
            if (state == null) navigationStatus.Text = "";
            else if (state.Navigation == NavigationMode.Route && state.Route != null && next < count)
                navigationStatus.Text = $"ROUTE: to {PointLabel(state.Route[next], magneticVariation)}";
            else if (state.Navigation == NavigationMode.RouteArmed)
                navigationStatus.Text = $"ROUTE: keeps heading {heading} until you choose a point";
            else if (state.Route != null && next < count)
                navigationStatus.Text = $"MANUAL: heading {heading} (route kept: {string.Join(", ", state.Route.Skip(next).Take(2).Select(p => PointLabel(p, magneticVariation)))}{(count - next > 2 ? "…" : "")})";
            else if (state.Route != null)
                navigationStatus.Text = $"MANUAL: heading {heading} (route finished)";
            else
                navigationStatus.Text = $"MANUAL: heading {heading} (no route: Edit route… makes one)";

            // Hidden (not collapsed): the controls below never move.
            Visibility listShown = state != null && !manual && count > 0 ? Visibility.Visible : Visibility.Hidden;
            navigationList.Visibility = navigationHint.Visibility = listShown;
            // Direct to also from MANUAL (it puts the aircraft in ROUTE); Next WPT only in ROUTE.
            directRow.IsEnabled = state != null;
            nextWaypointButton.IsEnabled = state != null && !manual && count > 0;

            // The points: those passed in grey, → the one it flies to.
            if (state != null && state.Route != null && listShown == Visibility.Visible)
            {
                List<string> rows = [];
                for (int i = 0; i < count; i++)
                {
                    RoutePoint point = state.Route[i];
                    string mark = state.Navigation == NavigationMode.Route && i == next ? "→ " : "  ";
                    rows.Add(string.Format(CultureInfo.InvariantCulture, "{0}{1,-11} {2,5} ft {3,3} kt{4}", mark, PointLabel(point, magneticVariation), PointAltitude(point), PointSpeed(point), i < next ? "  passed" : ""));
                }
                string key = state.Callsign + "|" + string.Join("|", rows);
                if (key != navigationShownKey)
                {
                    navigationShownKey = key;
                    navigationList.Items.Clear();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        ListBoxItem item = new() { Content = rows[i], Tag = i, Foreground = i < next ? Brushes.Gray : Brushes.Black };
                        navigationList.Items.Add(item);
                        if (state.Navigation == NavigationMode.Route && i == next) navigationList.SelectedItem = item;
                    }
                    if (navigationList.SelectedItem is ListBoxItem selected) navigationList.ScrollIntoView(selected);
                }
            }
            else if (navigationShownKey.Length > 0)
            {
                navigationShownKey = "";
                navigationList.Items.Clear();
            }

            // The names of the Direct to box: the points of the route still to fly, then those of the maps.
            List<string> routeNames = state?.Route == null ? [] : state.Route.Skip(Math.Min(next, count)).Select(p => PointLabel(p, magneticVariation)).ToList();
            string namesKey = (state?.Callsign ?? "") + "|" + string.Join("|", routeNames) + "|" + (namesFromLayers?.GetHashCode() ?? 0);
            if (namesKey != navigationNamesKey && !directBox.IsKeyboardFocusWithin)
            {
                navigationNamesKey = namesKey;
                string typed = directBox.Text;
                directBox.ItemsSource = routeNames.Concat(PointNames().Where(n => !routeNames.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
                directBox.Text = typed;
            }
        }
    }
}
