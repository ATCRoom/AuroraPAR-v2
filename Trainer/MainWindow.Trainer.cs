using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: its parts in the main window. The surveillance radar (RDR) is shown in place of the PAR
    /// (one monitor: R or the RDR / PAR keys switch) or in its own window (two monitors). The bodies of the partial
    /// methods declared in MainWindow.xaml.cs are here, so AuroraPAR v2 (which does not build this folder) is not changed.
    /// </summary>
    public partial class MainWindow
    {
        private RadarPanel? radarPanel;
        private RadarWindow? radarWindow;
        private Button? rdrButton;
        private Button? parButton;
        /// <summary>One window: RDR is shown in place of the PAR.</summary>
        private bool radarInMain;
        /// <summary>The maps of the Maps folder (and the files that could not be read).</summary>
        private (List<LoadedMap> Maps, List<string> Errors) maps = (new List<LoadedMap>(), new List<string>());
        private MapsWindow? mapsWindow;
        /// <summary>QNH typed by hand, shown only while Aurora is not connected.</summary>
        private StackPanel? qnhPanel;
        private TextBox? qnhBox;
        /// <summary>The key "Instructor+" (beside the Instructor key) and the plan view inside the Instructor+ window.</summary>
        private Button? plusButton;
        private PlanView? instructorView;
        private MiniParView? instructorPar;

        partial void TrainerStart()
        {
            TrainerSettings trainer = settings.Trainer;
            radarPanel = new RadarPanel();
            radarPanel.View.Range = trainer.RadarRange;
            radarPanel.View.RangeStepRequested += StepRadarRange;
            // CENTRE (saved), and a click on a track selects it for TRAFFIC (again: no selection).
            radarPanel.View.Centre = TrainerSettings.ParseCentre(trainer.RadarCentre);
            radarPanel.View.CentreChanged += mode => { settings.Trainer.RadarCentre = mode.ToString(); SettingsStore.Save(settings); };
            radarPanel.View.AircraftClicked += callsign =>
            {
                if (radarPanel == null) return;
                radarPanel.View.SelectedCallsign = radarPanel.View.SelectedCallsign == callsign ? null : callsign;
                radarPanel.View.InvalidateVisual();
            };
            radarPanel.PlaceClicked += MoveRadar;
            radarPanel.MapsClicked += OpenMapsWindow;
            LoadMaps();
            BuildQnhBox();
            if (trainer.ManualQnh is int manual && qnh == 0) qnh = manual;
            rdrButton = new Button { Content = "RDR", Focusable = false };
            parButton = new Button { Content = "PAR", Focusable = false };
            rdrButton.Click += (s, e) => ShowView(radar: true);
            parButton.Click += (s, e) => ShowView(radar: false);
            // First row of the system keys.
            if (ModeButton.Parent is Panel keys)
            {
                keys.Children.Insert(0, parButton);
                keys.Children.Insert(0, rdrButton);
            }
            plusButton = new Button { Content = "Instructor+ (⇧I)", Focusable = false };
            plusButton.Click += (s, e) => OpenInstructor();
            if (BasicButton.Parent is Panel basicRow) basicRow.Children.Insert(basicRow.Children.IndexOf(BasicButton) + 1, plusButton);
            ApplyInstructorButtons();
            TrainerControlStyles(viewOptions.Analog);
            if (trainer.RadarSeparateWindow)
            {
                Loaded += (s, e) => OpenRadarWindow(activate: false);
            }
            else
            {
                AttachRadarToMain();
                SetRadarInMain(trainer.RadarShown);
            }
        }

        partial void TrainerRedraw(List<Aircraft> aircrafts)
        {
            if (radarPanel == null) return;
            // The history dots are kept also while the RDR is hidden.
            radarPanel.View.Update(aircrafts, DateTime.UtcNow, viewOptions.HistoryEnabled, viewOptions.HistoryDots, viewOptions.HistoryInterval);
            if (radarWindow != null || radarInMain) RefreshRadar();
            if (instructorView != null)
            {
                instructorView.Update(aircrafts, DateTime.UtcNow, viewOptions.HistoryEnabled, viewOptions.HistoryDots, viewOptions.HistoryInterval);
                instructorPar?.Update(aircrafts, DateTime.UtcNow);
                FeedInstructorView();
            }
            // QNH by hand only without Aurora (with Aurora the METAR gives it).
            if (qnhPanel != null)
            {
                Visibility shown = aurora.Connected ? Visibility.Collapsed : Visibility.Visible;
                if (qnhPanel.Visibility != shown)
                {
                    qnhPanel.Visibility = shown;
                    if (shown == Visibility.Visible) ShowManualQnh();
                }
            }
        }

        partial void TrainerKey(KeyEventArgs e, ModifierKeys modifiers)
        {
            if (radarPanel == null || modifiers != 0) return;
            if (e.Key == Key.R)
            {
                if (settings.Trainer.RadarSeparateWindow) OpenRadarWindow(activate: true);
                else SetRadarInMain(!radarInMain);
                e.Handled = true;
            }
            else if (radarInMain && e.Key is Key.PageUp or Key.PageDown)
            {
                StepRadarRange(e.Key == Key.PageUp ? 1 : -1);
                e.Handled = true;
            }
        }

        partial void TrainerClosing()
        {
            mapsWindow?.Close();
            if (radarWindow != null)
            {
                radarWindow.Close();
            }
        }

        partial void TrainerControlStyles(bool analog)
        {
            if (plusButton != null)
            {
                plusButton.ToolTip = "Instructor+: the advanced instructor, with the plan view of the radar (key Shift+I).";
                if (analog)
                {
                    StyleAsKey(plusButton, "INSTR+");
                    plusButton.Content = "INSTR+";
                }
                else
                {
                    plusButton.ClearValue(StyleProperty);
                    plusButton.ClearValue(FontSizeProperty);
                    plusButton.Content = "Instructor+ (⇧I)";
                    plusButton.Width = 98;
                    plusButton.Height = 26;
                    plusButton.Margin = new Thickness(0, 4, 0, 0);
                }
                UpdatePlusButton();
            }
            if (rdrButton == null || parButton == null) return;
            foreach ((Button button, string text, string tip) in new[]
            {
                (rdrButton!, "RDR", "Surveillance radar: the plan view (key R switches between RDR and PAR)."),
                (parButton!, "PAR", "Precision approach radar (key R switches between RDR and PAR).")
            })
            {
                button.ToolTip = tip;
                button.Content = text;
                if (analog)
                {
                    StyleAsKey(button, text);
                }
                else
                {
                    button.ClearValue(StyleProperty);
                    button.ClearValue(FontSizeProperty);
                    button.Width = 48;
                    button.Height = 26;
                    button.Margin = new Thickness(button == parButton ? 2 : 0, 4, 0, 0);
                }
            }
            UpdateViewButtons();
        }

        /// <summary>Reads the Maps folder again and gives the maps to the RDR.</summary>
        private (List<LoadedMap> Maps, List<string> Errors) LoadMaps()
        {
            maps = MapLibrary.Load();
            ApplyMapLayers();
            return maps;
        }

        /// <summary>The airport (ICAO) whose maps are on the RDR.</summary>
        private string mapAirport = "";

        private static SolidColorBrush FrozenBrush(Color colour)
        {
            SolidColorBrush brush = new(colour);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// The maps shown on the RDR: for the airport in use, each category with the choices made in the Maps window (else its
        /// defaults); the maps of the old kind (no airport) as before.
        /// </summary>
        private void ApplyMapLayers()
        {
            if (radarPanel == null) return;
            string icao = (runway?.ICAO ?? "").Trim().ToUpperInvariant();
            mapAirport = icao;
            List<MapLayer> layers = [];
            HashSet<string> airports = new(StringComparer.OrdinalIgnoreCase);
            foreach (LoadedMap map in maps.Maps)
            {
                if (map.Map.Airport.Length == 0)
                {
                    MapDisplaySettings display = settings.Trainer.MapDisplay(map.FileName);
                    if (!display.Visible) continue;
                    Color line = ColorText.Parse(display.LineColour ?? map.Map.LineColour, Color.FromRgb(0x2F, 0x6F, 0x86));
                    Color label = ColorText.Parse(display.LabelColour ?? map.Map.LabelColour, line);
                    layers.Add(new MapLayer(map.Map, FrozenBrush(line), FrozenBrush(label)));
                    continue;
                }
                if (icao.Length == 0 || !string.Equals(map.Map.Airport, icao, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (MapCategoryInfo category in MapCategory.All)
                {
                    MapFile part = map.Map.OfCategory(category.Id);
                    if (part.Lines.Count == 0 && part.Labels.Count == 0 && part.Points.Count == 0) continue;
                    CategoryDisplaySettings display = settings.Trainer.CategoryDisplay(icao, category.Id);
                    bool shapes = display.Shapes ?? category.ShapesOn;
                    bool texts = category.HasLabels && (display.Labels ?? category.LabelsOn);
                    if (!shapes && !texts) continue;
                    Color line = ColorText.Parse(display.ShapeColour ?? category.DefaultColour, Color.FromRgb(0x2F, 0x6F, 0x86));
                    Color label = ColorText.Parse(display.LabelColour ?? display.ShapeColour ?? category.DefaultColour, line);
                    MapSymbol? symbol = Enum.TryParse(display.Symbol, ignoreCase: true, out MapSymbol chosen) && Enum.IsDefined(chosen) ? chosen : null;
                    CustomSymbol? custom = symbol == null ? CustomSymbols.Find(display.Symbol) : null;
                    layers.Add(new MapLayer(part, FrozenBrush(line), FrozenBrush(label), shapes, texts, symbol, custom));
                    if (category.Id == MapCategory.Airports) foreach (MapPoint point in part.Points) airports.Add(point.Name);
                }
            }
            radarPanel.View.Layers = layers;
            radarPanel.View.MapAirports = airports;
            RefreshRadar();
            FeedInstructorView();
        }

        private void OpenMapsWindow()
        {
            if (mapsWindow != null)
            {
                mapsWindow.Activate();
                return;
            }
            MapsWindow window = new(settings.Trainer, () => maps, LoadMaps, () => mapAirport, () =>
            {
                ApplyMapLayers();
                SettingsStore.Save(settings);
            })
            {
                Owner = radarWindow != null ? radarWindow : this
            };
            window.Closed += (s, e) =>
            {
                if (mapsWindow == window) mapsWindow = null;
                SettingsStore.Save(settings);
            };
            mapsWindow = window;
            window.Show();
        }

        /// <summary>The QNH box at the top of the keys on the right, for lessons without Aurora.</summary>
        private void BuildQnhBox()
        {
            qnhBox = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
            qnhPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed,
                ToolTip = "Without Aurora: the QNH typed by hand, in hPa (or inHg, e.g. 29.92). Enter to apply. Hidden while Aurora is connected (the METAR gives it)."
            };
            qnhPanel.Children.Add(new TextBlock { Text = "QNH (no Aurora)", Margin = new Thickness(2, 0, 0, 2) });
            qnhPanel.Children.Add(qnhBox);
            SelectorStack.Children.Insert(0, qnhPanel);
            qnhBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyManualQnh();
                    FocusHelper.ReleaseKeyboard(this);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    ShowManualQnh();
                    FocusHelper.ReleaseKeyboard(this);
                    e.Handled = true;
                }
            };
            qnhBox.LostKeyboardFocus += (s, e) => ApplyManualQnh();
        }

        private void ShowManualQnh()
        {
            if (qnhBox == null) return;
            qnhBox.ClearValue(BackgroundProperty);
            qnhBox.Text = qnh > 0 ? qnh.ToString(CultureInfo.InvariantCulture) : "";
        }

        /// <summary>The QNH typed: hPa (850–1090), or inHg (a value below 40, e.g. 29.92) turned into hPa.</summary>
        private void ApplyManualQnh()
        {
            if (qnhBox == null || aurora.Connected) return;
            string text = qnhBox.Text.Trim().Replace(',', '.');
            if (text.Length == 0) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                qnhBox.Background = new SolidColorBrush(Color.FromRgb(255, 215, 215));
                return;
            }
            int hpa = (int)Math.Round(value < 40 ? value * 33.8639 : value);
            if (hpa < 850 || hpa > 1090)
            {
                qnhBox.Background = new SolidColorBrush(Color.FromRgb(255, 215, 215));
                return;
            }
            SetManualQnh(hpa);
        }

        /// <summary>The QNH by hand (without Aurora): from the box, or from the Instructor+ (typed, or a METAR from the internet).</summary>
        private void SetManualQnh(int hpa)
        {
            if (aurora.Connected || hpa < 850 || hpa > 1090) return;
            qnh = hpa;
            settings.Trainer.ManualQnh = hpa;
            SettingsStore.Save(settings);
            ShowManualQnh();
            Redraw();
        }

        /// <summary>RDR or PAR key: one window, that view; two windows, that window to the front.</summary>
        private void ShowView(bool radar)
        {
            if (settings.Trainer.RadarSeparateWindow)
            {
                if (radar) OpenRadarWindow(activate: true);
                else BringMainToFront();
            }
            else
            {
                SetRadarInMain(radar);
            }
        }

        /// <summary>One window: RDR in place of the PAR (true), or the PAR back.</summary>
        private void SetRadarInMain(bool show)
        {
            if (radarPanel == null) return;
            radarInMain = show;
            settings.Trainer.RadarShown = show;
            radarPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // Hidden, not collapsed: the PAR views keep their size (no redraw of everything at every switch).
            ScopeArea.Visibility = show ? Visibility.Hidden : Visibility.Visible;
            BezelLayer.Visibility = show ? Visibility.Hidden : Visibility.Visible;
            if (show) RefreshRadar();
            UpdateViewButtons();
        }

        private void AttachRadarToMain()
        {
            if (radarPanel == null || radarPanel.Parent == ScopeHost) return;
            radarPanel.InWindow = false;
            ScopeHost.Children.Add(radarPanel);
            Panel.SetZIndex(radarPanel, 100);
        }

        private void DetachRadarFromMain()
        {
            if (radarPanel == null || radarPanel.Parent != ScopeHost) return;
            ScopeHost.Children.Remove(radarPanel);
            radarInMain = false;
            ScopeArea.Visibility = Visibility.Visible;
            BezelLayer.Visibility = Visibility.Visible;
        }

        /// <summary>Opens the RDR window (or brings it to the front).</summary>
        private void OpenRadarWindow(bool activate)
        {
            if (radarPanel == null) return;
            if (radarWindow != null)
            {
                if (radarWindow.WindowState == WindowState.Minimized) radarWindow.WindowState = WindowState.Normal;
                radarWindow.Activate();
                return;
            }
            DetachRadarFromMain();
            RadarWindow window = new(radarPanel, settings.Trainer.RadarWindow, Icon) { ShowActivated = activate };
            window.SwitchRequested += BringMainToFront;
            window.RangeStepRequested += StepRadarRange;
            window.Closed += (s, e) =>
            {
                if (window.LastPlacement != null) settings.Trainer.RadarWindow = window.LastPlacement;
                if (radarWindow == window) radarWindow = null;
                UpdateViewButtons();
            };
            radarWindow = window;
            window.Show();
            RefreshRadar();
            UpdateViewButtons();
        }

        /// <summary>The button on the RDR: into its own window, or back into the main window.</summary>
        private void MoveRadar()
        {
            if (settings.Trainer.RadarSeparateWindow)
            {
                settings.Trainer.RadarSeparateWindow = false;
                radarWindow?.Close();
                AttachRadarToMain();
                SetRadarInMain(true);
                BringMainToFront();
            }
            else
            {
                settings.Trainer.RadarSeparateWindow = true;
                OpenRadarWindow(activate: true);
            }
            SettingsStore.Save(settings);
        }

        /// <summary>The main window to the front (also when it was minimized).</summary>
        /// <summary>The Instructor buttons chosen in the Settings window: a hidden button also switches off its key.</summary>
        private void ApplyInstructorButtons()
        {
            BasicButton.Visibility = settings.Trainer.ShowBasicInstructor ? Visibility.Visible : Visibility.Collapsed;
            if (plusButton != null) plusButton.Visibility = settings.Trainer.ShowPlusInstructor ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Analog key INSTR+ lit while the Instructor+ window is open.</summary>
        private void UpdatePlusButton()
        {
            if (plusButton != null) plusButton.Tag = viewOptions.Analog ? (instructorWindow != null ? "Lit" : "Unlit") : null;
        }

        partial void TrainerSettingsClosed() => ApplyInstructorButtons();

        partial void TrainerInstructorOpened(InstructorWindow window)
        {
            instructorView = new PlanView { Range = settings.Trainer.RadarRange, Centre = TrainerSettings.ParseCentre(settings.Trainer.InstructorCentre) };
            instructorView.CentreChanged += mode => { settings.Trainer.InstructorCentre = mode.ToString(); SettingsStore.Save(settings); };
            instructorView.RangeStepRequested += StepInstructorRange;
            instructorPar = new MiniParView();
            window.AttachPlan(instructorView, instructorPar);
            // The QNH of the Instructor+ is the one of the main window (by hand without Aurora).
            window.AttachWeather(() => aurora.Connected, () => qnh, SetManualQnh);
            // Aircraft removed or started again: their old tracks and history dots go from every view.
            window.AttachForget(ForgetTracks);
            FeedInstructorView();
            UpdatePlusButton();
        }

        /// <summary>
        /// The tracks and history dots of a simulated aircraft (null: of all of them) forgotten on the PAR, the RDR and the
        /// views of the Instructor+: it was removed, or starts again under the same callsign.
        /// </summary>
        private void ForgetTracks(string? callsign)
        {
            instructor.Forget(callsign);
            if (callsign == null)
            {
                // Only the simulated ones: real traffic keeps its tracks.
                foreach (string sim in lastAircrafts.Where(a => a.IsSim).Select(a => a.Callsign).ToList()) ForgetTracks(sim);
                return;
            }
            profileView.ForgetTrack(callsign);
            horizontalView.ForgetTrack(callsign);
            radarPanel?.View.ForgetHistory(callsign);
            instructorView?.ForgetHistory(callsign);
            instructorPar?.ForgetHistory(callsign);
        }

        partial void TrainerInstructorClosed()
        {
            instructorView = null;
            instructorPar = null;
            UpdatePlusButton();
        }

        /// <summary>The range of the plan view of the Instructor+ (the mouse wheel over it), apart from the one of the RDR.</summary>
        private void StepInstructorRange(int step)
        {
            if (instructorView == null) return;
            double[] ranges = TrainerSettings.RadarRanges;
            int index = Array.IndexOf(ranges, TrainerSettings.NearestRange(instructorView.Range));
            instructorView.Range = ranges[Math.Clamp(index + step, 0, ranges.Length - 1)];
            instructorView.InvalidateVisual();
        }

        /// <summary>Gives the plan view of the Instructor+ the runway, the pressure and the maps of the RDR.</summary>
        private void FeedInstructorView()
        {
            if (instructorView == null || radarPanel == null) return;
            // The maps of the airport in use, also while the RDR is not shown (the student on the PAR): ApplyMapLayers
            // sets mapAirport and comes back here.
            if ((runway?.ICAO ?? "").Trim().ToUpperInvariant() != mapAirport)
            {
                ApplyMapLayers();
                mapsWindow?.Refresh();
                return;
            }
            Profile profile = settings.Active;
            bool qfe = profile.PressureReference == PressureReference.QFE;
            string pressure = qnh == 0 ? "----" : Pressure.Format(qfe ? Pressure.QfeFromQnh(qnh, runway.Elevation) : qnh, profile.PressureUnit);
            instructorView.Runway = runway;
            if (instructorPar != null)
            {
                // The student's PAR: the same runway and range.
                instructorPar.Runway = runway;
                instructorPar.RangeNM = runway.Distance;
            }
            instructorView.Runways = runways;
            instructorView.ShowLabels = viewOptions.ShowLabels;
            instructorView.InfoText = $"RWY {runway.DisplayName}   {(qfe ? "QFE" : "QNH")} {pressure}";
            instructorView.SignText = instructor.Count > 0 ? "SIM TRAFFIC" : "";
            instructorView.Layers = radarPanel.View.Layers;
            instructorView.MapAirports = radarPanel.View.MapAirports;
            // BEAM: where the student's PAR sees the aircraft, from above (it follows the antenna's tilt).
            instructorView.ScanSector = PlanSector(radar.ScanLeftEdge, radar.ScanRightEdge);
            instructorView.BeamSector = PlanSector(radar.AzimuthLeftEdge, radar.AzimuthRightEdge);
            instructorView.InvalidateVisual();
        }

        /// <summary>
        /// A sector of the PAR seen from above: from the antenna (middle of the runway, with its offset) to the end of the
        /// range, between two azimuth angles (signed, positive right as seen by the pilot), as latitude and longitude.
        /// </summary>
        private List<(double Latitude, double Longitude)> PlanSector(double leftAngle, double rightAngle)
        {
            double radius = Earth.Radius(runway);
            // Along the approach from the threshold (NM, negative over the runway), to the pilot's right (NM).
            (double Latitude, double Longitude) At(double along, double right)
            {
                (double latitude, double longitude) = Earth.Destination(runway.Latitude, runway.Longitude, along >= 0 ? runway.Heading + 180 : runway.Heading, Math.Abs(along), radius);
                return Earth.Destination(latitude, longitude, right >= 0 ? runway.Heading + 90 : runway.Heading - 90, Math.Abs(right), radius);
            }
            double antennaAlong = Radar.AntennaFromRunwayEnd(runway) - runway.LengthNM;
            double end = runway.Distance;
            double fromAntenna = end - antennaAlong;
            if (fromAntenna <= 0) return [];
            double offset = radar.AntennaOffsetNM;
            return
            [
                At(antennaAlong, offset),
                At(end, offset + Radar.ScanOffset(fromAntenna, leftAngle)),
                At(end, offset + Radar.ScanOffset(fromAntenna, rightAngle))
            ];
        }

        private void BringMainToFront()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void StepRadarRange(int step)
        {
            if (radarPanel == null) return;
            double[] ranges = TrainerSettings.RadarRanges;
            int index = Array.IndexOf(ranges, TrainerSettings.NearestRange(radarPanel.View.Range));
            double range = ranges[Math.Clamp(index + step, 0, ranges.Length - 1)];
            radarPanel.View.Range = range;
            settings.Trainer.RadarRange = range;
            RefreshRadar();
        }

        /// <summary>Redraws the RDR with the runway, the pressure and the last traffic.</summary>
        private void RefreshRadar()
        {
            if (radarPanel == null) return;
            string icao = (runway?.ICAO ?? "").Trim().ToUpperInvariant();
            if (icao != mapAirport)
            {
                // Another airport: its maps (ApplyMapLayers sets mapAirport first, so this does not repeat).
                ApplyMapLayers();
                mapsWindow?.Refresh();
                return;
            }
            PlanView view = radarPanel.View;
            Profile profile = settings.Active;
            bool qfe = profile.PressureReference == PressureReference.QFE;
            string pressure = qnh == 0 ? "----" : Pressure.Format(qfe ? Pressure.QfeFromQnh(qnh, runway.Elevation) : qnh, profile.PressureUnit);
            view.Runway = runway;
            view.Runways = runways;
            view.ShowLabels = viewOptions.ShowLabels;
            view.InfoText = $"RWY {runway.DisplayName}   {(qfe ? "QFE" : "QNH")} {pressure}";
            // In the main window the sign of the main window is over the RDR too; its own window needs its own.
            bool test = testTraffic.Count > 0;
            bool sim = instructor.Count > 0 && aurora.Connected;
            view.SignText = radarWindow == null || !(test || sim) ? "" : test && sim ? "TEST + SIM TRAFFIC" : sim ? "SIM TRAFFIC" : "TEST TRAFFIC";
            view.InvalidateVisual();
        }

        /// <summary>The RDR and PAR keys show which view is in use (two windows: RDR lit while its window is open).</summary>
        private void UpdateViewButtons()
        {
            if (rdrButton == null || parButton == null) return;
            bool separate = settings.Trainer.RadarSeparateWindow;
            bool rdrOn = separate ? radarWindow != null : radarInMain;
            bool parOn = separate || !radarInMain;
            bool analog = viewOptions.Analog;
            foreach ((Button button, bool on) in new[] { (rdrButton!, rdrOn), (parButton!, parOn) })
            {
                if (analog)
                {
                    button.Tag = on ? "Lit" : "Unlit";
                    button.ClearValue(BackgroundProperty);
                    button.ClearValue(FontWeightProperty);
                }
                else
                {
                    button.Tag = null;
                    if (on)
                    {
                        button.Background = new SolidColorBrush(Color.FromRgb(0xB8, 0xD6, 0xEE));
                        button.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        button.ClearValue(BackgroundProperty);
                        button.ClearValue(FontWeightProperty);
                    }
                }
            }
        }
    }
}
