using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the flight plans. New FPL makes a plan (callsign, class; its route in the Route
    /// group), Duplicate copies the selected one, Activate (beside each plan) starts its aircraft on the first point,
    /// with that point's altitude and speed, towards the second point, flying the route. A plan stays in the list:
    /// once its aircraft is gone it waits again. Save scenario / Load scenario write and read all the plans.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly List<FlightPlan> flightPlans = [];
        private readonly ListBox fplList = new() { Height = 120, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox fplCallsignBox = new() { Width = 80, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Callsign of the plan (2 to 10 letters or digits). Empty: the next free callsign of the prefix when activated." };
        private readonly ComboBox fplClassBox = new() { Width = 100, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Performance class of the aircraft (speeds, climb, descent, turns)." };
        /// <summary>The plan whose route the Route group shows (while it waits), or null.</summary>
        private FlightPlan? routeFpl;
        private string fplShownKey = "";
        /// <summary>The selection of a list is being set by the program (not a click): the other list does not follow.</summary>
        private bool fplSync;

        private GroupBox BuildFlightPlans()
        {
            StackPanel panel = new();
            panel.Children.Add(fplList);
            WrapPanel fields = new() { Margin = new Thickness(0, 6, 0, 0) };
            fields.Children.Add(Caption("Callsign"));
            fields.Children.Add(fplCallsignBox);
            fields.Children.Add(Caption("Class"));
            fields.Children.Add(fplClassBox);
            Button newPlan = SmallButton("New FPL");
            newPlan.FontWeight = FontWeights.SemiBold;
            newPlan.ToolTip = "A new flight plan: then make its route in the route editor below.";
            Button duplicate = SmallButton("Duplicate");
            duplicate.ToolTip = "A copy of the selected flight plan (its whole route), with the next number (AZA101 → AZA102); the route can then be changed.";
            Button remove = SmallButton("Remove FPL");
            remove.ToolTip = "Removes the selected flight plan (an aircraft already flying it goes on).";
            fields.Children.Add(newPlan);
            fields.Children.Add(duplicate);
            fields.Children.Add(remove);
            panel.Children.Add(fields);
            WrapPanel files = new() { Margin = new Thickness(0, 6, 0, 0) };
            Button save = SmallButton("Save scenario...");
            save.ToolTip = "Saves all the flight plans in a file (folder Scenarios of the Trainer).";
            Button load = SmallButton("Load scenario...");
            load.ToolTip = "Loads a saved scenario: it replaces the waiting flight plans; aircraft already flying are not affected.";
            files.Children.Add(save);
            files.Children.Add(load);
            panel.Children.Add(files);
            panel.Children.Add(new TextBlock
            {
                Text = "New FPL, then its route in the editor below (the first point needs altitude and speed: the aircraft starts there). Activate starts the aircraft when you decide. Callsign and class change the selected plan while it waits.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(2, 6, 2, 0)
            });

            fplClassBox.ItemsSource = bridge.Engine.Classes.Select(c => c.Name).ToList();
            fplClassBox.SelectedItem = DefaultClass();
            newPlan.Click += (s, e) => NewFlightPlan();
            duplicate.Click += (s, e) => DuplicateFlightPlan();
            remove.Click += (s, e) => RemoveFlightPlan();
            save.Click += (s, e) => SaveScenario();
            load.Click += (s, e) => LoadScenario();
            fplCallsignBox.LostFocus += (s, e) => ApplyFplCallsign();
            fplCallsignBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) ApplyFplCallsign(); };
            fplClassBox.SelectionChanged += (s, e) =>
            {
                if (fplSync || SelectedFpl is not FlightPlan plan || plan.Active != null || fplClassBox.SelectedItem is not string name) return;
                plan.Class = name;
                fplShownKey = "";
                Refresh();
            };
            fplList.SelectionChanged += (s, e) =>
            {
                if (fplSync) return;
                routeFpl = SelectedFpl;
                ShowFplFields();
                // A plan being flown: its aircraft is selected.
                if (routeFpl?.Active is string callsign)
                {
                    fplSync = true;
                    try { SelectCallsign(callsign); } finally { fplSync = false; }
                }
                routeShownKey = "";
                Refresh();
            };
            return Group("Flight plans", panel);
        }

        private string DefaultClass() =>
            bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, PerformanceClass.DefaultName, StringComparison.OrdinalIgnoreCase))?.Name ?? bridge.Engine.Classes[0].Name;

        private FlightPlan? SelectedFpl => (fplList.SelectedItem as ListBoxItem)?.Tag as FlightPlan;

        /// <summary>A click on the aircraft already selected while the Route group shows a flight plan: back to the aircraft.</summary>
        private void AircraftClickedAgain()
        {
            if (fplSync || EditedPlan() == null || SelectedCallsign == null) return;
            AircraftPicked();
            Refresh();
        }

        /// <summary>An aircraft chosen in the list or on the plan view (by the user): the plans follow (its plan, or none).</summary>
        private void AircraftPicked()
        {
            if (fplSync) return;
            routeFpl = null;
            FlightPlan? plan = flightPlans.FirstOrDefault(p => p.Active != null && string.Equals(p.Active, SelectedCallsign, StringComparison.OrdinalIgnoreCase));
            fplSync = true;
            try
            {
                fplList.SelectedItem = fplList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, plan));
                if (plan != null) routeFpl = plan;
            }
            finally
            {
                fplSync = false;
            }
            ShowFplFields();
            routeShownKey = "";
        }

        private void ShowFplFields()
        {
            fplSync = true;
            try
            {
                FlightPlan? plan = SelectedFpl;
                fplCallsignBox.Text = plan?.Callsign ?? "";
                fplCallsignBox.IsEnabled = fplClassBox.IsEnabled = plan == null || plan.Active == null;
                if (plan != null) fplClassBox.SelectedItem = plan.Class;
            }
            finally
            {
                fplSync = false;
            }
        }

        /// <summary>The origin of the plane of the engine now (the threshold of the reference runway), or null.</summary>
        private (double Latitude, double Longitude, double Heading)? Origin() =>
            bridge.Reference is Runway runway ? (runway.Latitude, runway.Longitude, runway.Heading) : null;

        /// <summary>The points of a plan placed again on the plane of the engine if the reference runway changed.</summary>
        private void PlaceAgain(FlightPlan plan)
        {
            if (Origin() is not { } origin) return;
            if (plan.OriginLatitude == origin.Latitude && plan.OriginLongitude == origin.Longitude) return;
            if (double.IsFinite(plan.OriginLatitude))
            {
                LocalPlane old = new(plan.OriginLatitude, plan.OriginLongitude, plan.OriginHeading);
                List<RoutePoint> placed = [];
                foreach (RoutePoint point in plan.Points)
                {
                    (double latitude, double longitude) = old.ToGeo(point.X, point.Y);
                    if (bridge.FromGeo(latitude, longitude) is { } at) placed.Add(point with { X = at.X, Y = at.Y });
                }
                plan.Points = placed;
            }
            plan.OriginLatitude = origin.Latitude;
            plan.OriginLongitude = origin.Longitude;
            plan.OriginHeading = origin.Heading;
        }

        private FlightPlan NewPlan(string callsign, string kind, List<RoutePoint> points)
        {
            FlightPlan plan = new() { Callsign = callsign, Class = kind, Points = points };
            if (Origin() is { } origin)
            {
                plan.OriginLatitude = origin.Latitude;
                plan.OriginLongitude = origin.Longitude;
                plan.OriginHeading = origin.Heading;
            }
            return plan;
        }

        private void NewFlightPlan()
        {
            if (bridge.Reference == null)
            {
                Warn("No runway: choose the runway of the radar first.");
                return;
            }
            // The callsign after the one of the last plan that has one (or the next free one of the prefix at the start); the
            // class of the selected plan. Both can then be changed in the fields.
            FlightPlan? previous = SelectedFpl ?? flightPlans.LastOrDefault();
            string? lastCallsign = flightPlans.LastOrDefault(p => p.Callsign.Length > 0)?.Callsign;
            string callsign = lastCallsign != null ? NextCallsign(lastCallsign) : "";
            // No plan selected: the callsign typed in the box is the one of the new plan (the next one if another plan has it).
            string typed = fplCallsignBox.Text.Trim().ToUpperInvariant();
            if (SelectedFpl == null && typed.Length > 0)
            {
                if (!ValidCallsign(typed))
                {
                    Warn("Callsign: 2 to 10 letters or digits, or empty for the next free one.");
                    return;
                }
                bool taken = CallsignInPlans(typed, null) || bridge.Engine.IsCallsignTaken(typed) || otherCallsigns().Contains(typed, StringComparer.OrdinalIgnoreCase);
                callsign = taken ? NextCallsign(typed) : typed;
                if (callsign.Length == 0)
                {
                    Warn($"{typed} is taken and no next callsign is free: type another one.");
                    return;
                }
            }
            FlightPlan plan = NewPlan(callsign, previous?.Class ?? DefaultClass(), []);
            flightPlans.Add(plan);
            SelectPlan(plan);
        }

        private void DuplicateFlightPlan()
        {
            if (SelectedFpl is not FlightPlan source)
            {
                Warn("Select a flight plan to duplicate.");
                return;
            }
            PlaceAgain(source);
            FlightPlan copy = NewPlan(source.Callsign.Length > 0 ? NextCallsign(source.Callsign) : "", source.Class, source.Points.Select(p => p with { }).ToList());
            flightPlans.Insert(flightPlans.IndexOf(source) + 1, copy);
            SelectPlan(copy);
        }

        private void RemoveFlightPlan()
        {
            if (SelectedFpl is not FlightPlan plan) return;
            flightPlans.Remove(plan);
            if (ReferenceEquals(routeFpl, plan)) routeFpl = null;
            fplShownKey = routeShownKey = "";
            Refresh();
            ShowFplFields();
        }

        /// <summary>Selects a plan in the list (and its route in the Route group).</summary>
        private void SelectPlan(FlightPlan plan)
        {
            routeFpl = plan;
            fplShownKey = routeShownKey = "";
            Refresh();
            fplSync = true;
            try { fplList.SelectedItem = fplList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, plan)); }
            finally { fplSync = false; }
            ShowFplFields();
            routeShownKey = "";
            Refresh();
        }

        private void ApplyFplCallsign()
        {
            if (fplSync || SelectedFpl is not FlightPlan plan || plan.Active != null) return;
            string callsign = fplCallsignBox.Text.Trim().ToUpperInvariant();
            if (callsign == plan.Callsign) return;
            if (callsign.Length > 0 && !ValidCallsign(callsign))
            {
                Warn("Callsign: 2 to 10 letters or digits, or empty for the next free one.");
                fplCallsignBox.Text = plan.Callsign;
                return;
            }
            if (callsign.Length > 0 && CallsignInPlans(callsign, plan))
            {
                Warn($"{callsign} is already the callsign of another flight plan.");
                fplCallsignBox.Text = plan.Callsign;
                return;
            }
            plan.Callsign = callsign;
            fplCallsignBox.Text = callsign;
            fplShownKey = "";
            Refresh();
        }

        private static bool ValidCallsign(string callsign) => callsign.Length is >= 2 and <= 10 && callsign.All(char.IsAsciiLetterOrDigit);

        private bool CallsignInPlans(string callsign, FlightPlan? except) =>
            flightPlans.Any(p => !ReferenceEquals(p, except) && string.Equals(p.Callsign, callsign, StringComparison.OrdinalIgnoreCase));

        /// <summary>The next callsign not used by a plan or an aircraft: the number at its end + 1 (AZA101 → AZA102), or a 2 added.</summary>
        private string NextCallsign(string callsign)
        {
            int digits = callsign.Length - callsign.TrimEnd("0123456789".ToCharArray()).Length;
            string stem = callsign[..(callsign.Length - digits)];
            long number = digits > 0 && long.TryParse(callsign[^digits..], NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : 1;
            for (int i = 0; i < 1000; i++)
            {
                number++;
                string candidate = stem + number.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
                if (candidate.Length > 10) break;
                if (!CallsignInPlans(candidate, null) && !bridge.Engine.IsCallsignTaken(candidate)
                    && !otherCallsigns().Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
            }
            return "";
        }

        private void Activate(FlightPlan plan)
        {
            if (plan.Active != null) return;
            PlaceAgain(plan);
            if (plan.Points.Count == 0)
            {
                Warn("The flight plan has no route yet: add its points in the route editor.");
                return;
            }
            RoutePoint first = plan.Points[0];
            if (!double.IsFinite(first.AltitudeFt) || !double.IsFinite(first.SpeedKt))
            {
                Warn("The first point needs an altitude and a speed: the aircraft starts there.");
                return;
            }
            if (plan.Callsign.Length > 0 && bridge.Engine.IsCallsignTaken(plan.Callsign))
            {
                string next = NextCallsign(plan.Callsign);
                if (next.Length == 0 || MessageBox.Show(this, $"{plan.Callsign} is already flying. Start this plan as {next}?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                plan.Callsign = next;
            }
            // Towards the second point (with only one point: towards the threshold).
            double toX = plan.Points.Count > 1 ? plan.Points[1].X - first.X : -first.X;
            double toY = plan.Points.Count > 1 ? plan.Points[1].Y - first.Y : -first.Y;
            double heading = Math.Abs(toX) + Math.Abs(toY) > 1e-9 ? SimEngine.Normalize(Math.Atan2(toX, toY) * 180 / Math.PI) : bridge.Reference?.Heading ?? 0;
            try
            {
                string name = bridge.Add(first.X, first.Y, first.AltitudeFt, heading, first.SpeedKt, plan.Callsign.Length > 0 ? plan.Callsign : null, otherCallsigns(), plan.Class);
                bridge.Engine.SetTurnRateMode(name, CurrentTurnMode());
                if (plan.Points.Count > 1) bridge.Engine.SetRoute(name, plan.Points.Skip(1));
                plan.Active = name;
                routeFpl = plan;
                fplShownKey = routeShownKey = "";
                Refresh();
                fplSync = true;
                try
                {
                    fplList.SelectedItem = fplList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, plan));
                    SelectCallsign(name);
                }
                finally
                {
                    fplSync = false;
                }
                ShowFplFields();
                Refresh();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Warn(ex.Message);
            }
        }

        // ----- Scenarios ---------------------------------------------------------------------------------------------

        private void SaveScenario()
        {
            if (flightPlans.Count == 0)
            {
                Warn("No flight plans to save.");
                return;
            }
            Runway? runway = bridge.Reference;
            ScenarioFile file = new() { Airport = runway?.ICAO ?? "", Runway = runway?.DisplayName ?? "" };
            foreach (FlightPlan plan in flightPlans)
            {
                PlaceAgain(plan);
                ScenarioPlan saved = new() { Callsign = plan.Callsign, Class = plan.Class };
                foreach (RoutePoint point in plan.Points)
                {
                    if (bridge.ToGeo(point.X, point.Y) is not { } geo) continue;
                    saved.Points.Add(new ScenarioPoint
                    {
                        Name = point.Name,
                        Latitude = Math.Round(geo.Latitude, 7),
                        Longitude = Math.Round(geo.Longitude, 7),
                        AltitudeFt = double.IsFinite(point.AltitudeFt) ? point.AltitudeFt : null,
                        SpeedKt = double.IsFinite(point.SpeedKt) ? point.SpeedKt : null
                    });
                }
                file.FlightPlans.Add(saved);
            }
            try
            {
                System.IO.Directory.CreateDirectory(ScenarioFile.Folder);
                SaveFileDialog dialog = new()
                {
                    Title = "Save scenario",
                    Filter = "Scenario (*.json)|*.json",
                    InitialDirectory = ScenarioFile.Folder,
                    FileName = $"{(file.Airport.Length > 0 ? file.Airport + " " : "")}scenario.json"
                };
                if (dialog.ShowDialog(this) != true) return;
                file.Save(dialog.FileName);
            }
            catch (Exception ex)
            {
                Warn($"Cannot save the scenario:\n{ex.Message}");
            }
        }

        private void LoadScenario()
        {
            if (bridge.Reference is not Runway runway)
            {
                Warn("No runway: choose the runway of the radar first.");
                return;
            }
            ScenarioFile file;
            try
            {
                System.IO.Directory.CreateDirectory(ScenarioFile.Folder);
                OpenFileDialog dialog = new()
                {
                    Title = "Load scenario",
                    Filter = "Scenario (*.json)|*.json",
                    InitialDirectory = ScenarioFile.Folder
                };
                if (dialog.ShowDialog(this) != true) return;
                file = ScenarioFile.Load(dialog.FileName);
            }
            catch (System.Text.Json.JsonException)
            {
                Warn("This file is not a valid scenario.");
                return;
            }
            catch (Exception ex)
            {
                Warn($"Cannot read the scenario:\n{ex.Message}");
                return;
            }
            if (!string.IsNullOrWhiteSpace(file.Airport) && !string.Equals(file.Airport.Trim(), runway.ICAO, StringComparison.OrdinalIgnoreCase)
                && MessageBox.Show(this, $"This scenario was made for {file.Airport}; the runway in use is at {runway.ICAO}. Load it anyway?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (file.FlightPlans == null || file.FlightPlans.Count == 0)
            {
                Warn("No flight plans in this file.");
                return;
            }
            int waiting = flightPlans.Count(p => p.Active == null);
            if (waiting > 0
                && MessageBox.Show(this, $"The {(waiting == 1 ? "waiting flight plan" : $"{waiting} waiting flight plans")} will be replaced by those of the scenario. Continue?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            // Plans being flown stay in the list (their aircraft go on).
            flightPlans.RemoveAll(p => p.Active == null);
            List<string> classes = bridge.Engine.Classes.Select(c => c.Name).ToList();
            List<FlightPlan> loaded = [];
            foreach (ScenarioPlan saved in file.FlightPlans ?? new List<ScenarioPlan>())
            {
                List<RoutePoint> points = [];
                foreach (ScenarioPoint point in saved.Points ?? new List<ScenarioPoint>())
                {
                    if (point.Latitude is not double latitude || point.Longitude is not double longitude || !double.IsFinite(latitude) || !double.IsFinite(longitude)
                        || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180 || bridge.FromGeo(latitude, longitude) is not { } at) continue;
                    // Far away (a wrong number in the file): left out.
                    if (Math.Sqrt(at.X * at.X + at.Y * at.Y) > 300) continue;
                    points.Add(new RoutePoint(at.X, at.Y, point.AltitudeFt is double altitude && double.IsFinite(altitude) ? altitude : double.NaN,
                        point.SpeedKt is double speed && double.IsFinite(speed) ? speed : double.NaN, (point.Name ?? "").Trim()));
                }
                string callsign = (saved.Callsign ?? "").Trim().ToUpperInvariant();
                if (!ValidCallsign(callsign)) callsign = "";
                string kind = classes.FirstOrDefault(c => string.Equals(c, saved.Class, StringComparison.OrdinalIgnoreCase)) ?? DefaultClass();
                // Each plan added at once, so a callsign repeated in the file (or of a plan being flown) gets the next one.
                if (callsign.Length > 0 && CallsignInPlans(callsign, null)) callsign = NextCallsign(callsign);
                FlightPlan plan = NewPlan(callsign, kind, points);
                flightPlans.Add(plan);
                loaded.Add(plan);
            }
            routeFpl = null;
            fplShownKey = routeShownKey = "";
            Refresh();
            if (loaded.Count > 0) SelectPlan(loaded[0]);
        }

        // ----- Showing -------------------------------------------------------------------------------------------------

        /// <summary>The list of the plans (each refresh; rebuilt only when it changes): callsign, class, points, state, and Activate.</summary>
        private void RefreshFlightPlans(IReadOnlyList<AircraftState> states)
        {
            foreach (FlightPlan plan in flightPlans)
            {
                PlaceAgain(plan);
                // Its aircraft gone (removed, landed): waiting again.
                if (plan.Active != null && !states.Any(s => string.Equals(s.Callsign, plan.Active, StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Active = null;
                    if (ReferenceEquals(plan, SelectedFpl)) ShowFplFields();
                    else if (ReferenceEquals(plan, routeFpl)) routeFpl = null;
                }
            }
            string Row(FlightPlan plan)
            {
                AircraftState? state = plan.Active == null ? null : states.FirstOrDefault(s => string.Equals(s.Callsign, plan.Active, StringComparison.OrdinalIgnoreCase));
                string status = state == null ? "waiting" : state.Navigation == NavigationMode.Route ? "flying the route" : state.Navigation == NavigationMode.RouteArmed ? "flying (ROUTE, choose a point)" : "flying (MANUAL)";
                string name = plan.Active ?? (plan.Callsign.Length > 0 ? plan.Callsign : "(next free)");
                return string.Format(CultureInfo.InvariantCulture, "{0,-10} {1,-10} {2,2} points  {3}", name, plan.Class, plan.Points.Count, status);
            }
            List<string> rows = flightPlans.Select(Row).ToList();
            // The rows are changed in place; the items are made again only when the plans themselves change (so a
            // click on a row or on its Activate button is never lost under a rebuild).
            bool samePlans = fplList.Items.Count == flightPlans.Count
                && fplList.Items.OfType<ListBoxItem>().Select(i => i.Tag).SequenceEqual(flightPlans.Cast<object>());
            if (samePlans)
            {
                for (int i = 0; i < flightPlans.Count; i++)
                {
                    if (fplList.Items[i] is ListBoxItem item && item.Content is DockPanel line)
                    {
                        foreach (UIElement child in line.Children)
                        {
                            if (child is TextBlock text && text.Text != rows[i]) text.Text = rows[i];
                            if (child is Button activate) activate.IsEnabled = flightPlans[i].Active == null;
                        }
                    }
                }
                return;
            }
            FlightPlan? keep = routeFpl ?? SelectedFpl;
            fplSync = true;
            try
            {
                fplList.Items.Clear();
                for (int i = 0; i < flightPlans.Count; i++)
                {
                    FlightPlan plan = flightPlans[i];
                    DockPanel line = new();
                    Button activate = new() { Content = "Activate", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 1, 0, 1), IsEnabled = plan.Active == null };
                    activate.ToolTip = "The aircraft starts now on the first point and flies the route.";
                    activate.Click += (s, e) => Activate(plan);
                    DockPanel.SetDock(activate, Dock.Right);
                    line.Children.Add(activate);
                    line.Children.Add(new TextBlock { Text = rows[i], FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    ListBoxItem item = new() { Content = line, Tag = plan };
                    fplList.Items.Add(item);
                    if (ReferenceEquals(plan, keep)) fplList.SelectedItem = item;
                }
            }
            finally
            {
                fplSync = false;
            }
        }
    }
}
