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
    /// AuroraPAR v2 Trainer, Instructor+: the route editor. It changes the route of the flight plan selected in the
    /// Advanced tab (while it waits), or, in the "Route of …" window opened by Edit route…, the points still to fly of an
    /// aircraft (given at once; MANUAL / ROUTE stay as they are). Points are added by a click on the plan view, by the
    /// name of a point of the maps (or the ICAO of an airport of runways.par), or by magnetic bearing and distance from
    /// the threshold; each has an altitude and a speed (empty: kept). A new point goes after the selected one, or at
    /// the end.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly TextBlock routeStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4) };
        private readonly ListBox routeList = new() { Height = 96, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        private readonly TextBox routeAltitudeBox = NumberBox("");
        private readonly TextBlock routeAltitudeCaption = Caption("ft QNH");
        private readonly TextBox routeSpeedBox = NumberBox("");
        private readonly ComboBox routeNameBox = new() { IsEditable = true, IsTextSearchEnabled = false, Width = 110, Height = 22, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Name of a point of the maps of the airport (or the ICAO of an airport), then Add or Enter. The list shows the names." };
        private readonly TextBox routeBearingBox = NumberBox("");
        private readonly TextBox routeDistanceBox = NumberBox("");
        private readonly ToggleButton routeClickButton = new() { Content = "Add by click", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 2, 8, 2), ToolTip = "While pressed, a click on the plan view adds a point there (instead of selecting a track)." };
        private readonly Button routeSetButton = SmallButton("Set alt/speed");
        private readonly Button routeRemoveButton = SmallButton("Remove point");
        private readonly Button routeClearButton = SmallButton("Clear");
        /// <summary>The editor below its status line: shown only while there is a route to change.</summary>
        private readonly StackPanel routeEditorBody = new();

        /// <summary>The editor (moved into the "Route of …" window while it is open) and its place in the Advanced tab.</summary>
        private FrameworkElement? routeEditor;
        private readonly Border routeEditorHost = new();
        /// <summary>The aircraft whose route the "Route of …" window changes (null: the window is closed).</summary>
        private string? routeEditCallsign;
        private Window? routeWindow;

        /// <summary>What the list shows (to rebuild it only when it changes), and the point to select after a change.</summary>
        private object? routeShownFor;
        private string routeShownKey = "";
        private RoutePoint? routeSelectNext;
        /// <summary>QFE when the altitude field was last filled (null: not yet).</summary>
        private bool? routeQfe;

        private GroupBox BuildRoute()
        {
            StackPanel panel = new();
            panel.Children.Add(routeStatus);
            panel.Children.Add(routeEditorBody);
            routeEditorBody.Children.Add(routeList);

            WrapPanel values = new() { Margin = new Thickness(0, 6, 0, 0) };
            values.Children.Add(Caption("Altitude"));
            values.Children.Add(routeAltitudeBox);
            values.Children.Add(routeAltitudeCaption);
            values.Children.Add(Caption("speed"));
            values.Children.Add(routeSpeedBox);
            values.Children.Add(Caption("kt"));
            routeSetButton.Margin = new Thickness(8, 0, 4, 0);
            routeSetButton.ToolTip = "Gives this altitude and speed to the point selected in the list.";
            values.Children.Add(routeSetButton);
            routeRemoveButton.ToolTip = "Removes the point selected in the list.";
            routeClearButton.ToolTip = "Removes all the points.";
            values.Children.Add(routeRemoveButton);
            values.Children.Add(routeClearButton);
            routeEditorBody.Children.Add(values);

            WrapPanel add = new() { Margin = new Thickness(0, 6, 0, 0) };
            add.Children.Add(routeClickButton);
            add.Children.Add(Caption("Name"));
            add.Children.Add(routeNameBox);
            Button addName = SmallButton("Add");
            add.Children.Add(addName);
            routeEditorBody.Children.Add(add);
            WrapPanel addBearingRow = new() { Margin = new Thickness(0, 4, 0, 0) };
            addBearingRow.Children.Add(Caption("Bearing"));
            addBearingRow.Children.Add(routeBearingBox);
            addBearingRow.Children.Add(Caption("°M"));
            addBearingRow.Children.Add(routeDistanceBox);
            addBearingRow.Children.Add(Caption("NM from the threshold"));
            Button addBearing = SmallButton("Add");
            addBearingRow.Children.Add(addBearing);
            routeEditorBody.Children.Add(addBearingRow);
            routeEditorBody.Children.Add(new TextBlock
            {
                Text = "A new point goes after the selected one (or at the end). Altitude or speed empty: kept as they are. The points' altitudes and speeds are flown in ROUTE until you give the aircraft an altitude or a speed.",
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
            return Group("Route", panel);
        }

        // ----- The points ----------------------------------------------------------------------------------------------

        /// <summary>The points of an aircraft still to fly (the one it flies to, or will fly to, first).</summary>
        private static List<RoutePoint> RemainingPoints(AircraftState state) =>
            state.Route == null ? new List<RoutePoint>() : state.Route.Skip(Math.Clamp(state.RouteNext, 0, state.Route.Count)).ToList();

        /// <summary>The flight plan the editor changes now (selected and waiting, the "Route of …" window closed), or null.</summary>
        private FlightPlan? EditedPlan() =>
            routeEditCallsign == null && routeFpl != null && routeFpl.Active == null && flightPlans.Contains(routeFpl) ? routeFpl : null;

        /// <summary>The aircraft the editor changes now (in the "Route of …" window), or null.</summary>
        private AircraftState? EditedAircraft() => routeEditCallsign == null ? null : bridge.Engine.Get(routeEditCallsign);

        /// <summary>Changes the points of what the editor shows: a flight plan waiting, or the aircraft of the window (given at once).</summary>
        private void EditRoute(Action<List<RoutePoint>> change)
        {
            if (EditedPlan() is FlightPlan edited)
            {
                List<RoutePoint> planned = new(edited.Points);
                change(planned);
                edited.Points = planned;
                routeShownKey = fplShownKey = "";
                Refresh();
                return;
            }
            if (EditedAircraft() is not AircraftState state)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            List<RoutePoint> points = RemainingPoints(state);
            change(points);
            if (points.Count == 0) bridge.Engine.ClearRoute(state.Callsign);
            else bridge.Engine.SetRoute(state.Callsign, points, follow: null);
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
            if (EditedPlan() == null && EditedAircraft() == null)
            {
                Warn("Select a waiting flight plan (Advanced tab), or open Edit route… of an aircraft.");
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

        /// <summary>
        /// A click on the plan view while "Add by click" is pressed: near points of the maps (VRP, FIX, VOR...), a menu to
        /// choose one of them or a GPS point where the click was (as SkyVector); else the GPS point at once.
        /// </summary>
        private void AddClickedPoint(double latitude, double longitude, Point where)
        {
            if (bridge.FromGeo(latitude, longitude) is not { } at)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            ChoosePoint(at.X, at.Y, where, (x, y, name) => AddPoint(x, y, name), null);
        }

        /// <summary>
        /// A point of the selected route dropped somewhere else on the plan view (also while it is flown): it goes there,
        /// or to a point of the maps chosen in the menu; its altitude and speed stay. Flying to it now: straight to the new place.
        /// </summary>
        private void MoveDraggedPoint(string? callsign, object tag, double latitude, double longitude, Point where)
        {
            if (tag is not RoutePoint moved || bridge.FromGeo(latitude, longitude) is not { } at)
            {
                plan?.EndRouteMove();
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            ChoosePoint(at.X, at.Y, where, (x, y, name) => MovePoint(callsign, moved, x, y, name), () => plan?.EndRouteMove());
        }

        /// <summary>Moves a point of a route: the flight plan being made (callsign null) or the points still to fly of an aircraft.</summary>
        private void MovePoint(string? callsign, RoutePoint moved, double x, double y, string name)
        {
            RoutePoint changed = moved with { X = x, Y = y, Name = name };
            if (callsign == null)
            {
                if (EditedPlan() is not FlightPlan edited || !edited.Points.Any(p => ReferenceEquals(p, moved)))
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                routeSelectNext = changed;
                edited.Points = edited.Points.Select(p => ReferenceEquals(p, moved) ? changed : p).ToList();
                routeShownKey = fplShownKey = "";
                Refresh();
                return;
            }
            // The aircraft may have passed the point meanwhile: then nothing.
            if (bridge.Engine.Get(callsign) is not AircraftState state)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            List<RoutePoint> points = RemainingPoints(state);
            int index = points.FindIndex(p => ReferenceEquals(p, moved));
            if (index < 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            points[index] = changed;
            if (string.Equals(routeEditCallsign, callsign, StringComparison.OrdinalIgnoreCase)) routeSelectNext = changed;
            // Given again as an edit of the route (ROUTE / MANUAL as they are): the leg flown changes only when this is the point it flies to.
            bridge.Engine.SetRoute(callsign, points, follow: null);
            routeShownKey = "";
            Refresh();
        }

        /// <summary>
        /// The place of a new or moved point (x, y in the plane of the engine): with points of the maps within a few pixels
        /// of <paramref name="where"/>, a menu (each of them, or a GPS point here); else the GPS point at once.
        /// <paramref name="done"/> runs in the end, chosen or not.
        /// </summary>
        private void ChoosePoint(double x, double y, Point where, Action<double, double, string> chosen, Action? done)
        {
            List<(string Name, MapSymbol Symbol, double Latitude, double Longitude)> near = plan?.MapPointsNear(where) ?? [];
            if (near.Count == 0)
            {
                chosen(x, y, "");
                done?.Invoke();
                return;
            }
            ContextMenu menu = new() { PlacementTarget = plan, Placement = PlacementMode.MousePoint };
            foreach ((string name, MapSymbol symbol, double latitude, double longitude) in near)
            {
                if (bridge.FromGeo(latitude, longitude) is not { } at) continue;
                MenuItem item = new() { Header = $"{SymbolKind(symbol)}  {name}", FontWeight = FontWeights.SemiBold };
                item.Click += (s, e) => chosen(at.X, at.Y, name);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            MenuItem gps = new() { Header = $"GPS point here  ({PointLabel(new RoutePoint(x, y, double.NaN, double.NaN), variation())})" };
            gps.Click += (s, e) => chosen(x, y, "");
            menu.Items.Add(gps);
            // After the Click of the item chosen (it comes once the menu is closed).
            menu.Closed += (s, e) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => done?.Invoke()));
            // Opened after the mouse event is over.
            Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true));
        }

        private static string SymbolKind(MapSymbol symbol) => symbol switch
        {
            MapSymbol.Vrp => "VRP",
            MapSymbol.Vor => "VOR",
            MapSymbol.Vortac => "VORTAC",
            MapSymbol.Tacan => "TACAN",
            MapSymbol.Ndb => "NDB",
            MapSymbol.Airport => "AD",
            _ => "FIX"
        };

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

        /// <summary>A named point of the maps (or an airport) in the plane: the nearest to the threshold when several have the name; null: none.</summary>
        private (string Name, double X, double Y)? FindNamedPoint(string name)
        {
            List<(string Name, double X, double Y)> found = [];
            foreach ((string Name, double Latitude, double Longitude) named in NamedPoints())
            {
                if (!string.Equals(named.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (bridge.FromGeo(named.Latitude, named.Longitude) is { } at) found.Add((named.Name.ToUpperInvariant(), at.X, at.Y));
            }
            if (found.Count == 0) return null;
            return found.OrderBy(p => p.X * p.X + p.Y * p.Y).First();
        }

        /// <summary>The maps and runways the names of the name boxes come from (the lists are made again when they change).</summary>
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
            navigationNamesKey = "";
        }

        private void AddNamedPoint()
        {
            string name = (routeNameBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            if (FindNamedPoint(name) is not { } found)
            {
                Warn($"No map point or airport named {name}.");
                return;
            }
            AddPoint(found.X, found.Y, found.Name);
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

        // ----- The "Route of …" window ---------------------------------------------------------------------------------

        /// <summary>Edit route…: the editor in a small window for the selected aircraft (the points still to fly).</summary>
        private void OpenRouteWindow()
        {
            if (SelectedCallsign is not string callsign || routeEditor == null) return;
            if (routeWindow != null)
            {
                if (string.Equals(routeEditCallsign, callsign, StringComparison.OrdinalIgnoreCase))
                {
                    routeWindow.Activate();
                    return;
                }
                routeWindow.Close();
            }
            routeEditCallsign = callsign;
            routeEditorHost.Child = new TextBlock { Text = $"The route editor is in the window \"Route of {callsign}\".", Foreground = Brushes.Gray, Margin = new Thickness(4) };
            Border holder = new() { Padding = new Thickness(10), Child = routeEditor };
            Window window = new()
            {
                Title = $"Route of {callsign}",
                Owner = this,
                Width = 600,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = holder
            };
            window.Closed += (s, e) =>
            {
                holder.Child = null;
                routeEditorHost.Child = routeEditor;
                routeEditCallsign = null;
                routeWindow = null;
                routeShownKey = "";
                Refresh();
            };
            routeWindow = window;
            routeShownKey = "";
            Refresh();
            window.Show();
        }

        // ----- Showing -------------------------------------------------------------------------------------------------

        /// <summary>Name of a point for the lists and the plan view: its own, or magnetic bearing and distance from the threshold.</summary>
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

        /// <summary>The route editor and the routes on the plan view (each refresh).</summary>
        private void RefreshRoute(IReadOnlyList<AircraftState> states, double magneticVariation)
        {
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
            // The aircraft of the window gone: the window closes.
            if (routeEditCallsign != null && !states.Any(s => string.Equals(s.Callsign, routeEditCallsign, StringComparison.OrdinalIgnoreCase)))
            {
                routeWindow?.Close();
            }
            FlightPlan? edited = EditedPlan();
            AircraftState? aircraft = routeEditCallsign == null ? null : states.FirstOrDefault(s => string.Equals(s.Callsign, routeEditCallsign, StringComparison.OrdinalIgnoreCase));
            List<RoutePoint> points = edited != null ? new List<RoutePoint>(edited.Points) : aircraft != null ? RemainingPoints(aircraft) : new List<RoutePoint>();

            if (edited != null)
            {
                string name = edited.Callsign.Length > 0 ? edited.Callsign : "(next free callsign)";
                routeStatus.Text = points.Count == 0
                    ? $"Flight plan {name}: no route yet. Add its points (the first one with altitude and speed: the aircraft starts there)."
                    : $"Flight plan {name}: {Plural(points.Count)}. Activate starts it on the first point.";
            }
            else if (aircraft != null)
            {
                routeStatus.Text = $"{aircraft.Callsign}: {Plural(points.Count)} still to fly ({NavText(aircraft)}). Changes are given at once.";
            }
            else if (routeFpl != null && routeFpl.Active != null)
            {
                routeStatus.Text = $"{routeFpl.Active} is flying this plan: change its route with Edit route… in Aircraft control.";
            }
            else
            {
                routeStatus.Text = "Select a flight plan (New FPL makes one).";
            }
            bool editing = edited != null || aircraft != null;
            routeEditorBody.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            routeSetButton.IsEnabled = routeRemoveButton.IsEnabled = routeClearButton.IsEnabled = points.Count > 0;
            routeClickButton.IsEnabled = editing;
            if (!editing && routeClickButton.IsChecked == true) routeClickButton.IsChecked = false;

            // Another plan or aircraft: the fields start from its values (the last point, or the aircraft's own).
            object? target = (object?)edited ?? aircraft?.Callsign;
            if (!Equals(routeShownFor, target))
            {
                routeShownFor = target;
                routeShownKey = "";
                RoutePoint? last = points.LastOrDefault();
                if (edited != null && last == null)
                {
                    // A new plan: a start the aircraft can fly (3000 ft above the threshold, the approach speed of its class).
                    PerformanceClass? kind = bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, edited.Class, StringComparison.OrdinalIgnoreCase));
                    if (!TryRead(routeAltitudeBox, out _)) routeAltitudeBox.Text = (Qfe ? 3000 : Math.Round((Elevation + 3000) / 100) * 100).ToString("0", CultureInfo.InvariantCulture);
                    if (!TryRead(routeSpeedBox, out _) && kind != null) routeSpeedBox.Text = kind.ApproachSpeedKt.ToString("0", CultureInfo.InvariantCulture);
                }
                double altitude = last != null && double.IsFinite(last.AltitudeFt) ? last.AltitudeFt : aircraft?.TargetAltitudeFt ?? double.NaN;
                double speed = last != null && double.IsFinite(last.SpeedKt) ? last.SpeedKt : aircraft?.TargetSpeedKt ?? double.NaN;
                if (double.IsFinite(altitude)) routeAltitudeBox.Text = (Math.Round((Qfe ? altitude - Elevation : altitude) / 100) * 100).ToString("0", CultureInfo.InvariantCulture);
                if (double.IsFinite(speed)) routeSpeedBox.Text = Math.Round(speed).ToString("0", CultureInfo.InvariantCulture);
            }

            // The list, rebuilt only when what it shows changes (so the selection stays).
            List<string> rows = points.Select((p, i) => string.Format(CultureInfo.InvariantCulture, "{0,2}  {1,-12} {2,6} ft  {3,3} kt",
                i + 1, PointLabel(p, magneticVariation), PointAltitude(p), PointSpeed(p))).ToList();
            string key = (edited != null ? "P|" : aircraft != null ? "A|" : "-|") + string.Join("|", rows) + "|" + string.Join(",", points.Select(p => p.GetHashCode()));
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
                if (routeList.SelectedItem == null && keep != null)
                {
                    routeList.SelectedItem = routeList.Items.OfType<ListBoxItem>().FirstOrDefault(i => Equals(i.Tag, keep));
                }
            }

            // The routes on the plan view: the plan being made (from its first point), the routes of the aircraft (the
            // selected one bright, dashed in MANUAL; the others dim).
            if (plan == null) return;
            List<PlanRoute> routes = [];
            if (edited != null && points.Count > 0 && bridge.ToGeo(points[0].X, points[0].Y) is { } first)
            {
                plan.SelectedCallsign = null;
                routes.Add(new PlanRoute(first.Latitude, first.Longitude, PlacePoints(points, magneticVariation), true, true, null));
            }
            foreach (AircraftState state in states)
            {
                List<RoutePoint> shown = RemainingPoints(state);
                if (shown.Count == 0 || bridge.ToGeo(state.X, state.Y) is not { } start) continue;
                bool isSelected = edited == null && string.Equals(state.Callsign, SelectedCallsign, StringComparison.OrdinalIgnoreCase);
                routes.Add(new PlanRoute(start.Latitude, start.Longitude, PlacePoints(shown, magneticVariation), isSelected, state.Navigation == NavigationMode.Manual, state.Callsign));
            }
            plan.Routes = routes;
            plan.InvalidateVisual();
        }

        /// <summary>The points of a route for the plan view: position, number and name, altitude (hundreds of ft) and speed.</summary>
        private List<PlanRoutePoint> PlacePoints(List<RoutePoint> shown, double magneticVariation)
        {
            List<PlanRoutePoint> placed = [];
            for (int i = 0; i < shown.Count; i++)
            {
                if (bridge.ToGeo(shown[i].X, shown[i].Y) is not { } geo) continue;
                string values = double.IsFinite(shown[i].AltitudeFt)
                    ? Math.Max(0, Math.Round((Qfe ? shown[i].AltitudeFt - Elevation : shown[i].AltitudeFt) / 100)).ToString("000", CultureInfo.InvariantCulture)
                    : "---";
                placed.Add(new PlanRoutePoint(geo.Latitude, geo.Longitude, $"{i + 1} {PointLabel(shown[i], magneticVariation)}", $"{values} {PointSpeed(shown[i])}", shown[i]));
            }
            return placed;
        }

        private static string Plural(int count) => count == 1 ? "1 point" : $"{count} points";
    }
}
