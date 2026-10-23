using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Maps;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the route of the selected aircraft. Points are added by a click on the plan
    /// view, by the name of a point of the maps (or the ICAO of an airport of runways.par), or by magnetic bearing and
    /// distance from the threshold; each has an altitude and a speed (empty: kept as they are). A new point goes after
    /// the selected one, or at the end. Not flown yet, the route is a draft of the window ("Fly route" gives it); while
    /// the aircraft flies it, the list shows the points still to fly and every change is given at once.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        /// <summary>Routes made but not flown yet, by callsign.</summary>
        private readonly Dictionary<string, List<RoutePoint>> drafts = new(StringComparer.OrdinalIgnoreCase);

        private readonly TextBlock routeStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4) };
        private readonly ListBox routeList = new() { Height = 100, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        private readonly TextBox routeAltitudeBox = NumberBox("");
        private readonly TextBlock routeAltitudeCaption = Caption("ft QNH");
        private readonly TextBox routeSpeedBox = NumberBox("");
        private readonly ComboBox routeNameBox = new() { IsEditable = true, IsTextSearchEnabled = false, Width = 110, Height = 22, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Name of a point of the maps of the airport (or the ICAO of an airport), then Add or Enter. The list shows the names." };
        private readonly TextBox routeBearingBox = NumberBox("");
        private readonly TextBox routeDistanceBox = NumberBox("");
        private readonly ToggleButton routeClickButton = new() { Content = "Add by click", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 2, 8, 2), ToolTip = "While pressed, a click on the plan view adds a point there (instead of selecting a track)." };
        private readonly Button routeFlyButton = SmallButton("Fly route");
        private readonly Button routeStopButton = SmallButton("Stop route");
        private readonly Button routeSetButton = SmallButton("Set alt/speed");
        private readonly Button routeRemoveButton = SmallButton("Remove point");
        private readonly Button routeClearButton = SmallButton("Clear");

        /// <summary>The aircraft whose route the list shows, what the list shows (to rebuild it only when it changes), and the point to select after a change.</summary>
        private string? routeShownFor;
        private string routeShownKey = "";
        private RoutePoint? routeSelectNext;
        /// <summary>QFE when the altitude field was last filled (null: not yet).</summary>
        private bool? routeQfe;

        private GroupBox BuildRoute()
        {
            StackPanel panel = new();
            panel.Children.Add(routeStatus);
            panel.Children.Add(routeList);

            WrapPanel values = new() { Margin = new Thickness(0, 6, 0, 0) };
            values.Children.Add(Caption("Altitude"));
            values.Children.Add(routeAltitudeBox);
            values.Children.Add(routeAltitudeCaption);
            values.Children.Add(Caption("speed"));
            values.Children.Add(routeSpeedBox);
            values.Children.Add(Caption("kt"));
            routeSetButton.Margin = new Thickness(8, 0, 0, 0);
            routeSetButton.ToolTip = "Gives this altitude and speed to the point selected in the list.";
            values.Children.Add(routeSetButton);
            panel.Children.Add(values);

            WrapPanel add = new() { Margin = new Thickness(0, 6, 0, 0) };
            add.Children.Add(routeClickButton);
            add.Children.Add(Caption("Name"));
            add.Children.Add(routeNameBox);
            Button addName = SmallButton("Add");
            add.Children.Add(addName);
            add.Children.Add(Caption("or"));
            add.Children.Add(routeBearingBox);
            add.Children.Add(Caption("°M"));
            add.Children.Add(routeDistanceBox);
            add.Children.Add(Caption("NM from the threshold"));
            Button addBearing = SmallButton("Add");
            add.Children.Add(addBearing);
            panel.Children.Add(add);

            WrapPanel actions = new() { Margin = new Thickness(0, 6, 0, 0) };
            routeFlyButton.FontWeight = FontWeights.SemiBold;
            routeFlyButton.ToolTip = "The aircraft starts flying the route: to the first point, then the others in order; after the last it keeps the course of the last leg.";
            routeStopButton.ToolTip = "The aircraft stops flying the route and goes on straight; the points still to fly stay here (Fly route gives them again).";
            routeRemoveButton.ToolTip = "Removes the point selected in the list.";
            routeClearButton.ToolTip = "Removes all the points (while flying: the aircraft goes on straight).";
            actions.Children.Add(routeFlyButton);
            actions.Children.Add(routeStopButton);
            actions.Children.Add(routeRemoveButton);
            actions.Children.Add(routeClearButton);
            panel.Children.Add(actions);
            panel.Children.Add(new TextBlock
            {
                Text = "The list shows the points still to fly. A new point goes after the selected one (or at the end). Altitude or speed empty: kept as they are. An altitude or speed instruction replaces the route's altitude or speed until the route is changed. A heading instruction or the stick ends the route (its points are then gone).",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(2, 6, 2, 0)
            });

            routeAltitudeBox.ToolTip = "Altitude to have at the point (ft; with QFE selected: height above the threshold). Empty: kept. Mouse wheel: 100 ft (+Shift: 1000).";
            routeSpeedBox.ToolTip = "Speed to have at the point (kt). Empty: kept. Mouse wheel: 5 kt (+Shift: 10).";
            routeBearingBox.ToolTip = "Magnetic bearing from the threshold of the runway (degrees). Mouse wheel: 5° (+Shift: 10).";
            routeDistanceBox.ToolTip = "Distance from the threshold (NM). Mouse wheel: 0.5 NM (+Shift: 1).";
            Wheel(routeAltitudeBox, 100, 1000, -2000, 40000, "0", null);
            Wheel(routeSpeedBox, 5, 10, 40, 400, "0", null);
            Wheel(routeBearingBox, 5, 10, 0, 360, "000", null);
            Wheel(routeDistanceBox, 0.5, 1, 0, 250, "0.0", null);

            routeClickButton.Checked += (s, e) => { if (plan != null) plan.PickingPoints = true; };
            routeClickButton.Unchecked += (s, e) => { if (plan != null) plan.PickingPoints = false; };
            routeNameBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                routeNameBox.IsDropDownOpen = false;
                AddNamedPoint();
                e.Handled = true;
            };
            addName.Click += (s, e) => AddNamedPoint();
            addBearing.Click += (s, e) => AddBearingPoint();
            routeBearingBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) AddBearingPoint(); };
            routeDistanceBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) AddBearingPoint(); };
            routeSetButton.Click += (s, e) => SetPointValues();
            routeRemoveButton.Click += (s, e) => RemovePoint();
            routeClearButton.Click += (s, e) => EditRoute(points => points.Clear());
            routeFlyButton.Click += (s, e) => FlyRoute();
            routeStopButton.Click += (s, e) => StopRoute();
            return Group("Route", panel);
        }

        // ----- The points ----------------------------------------------------------------------------------------------

        /// <summary>The points of the selected aircraft: still to fly (flying the route) or of its draft.</summary>
        private static List<RoutePoint> RoutePoints(AircraftState state, Dictionary<string, List<RoutePoint>> drafts) =>
            state.Route != null ? state.Route.Skip(Math.Max(0, state.RouteNext)).ToList()
            : drafts.TryGetValue(state.Callsign, out List<RoutePoint>? draft) ? new List<RoutePoint>(draft) : new List<RoutePoint>();

        /// <summary>Changes the points of the selected aircraft: while it flies the route, given at once; else its draft.</summary>
        private void EditRoute(Action<List<RoutePoint>> change)
        {
            if (Selected() is not AircraftState state)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            List<RoutePoint> points = RoutePoints(state, drafts);
            change(points);
            if (state.Route != null)
            {
                if (points.Count == 0) bridge.Engine.ClearRoute(state.Callsign);
                else bridge.Engine.SetRoute(state.Callsign, points);
            }
            else
            {
                drafts[state.Callsign] = points;
            }
            routeShownKey = "";
            Refresh();
        }

        /// <summary>The point selected in the list, or null.</summary>
        private RoutePoint? SelectedPoint => (routeList.SelectedItem as ListBoxItem)?.Tag as RoutePoint;

        /// <summary>Altitude (ft above mean sea level) and speed (kt) of the fields for a point; NaN when empty. False (and a message) when not valid.</summary>
        private bool ReadPointValues(out double altitude, out double speed)
        {
            altitude = speed = double.NaN;
            if (!string.IsNullOrWhiteSpace(routeAltitudeBox.Text))
            {
                if (!TryRead(routeAltitudeBox, out double value) || value < (Qfe ? 0 : Elevation) || value > 40000)
                {
                    Warn(Qfe ? "Altitude of the point: 0 to 40000 ft above the threshold, or empty." : $"Altitude of the point: {Elevation:0} to 40000 ft, or empty.");
                    return false;
                }
                altitude = Qfe ? value + Elevation : value;
            }
            if (!string.IsNullOrWhiteSpace(routeSpeedBox.Text))
            {
                if (!TryRead(routeSpeedBox, out double value) || value < 40 || value > 400)
                {
                    Warn("Speed of the point: 40 to 400 kt, or empty.");
                    return false;
                }
                speed = value;
            }
            return true;
        }

        /// <summary>Adds a point (in the plane of the engine) after the selected one, or at the end, with the values of the fields.</summary>
        private void AddPoint(double x, double y, string name)
        {
            if (Selected() == null)
            {
                Warn("Select an aircraft first (in the list or with a click on the plan view).");
                return;
            }
            if (!ReadPointValues(out double altitude, out double speed)) return;
            RoutePoint point = new(x, y, altitude, speed, name);
            RoutePoint? after = SelectedPoint;
            // The new point is selected, so the next one goes after it.
            routeSelectNext = point;
            EditRoute(points =>
            {
                int index = after == null ? -1 : points.FindIndex(p => ReferenceEquals(p, after));
                points.Insert(index < 0 ? points.Count : index + 1, point);
            });
        }

        /// <summary>A click on the plan view while "Add by click" is pressed.</summary>
        private void AddClickedPoint(double latitude, double longitude)
        {
            if (bridge.FromGeo(latitude, longitude) is not { } at)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            AddPoint(at.X, at.Y, "");
        }

        /// <summary>The named points of the maps shown on the plan view and the airports of runways.par (name, latitude, longitude).</summary>
        private List<(string Name, double Latitude, double Longitude)> NamedPoints()
        {
            List<(string Name, double Latitude, double Longitude)> result = [];
            if (plan == null) return result;
            foreach (MapLayer layer in plan.Layers)
            {
                foreach (MapPoint point in layer.Map.Points)
                {
                    if (point.Name.Trim().Length > 0) result.Add((point.Name.Trim(), point.Latitude, point.Longitude));
                }
            }
            foreach (IGrouping<string, Runway> airport in plan.Runways.GroupBy(r => r.ICAO.Trim().ToUpperInvariant()))
            {
                if (airport.Key.Length > 0) result.Add((airport.Key, airport.First().Latitude, airport.First().Longitude));
            }
            return result;
        }

        private List<string> PointNames() => NamedPoints().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The maps and runways the names of the name box come from (the list is made again when they change).</summary>
        private object? namesFromLayers;
        private object? namesFromRunways;

        private void RefreshPointNames()
        {
            if (plan == null || (ReferenceEquals(namesFromLayers, plan.Layers) && ReferenceEquals(namesFromRunways, plan.Runways))) return;
            namesFromLayers = plan.Layers;
            namesFromRunways = plan.Runways;
            string typed = routeNameBox.Text;
            routeNameBox.ItemsSource = PointNames();
            routeNameBox.Text = typed;
        }

        private void AddNamedPoint()
        {
            string name = (routeNameBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            // Several points with the same name (in different maps): the nearest to the threshold.
            List<(string Name, double X, double Y)> found = [];
            foreach ((string Name, double Latitude, double Longitude) named in NamedPoints())
            {
                if (!string.Equals(named.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (bridge.FromGeo(named.Latitude, named.Longitude) is { } at) found.Add((named.Name, at.X, at.Y));
            }
            if (found.Count == 0)
            {
                Warn($"No map point or airport named {name}.");
                return;
            }
            (string Name, double X, double Y) nearest = found.OrderBy(p => p.X * p.X + p.Y * p.Y).First();
            AddPoint(nearest.X, nearest.Y, nearest.Name.ToUpperInvariant());
        }

        private void AddBearingPoint()
        {
            if (!TryRead(routeBearingBox, out double bearing) || bearing < 0 || bearing > 360)
            {
                Warn("Bearing: 0 to 360 degrees magnetic, from the threshold.");
                return;
            }
            if (!TryRead(routeDistanceBox, out double distance) || distance < 0 || distance > 250)
            {
                Warn("Distance: 0 to 250 NM from the threshold.");
                return;
            }
            double direction = SimEngine.Normalize(bearing + variation()) * Math.PI / 180;
            AddPoint(distance * Math.Sin(direction), distance * Math.Cos(direction), "");
        }

        private void SetPointValues()
        {
            if (SelectedPoint is not RoutePoint selected)
            {
                Warn("Select a point of the route in the list.");
                return;
            }
            if (!ReadPointValues(out double altitude, out double speed)) return;
            RoutePoint changed = selected with { AltitudeFt = altitude, SpeedKt = speed };
            routeSelectNext = changed;
            EditRoute(points =>
            {
                int index = points.FindIndex(p => ReferenceEquals(p, selected));
                if (index >= 0) points[index] = changed;
            });
        }

        private void RemovePoint()
        {
            if (SelectedPoint is not RoutePoint selected)
            {
                Warn("Select a point of the route in the list.");
                return;
            }
            EditRoute(points => points.RemoveAll(p => ReferenceEquals(p, selected)));
        }

        private void FlyRoute()
        {
            if (Selected() is not AircraftState state || state.Route != null) return;
            if (!drafts.TryGetValue(state.Callsign, out List<RoutePoint>? draft) || draft.Count == 0)
            {
                Warn("The route has no points yet.");
                return;
            }
            bridge.Engine.SetRoute(state.Callsign, draft);
            if (bridge.Engine.Get(state.Callsign)?.Route == null)
            {
                Warn($"{state.Callsign} is on the ground: the route cannot be flown.");
                return;
            }
            drafts.Remove(state.Callsign);
            routeShownKey = "";
            Refresh();
        }

        private void StopRoute()
        {
            if (Selected() is not AircraftState state || state.Route == null) return;
            // The points still to fly stay, as a draft.
            drafts[state.Callsign] = RoutePoints(state, drafts);
            bridge.Engine.ClearRoute(state.Callsign);
            routeShownKey = "";
            Refresh();
        }

        // ----- Showing -------------------------------------------------------------------------------------------------

        /// <summary>Name of a point for the list and the plan view: its own, or magnetic bearing and distance from the threshold.</summary>
        private string PointLabel(RoutePoint point, double magneticVariation)
        {
            if (point.Name.Length > 0) return point.Name;
            double distance = Math.Sqrt(point.X * point.X + point.Y * point.Y);
            double bearing = Math.Atan2(point.X, point.Y) * 180 / Math.PI;
            return string.Format(CultureInfo.InvariantCulture, "{0:000}/{1:0.0}", Magnetic(bearing, magneticVariation), distance);
        }

        private string PointAltitude(RoutePoint point) =>
            double.IsFinite(point.AltitudeFt) ? ((Qfe ? point.AltitudeFt - Elevation : point.AltitudeFt)).ToString("0", CultureInfo.InvariantCulture) : "-----";

        private static string PointSpeed(RoutePoint point) => double.IsFinite(point.SpeedKt) ? point.SpeedKt.ToString("0", CultureInfo.InvariantCulture) : "---";

        /// <summary>The route panel of the selected aircraft and the routes on the plan view (each refresh).</summary>
        private void RefreshRoute(IReadOnlyList<AircraftState> states, double magneticVariation)
        {
            // Drafts of aircraft that are gone.
            foreach (string callsign in drafts.Keys.Where(k => !states.Any(s => string.Equals(s.Callsign, k, StringComparison.OrdinalIgnoreCase))).ToList()) drafts.Remove(callsign);
            routeAltitudeCaption.Text = Qfe ? "ft QFE" : "ft QNH";
            // QNH / QFE switched: the altitude typed keeps its meaning.
            if (routeQfe != Qfe)
            {
                if (routeQfe != null && TryRead(routeAltitudeBox, out double typed))
                {
                    routeAltitudeBox.Text = (Qfe ? typed - Elevation : typed + Elevation).ToString("0", CultureInfo.InvariantCulture);
                }
                routeQfe = Qfe;
                routeShownKey = "";
            }
            RefreshPointNames();
            AircraftState? selected = states.FirstOrDefault(s => string.Equals(s.Callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase));
            List<RoutePoint> points = selected == null ? new List<RoutePoint>() : RoutePoints(selected, drafts);
            bool flying = selected?.Route != null;

            if (selected == null)
            {
                routeStatus.Text = "Select an aircraft (in the list or with a click on the plan view).";
            }
            else if (flying)
            {
                routeStatus.Text = $"{selected.Callsign}: flying the route, next point {(points.Count > 0 ? PointLabel(points[0], magneticVariation) : "")} ({Plural(points.Count)} to fly).";
            }
            else
            {
                routeStatus.Text = points.Count == 0
                    ? $"{selected.Callsign}: no route. Add points below, then Fly route."
                    : $"{selected.Callsign}: route ready, not started ({Plural(points.Count)}). Press Fly route.";
            }
            routeFlyButton.IsEnabled = selected != null && !flying && points.Count > 0;
            routeStopButton.IsEnabled = flying;
            routeSetButton.IsEnabled = routeRemoveButton.IsEnabled = routeClearButton.IsEnabled = points.Count > 0;

            // Another aircraft: the fields start from its values (the last point, or its own).
            if (!string.Equals(routeShownFor, selected?.Callsign, StringComparison.OrdinalIgnoreCase))
            {
                routeShownFor = selected?.Callsign;
                routeShownKey = "";
                if (selected != null)
                {
                    RoutePoint? last = points.LastOrDefault();
                    double altitude = last != null && double.IsFinite(last.AltitudeFt) ? last.AltitudeFt : selected.TargetAltitudeFt;
                    double speed = last != null && double.IsFinite(last.SpeedKt) ? last.SpeedKt : selected.TargetSpeedKt;
                    routeAltitudeBox.Text = (Math.Round((Qfe ? altitude - Elevation : altitude) / 100) * 100).ToString("0", CultureInfo.InvariantCulture);
                    routeSpeedBox.Text = Math.Round(speed).ToString("0", CultureInfo.InvariantCulture);
                }
            }

            // The list, rebuilt only when what it shows changes (so the selection stays).
            List<string> rows = points.Select((p, i) => string.Format(CultureInfo.InvariantCulture, "{0,2}  {1,-12} {2,6} ft  {3,3} kt",
                i + 1, PointLabel(p, magneticVariation), PointAltitude(p), PointSpeed(p))).ToList();
            string key = (flying ? "F|" : "D|") + string.Join("|", rows) + "|" + string.Join(",", points.Select(p => p.GetHashCode()));
            if (key != routeShownKey)
            {
                routeShownKey = key;
                RoutePoint? keep = routeSelectNext ?? SelectedPoint;
                routeSelectNext = null;
                routeList.Items.Clear();
                for (int i = 0; i < points.Count; i++)
                {
                    ListBoxItem item = new() { Content = rows[i], Tag = points[i] };
                    routeList.Items.Add(item);
                    if (keep != null && ReferenceEquals(points[i], keep)) routeList.SelectedItem = item;
                }
                // The same point given again (values equal but a new copy): by value.
                if (routeList.SelectedItem == null && keep != null)
                {
                    routeList.SelectedItem = routeList.Items.OfType<ListBoxItem>().FirstOrDefault(i => Equals(i.Tag, keep));
                }
            }

            // The routes on the plan view: those flown, and the draft of the selected aircraft.
            if (plan == null) return;
            List<PlanRoute> routes = [];
            foreach (AircraftState state in states)
            {
                bool isSelected = selected != null && ReferenceEquals(state, selected);
                List<RoutePoint> shown = state.Route != null ? RoutePoints(state, drafts) : isSelected ? points : new List<RoutePoint>();
                if (shown.Count == 0 || bridge.ToGeo(state.X, state.Y) is not { } start) continue;
                List<PlanRoutePoint> placed = [];
                for (int i = 0; i < shown.Count; i++)
                {
                    if (bridge.ToGeo(shown[i].X, shown[i].Y) is not { } geo) continue;
                    string values = double.IsFinite(shown[i].AltitudeFt)
                        ? Math.Max(0, Math.Round((Qfe ? shown[i].AltitudeFt - Elevation : shown[i].AltitudeFt) / 100)).ToString("000", CultureInfo.InvariantCulture)
                        : "---";
                    placed.Add(new PlanRoutePoint(geo.Latitude, geo.Longitude, $"{i + 1} {PointLabel(shown[i], magneticVariation)}", $"{values} {PointSpeed(shown[i])}"));
                }
                routes.Add(new PlanRoute(start.Latitude, start.Longitude, placed, isSelected, state.Route == null));
            }
            plan.Routes = routes;
            plan.InvalidateVisual();
        }

        private static string Plural(int count) => count == 1 ? "1 point" : $"{count} points";
    }
}
