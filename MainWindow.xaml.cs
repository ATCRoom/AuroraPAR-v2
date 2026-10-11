using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// How often the METAR (QNH) is requested again. It changes rarely, so there is no need to ask every refresh.
        /// </summary>
        private static readonly TimeSpan QnhRefreshInterval = TimeSpan.FromSeconds(60);
        private int qnh = 0;
        private DateTime lastQnhUpdate = DateTime.MinValue;
        private string lastQnhIcao = "";
        /// <summary>
        /// Set when the QNH must be requested again as soon as possible (e.g. right after (re)connecting).
        /// </summary>
        private volatile bool qnhRefreshNeeded = true;
        /// <summary>
        /// Delay between two connection attempts while Aurora is not reachable.
        /// </summary>
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
        /// <summary>
        /// Aircraft received at the last refresh, so the screen can be redrawn immediately
        /// (range or runway change, window resize) without waiting for Aurora.
        /// </summary>
        private volatile List<Aircraft> lastAircrafts = [];
        private readonly System.Timers.Timer timer;
        private readonly ProfileView profileView;
        private readonly HorizontalView horizontalView;
        /// <summary>Clock of the antenna scan effect.</summary>
        private readonly System.Diagnostics.Stopwatch sweepClock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>Knobs of the analog console.</summary>
        private const string KnobHelp = "Click the right half: one step clockwise; left half: counter-clockwise (the pointer shows the direction). Mouse wheel. Centre: drag up/down";
        private readonly Knob rangeKnob = new() { Title = "RANGE NM", ToolTip = $"Range. {KnobHelp}." };
        private readonly Knob elevationKnob = new() { Title = "EL TILT", ToolTip = $"Antenna elevation tilt. {KnobHelp}; double click on the centre: neutral." };
        private readonly Knob azimuthKnob = new() { Title = "AZ TILT", ToolTip = $"Antenna azimuth tilt. {KnobHelp}; double click on the centre: neutral." };
        private readonly Knob brightnessKnob = new() { Title = "BRT", Positions = Profile.MaxBrightness / Profile.BrightnessStep, ToolTip = $"Brightness of the scope, 10% to 150% (above 100% for dim monitors). {KnobHelp}; double click on the centre: 100%." };
        private readonly Knob clutterKnob = new() { Title = "CLUTTER", Positions = 11, ToolTip = "Rain clutter filter: reduces the clutter, but dims the tracks too. Turn clockwise for more filtering (Reset: off)." };
        /// <summary>Rain clutter forced in the Test traffic window (null: from the METAR) and CLUTTER filter step 0 to 10 (session values).</summary>
        private RainLevel? rainOverride;
        private int clutterFilterStep;
        private string? clutterMetarText;
        private RainLevel clutterMetarLevel;
        private readonly Knob dhKnob = new() { Title = "DH", ToolTip = $"Decision height, 10 ft per step. {KnobHelp}; double click on the centre: runway value." };
        /// <summary>Analog: airport entry (readout window).</summary>
        private readonly AptEntry aptEntry = new();
        /// <summary>Analog: keys of the runways of the airport in use, and of the approaches (glide paths) of the runway.</summary>
        private readonly List<(Runway Runway, Button Button)> runwayButtons = [];
        private readonly List<(Runway Approach, Button Button)> glideButtons = [];
        /// <summary>Analog mode: readouts and lamps next to the scope.</summary>
        private readonly ConsolePanel consolePanel = new();
        private static readonly Brush PanelTextBrush = CreateFrozenBrush(Color.FromRgb(0xD8, 0xD8, 0xD0));
        private readonly Brush glassBrush = ScopeBezel.CreateGlassBrush();
        private readonly Aurora aurora;
        /// <summary>Ranges of the range box (those ticked in the active profile, see <see cref="ApplyRanges"/>).</summary>
        private Distance[] distances = Ranges.Values.Select(v => (Distance)v).ToArray();
        /// <summary>
        /// Runway file, next to the program (not in the current directory, which depends on how the program is started).
        /// </summary>
        private readonly string dataPath = Path.Combine(AppContext.BaseDirectory, "runways.par");
        /// <summary>All the lines of the runway file (one per approach).</summary>
        private Runway[] runways = [];
        /// <summary>
        /// Runways of the drop-down: one per airport and designator. Lines with the same airport and designator and
        /// a different glide path are approaches of the same runway, chosen with the GP selector.
        /// </summary>
        private Runway[] runwayList = [];
        /// <summary>Published approaches of the current runway, by glide path angle.</summary>
        private Runway[] approaches = [];
        /// <summary>Published approach in use (<see cref="runway"/> is it, or its unpublished copy with another angle).</summary>
        private Runway? selectedApproach;
        /// <summary>True while the GP selector is filled by the program.</summary>
        private bool updatingGlidePath;
        /// <summary>
        /// True while the runway list is reloaded after editing, so the current range is kept.
        /// </summary>
        private bool reloadingRunways;
        private volatile bool Open = true;
        private readonly AppSettings settings;
        /// <summary>
        /// True while the last session's runway is being restored at start.
        /// </summary>
        private bool restoringSession;
        /// <summary>
        /// Information area (runway, QNH, connection status), created once and updated at every refresh.
        /// </summary>
        private readonly StackPanel infoPanel = new();
        private readonly TextBlock infoText = new() { FontSize = 14, Foreground = Brushes.White };
        /// <summary>Glide path, with the "unpublished approach" warning.</summary>
        private readonly TextBlock glidePathText = new() { FontSize = 14, Foreground = Brushes.White, Background = Brushes.Transparent };
        /// <summary>Missed approach point distance, on its own line for its tooltip.</summary>
        private readonly TextBlock missedApproachText = new()
        {
            FontSize = 14,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            ToolTip = "MAPt DIST - missed approach point: distance in NM from the touchdown point where the glide path reaches the decision height in use (DH / OCH).\nCompare it with the approach chart (MAPt / RPI DIST) to check runways.par; it is the distance for \"approach terminating at ...\".\nThe DH does not change with the glide path angle, the MAPt does: with an unpublished angle this is the new MAPt."
        };
        /// <summary>Final course, on its own line for its tooltip.</summary>
        private readonly TextBlock courseText = new()
        {
            FontSize = 14,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            ToolTip = "Final course (magnetic). Calculated, not the published value: runway true heading from runways.par corrected with the magnetic variation (of the runway, or the default in Settings), rounded to the degree. Check it against the approach chart."
        };
        private readonly TextBlock infoText2 = new() { FontSize = 14, Foreground = Brushes.White };
        /// <summary>
        /// Antenna tilt reminder, shown only when the antenna is not in neutral position.
        /// </summary>
        private readonly TextBlock tiltText = new() { FontSize = 14, Foreground = Brushes.Orange, FontWeight = FontWeights.Bold };
        /// <summary>
        /// Approach and scan limits and antenna tilt, shared by both views.
        /// </summary>
        private readonly Radar radar = new();
        private readonly ViewOptions viewOptions = new();
        private readonly TrackFilter trackFilter = new();
        /// <summary>Virtual aircraft for tests (key T), merged with the traffic of Aurora.</summary>
        private readonly TestTraffic testTraffic = new();
        private TestTrafficWindow? testTrafficWindow;
        private readonly TextBlock statusText = new() { FontSize = 14 };
        private readonly TextBlock dataText = new() { FontSize = 14 };
        /// <summary>
        /// Above this interval between position updates, Aurora's traffic refresh rate is too slow for a PAR.
        /// </summary>
        private const double MaxGoodDataInterval = 1.5;
        private readonly RefreshRateMonitor refreshMonitor = new();
        private readonly VerticalSpeedEstimator verticalSpeed = new();
        /// <summary>
        /// Measured interval between position updates from Aurora (s), NaN when unknown. Written by the refresh timer.
        /// </summary>
        private double dataInterval = double.NaN;
        private Runway runway = new()
        {
            ICAO = "EDDF",
            Designator = "TEST",
            Elevation = 364,
            Heading = 70,
            Latitude = 50.02766757167606,
            Longitude = 8.534360261721835,
            LengthM = 1000,
            WidthM = 60,
            Distance = 10
        };
        public MainWindow()
        {
            InitializeComponent();
            settings = SettingsStore.Load();
            RestoreWindowPlacement();
            this.Loaded += MainWindow_Loaded;
            DistanceComboBox.ItemsSource = distances;
            DistanceComboBox.SelectedIndex = IndexOfDistance(runway.Distance);//10 nm
            DistanceComboBox.SelectionChanged += DistanceComboBox_SelectionChanged;
            profileView = new(Vertical, runway, radar, viewOptions);
            horizontalView = new(Horizontal, runway, radar, viewOptions);
            infoPanel.Children.Add(infoText);
            // One entry per runway: "LIPC 11" for the lines "LIPC 11 2.8" and "LIPC 11 2.5".
            RunwayComboBox.DisplayMemberPath = nameof(Runway.DisplayName);
            // ICAO box (modern display): like the APT window of the analog console, it sets the airport in use.
            IcaoFilterBox.TextChanged += (s, e) =>
            {
                if (!settingFilter) IcaoTyped(enter: false);
            };
            IcaoFilterBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    IcaoTyped(enter: true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    ShowAirportInUse();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            IcaoFilterBox.LostKeyboardFocus += (s, e) => ShowAirportInUse();
            infoPanel.Children.Add(ToolTips.KeepOpen(courseText));
            infoPanel.Children.Add(glidePathText);
            infoPanel.Children.Add(ToolTips.KeepOpen(missedApproachText));
            foreach (Knob knob in new[] { rangeKnob, elevationKnob, azimuthKnob, dhKnob, brightnessKnob, clutterKnob }) ToolTips.KeepOpen(knob);
            aptEntry.Submit = SelectAirport;
            AptHost.Child = aptEntry;
            BrightnessDownButton.Click += (s, e) => ChangeBrightness(-1);
            BrightnessUpButton.Click += (s, e) => ChangeBrightness(1);
            BrightnessPanel.MouseWheel += (s, e) =>
            {
                // Handled here, so the wheel over the control does not also change the range.
                ChangeBrightness(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
            infoPanel.Children.Add(infoText2);
            infoPanel.Children.Add(tiltText);
            infoPanel.Children.Add(statusText);
            infoPanel.Children.Add(dataText);
            Panel.SetZIndex(infoPanel, 20);
            Vertical.Children.Add(infoPanel);
            aurora = new();
            this.Closing += MainWindow_Closing;
            // AutoReset = false: the next refresh is started only when the previous one has finished,
            // so refreshes never overlap even when Aurora answers slowly.
            timer = new()
            {
                Interval = 100,
                AutoReset = false
            };
            timer.Elapsed += Timer_Elapsed;
            Vertical.SizeChanged += (s, e) => InvalidateViews();
            Horizontal.SizeChanged += (s, e) => InvalidateViews();
            SettingsButton.Click += SettingsButton_Click;
            TiltUpButton.Click += (s, e) => TiltAntenna(1, 0);
            TiltDownButton.Click += (s, e) => TiltAntenna(-1, 0);
            TiltLeftButton.Click += (s, e) => TiltAntenna(0, -AzimuthSign);
            TiltRightButton.Click += (s, e) => TiltAntenna(0, AzimuthSign);
            TiltNeutralButton.Click += (s, e) => NeutralAntenna();
            LabelsButton.Click += (s, e) => ToggleLabels();
            LinButton.Click += (s, e) => SetDistanceScale(log: false);
            LogButton.Click += (s, e) => SetDistanceScale(log: true);
            ModeButton.Click += (s, e) => ToggleDisplayMode();
            CoordinationButton.Click += (s, e) => OpenCoordination();
            TestButton.Click += (s, e) => OpenTestTraffic();
            BuildKnobs();
            ScopeHost.SizeChanged += (s, e) => LayoutDisplay();
            // Console panel at most about a quarter of the display (it is scaled to fit, see the XAML).
            DisplayArea.SizeChanged += (s, e) => ConsoleHost.MaxWidth = Math.Max(90, Math.Min(DisplayArea.ActualWidth * 0.26, 300));
            ConsoleViewbox.Child = consolePanel;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            // A click outside the text field (APT, DH, GP...) that has the cursor takes the keyboard back, so that the
            // keys P, A, T, L and the arrows work again (a click on a part of the window that takes no focus does not).
            PreviewMouseDown += (sender, e) =>
            {
                if (Keyboard.FocusedElement is not (TextBox or AptEntry) || Keyboard.FocusedElement is not DependencyObject focused) return;
                for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null;
                     d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                {
                    if (d == focused) return;
                }
                Keyboard.ClearFocus();
            };
            StartJoystick();
            DhUpButton.Click += (s, e) => SetDecisionHeight(runway.MDH + DecisionHeightStep);
            DhDownButton.Click += (s, e) => SetDecisionHeight(runway.MDH - DecisionHeightStep);
            DhTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) ApplyDecisionHeightText();
            };
            DhTextBox.LostFocus += (s, e) => ApplyDecisionHeightText();
            DhTextBox.Text = FormatHeight(runway.MDH);
            GlidePathTextBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyGlidePathText();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    UpdateGlidePathSelector();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            GlidePathTextBox.LostKeyboardFocus += (s, e) => ApplyGlidePathText();
            ApplyProfile();
            // Antenna scan effect: redrawn at every frame of the screen (graphic only, independent of the traffic refresh).
            CompositionTarget.Rendering += (s, e) => RenderSweep();
        }

        /// <summary>Coordination light panel (one window, opened on demand).</summary>
        private CoordinationWindow? coordinationWindow;
        /// <summary>Callsign this Aurora is connected with, checked every few seconds.</summary>
        private string? connectedCallsign;
        private DateTime lastCallsignCheck = DateTime.MinValue;
        private static readonly TimeSpan CallsignCheckInterval = TimeSpan.FromSeconds(10);

        private void OpenCoordination()
        {
            if (coordinationWindow == null)
            {
                coordinationWindow = new CoordinationWindow(settings.Coordination, () => SettingsStore.Save(settings), connectedCallsign);
                coordinationWindow.Closed += (s, e) =>
                {
                    coordinationWindow = null;
                    CoordinationButton.Tag = "Unlit";
                };
                coordinationWindow.Show();
                CoordinationButton.Tag = "Lit";
            }
            else
            {
                if (coordinationWindow.WindowState == WindowState.Minimized) coordinationWindow.WindowState = WindowState.Normal;
                coordinationWindow.Activate();
            }
        }

        /// <summary>CLUTTER filter step (0 to 10), shared by the knob and the Test traffic window.</summary>
        private void SetClutterFilter(int step)
        {
            clutterFilterStep = Math.Clamp(step, 0, 10);
            clutterKnob.Index = clutterFilterStep;
            testTrafficWindow?.SetRain(rainOverride is RainLevel level ? (int)level + 1 : 0, clutterFilterStep);
        }

        /// <summary>
        /// Analog scope rain clutter: strength from the precipitation of the METAR (or forced in the Test traffic
        /// window, also with the option off in the Settings), and the CLUTTER filter, set for both views.
        /// </summary>
        private void UpdateClutter(Profile profile)
        {
            bool on = viewOptions.Analog && (profile.RainClutter || rainOverride != null);
            double strength = 0;
            if (on)
            {
                string? text = aurora.LastMetar is MetarReport report && string.Equals(report.Icao, runway.ICAO, StringComparison.OrdinalIgnoreCase) ? report.Text : null;
                if (text != clutterMetarText)
                {
                    clutterMetarText = text;
                    clutterMetarLevel = RainClutter.FromMetar(text);
                }
                strength = RainClutter.Strength(rainOverride ?? clutterMetarLevel);
            }
            // Wind that moves the patches: the wind of the Test traffic (when set), otherwise the one of the METAR.
            double from = 0, knots = 0;
            if (strength > 0)
            {
                if (testTraffic.Count > 0 && testTraffic.WindSpeed > 0) { from = testTraffic.WindFrom; knots = testTraffic.WindSpeed; }
                else if (!RainClutter.TryWind(clutterMetarText, out from, out knots)) knots = 0;
            }
            double towards = (from + 180 - runway.Heading) * Math.PI / 180;
            viewOptions.ClutterWindAlongKt = knots * Math.Cos(towards);
            viewOptions.ClutterWindRightKt = knots * Math.Sin(towards);
            viewOptions.ClutterStrength = strength;
            viewOptions.ClutterFilter = strength > 0 ? clutterFilterStep / 10.0 : 0;
        }

        private void RenderSweep()
        {
            Profile profile = settings.Active;
            double t = sweepClock.Elapsed.TotalSeconds;
            // The analog scope always has its beam.
            bool enabled = profile.ScanEffect || viewOptions.Analog;
            UpdateClutter(profile);
            profileView.RenderSweep(enabled, t, profile.ScanEffectSpeed);
            horizontalView.RenderSweep(enabled, t, profile.ScanEffectSpeed);
            MoveTracks();
        }

        /// <summary>
        /// Modern display: the smoothed and the coasting tracks are estimated positions, moved on at every frame of
        /// the screen (not only when a poll of Aurora ends, which is irregular), so they move smoothly. Only the
        /// tracks are updated (static parts and the information area are not rebuilt).
        /// </summary>
        private void MoveTracks()
        {
            if (!Open || viewOptions.Analog || settings.Active.TrackSmoothing == TrackSmoothing.Off) return;
            List<Aircraft> aircrafts = lastAircrafts;
            if (aircrafts.Count == 0) return;
            try
            {
                trackFilter.Apply(aircrafts, DateTime.UtcNow, settings.Active.TrackSmoothing);
                profileView.MoveTracks(aircrafts);
                horizontalView.MoveTracks(aircrafts);
            }
            catch (Exception)
            {
                // Never let a frame crash the program: the next data refresh redraws everything.
            }
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Knobs of the analog console: range, antenna tilt (elevation, azimuth) and decision height.
        /// </summary>
        private void BuildKnobs()
        {
            rangeKnob.Positions = Ranges.Values.Length;
            rangeKnob.LabelFor = i => Ranges.Values[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            rangeKnob.Turned += steps =>
            {
                DistanceComboBox.SelectedIndex = Math.Clamp(DistanceComboBox.SelectedIndex + steps, 0, DistanceComboBox.Items.Count - 1);
                UpdateKnobs();
            };
            elevationKnob.Turned += steps => TiltAntenna(steps, 0);
            elevationKnob.Reset += NeutralElevation;
            azimuthKnob.Turned += steps => TiltAntenna(0, steps * AzimuthSign);
            azimuthKnob.Reset += NeutralAzimuth;
            dhKnob.Turned += steps => SetDecisionHeight(runway.MDH + steps * DecisionHeightStep);
            dhKnob.Reset += () => SetDecisionHeight(runway.DefaultMDH);
            brightnessKnob.LabelFor = i => i == 0 ? "MIN" : i == 100 / Profile.BrightnessStep - 1 ? "100" : i == Profile.MaxBrightness / Profile.BrightnessStep - 1 ? "MAX" : null;
            brightnessKnob.Turned += steps => ChangeBrightness(steps);
            brightnessKnob.Reset += () => SetBrightness(100);
            clutterKnob.LabelFor = i => i == 0 ? "OFF" : i == 10 ? "MAX" : null;
            clutterKnob.Turned += steps => SetClutterFilter(clutterFilterStep + steps);
            clutterKnob.Reset += () => SetClutterFilter(0);
            tiltStick.Moved += (elevation, azimuth) => TiltAntenna(elevation, azimuth * AzimuthSign);
            tiltStick.Centred += NeutralAntenna;
            ToolTips.KeepOpen(tiltStick);
        }

        private void NeutralElevation()
        {
            if (radar.NeutralElevation()) InvalidateViews();
            UpdateKnobs();
        }

        private void NeutralAzimuth()
        {
            if (radar.NeutralAzimuth()) InvalidateViews();
            UpdateKnobs();
        }

        /// <summary>Analog: the small 4-way joystick of the antenna tilt (instead of the two tilt knobs).</summary>
        private readonly TiltStick tiltStick = new()
        {
            Title = "TILT",
            ToolTip = "Antenna tilt: push the stick (drag, or click on a direction) up/down for the elevation, left/right for the azimuth; held, it repeats. Click on the centre: neutral."
        };
        /// <summary>Analog keys of the control groups, and when each one is lit.</summary>
        private readonly List<(Button Key, Func<bool>? Lit, bool Latching)> analogKeys = [];
        /// <summary>Choices the analog controls were built for.</summary>
        private string? analogControlsLayout;

        /// <summary>
        /// Analog console, right side: each control group as a knob or as keys (the tilt also as a small joystick),
        /// as chosen in the profile. Built again only when the choices change.
        /// </summary>
        private void BuildAnalogControls()
        {
            Profile profile = settings.Active;
            string ranges = string.Join(",", Ranges.Values);
            string layout = $"{profile.KeyStyle}|{profile.RangeControl}|{profile.RangeDefaultKey}|{ranges}|{profile.PreferredRange}|{profile.TiltControl}|{profile.DhControl}|{profile.ShowDhSelector}|{profile.BrightnessControl}|{profile.RainClutter}";
            if (layout == analogControlsLayout) return;
            analogControlsLayout = layout;
            KnobPanel.Children.Clear();
            // The range knob may be inside a frame (FIAR keys): free it before it is placed again.
            if (rangeKnob.Parent is Panel knobParent) knobParent.Children.Remove(rangeKnob);
            analogKeys.Clear();
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            switch (profile.RangeControl)
            {
                case AnalogRangeControl.RangeKeys:
                    AddKeyGroup("RANGE NM", Ranges.Values.Select((value, i) => (
                        value.ToString(invariant), $"Range {value.ToString(invariant)} NM",
                        (Action)(() => SetRangeIndex(i)), (Func<bool>?)(() => DistanceComboBox.SelectedIndex == i))).ToArray(), latching: true, scaleKeys: true);
                    break;
                case AnalogRangeControl.PanelKeys:
                    AddRangePanel(profile);
                    break;
                case AnalogRangeControl.StepKeys:
                    AddKeyGroup("RANGE NM",
                    [
                        ("<", "Range down (smaller) (key Page down).", () => SetRangeIndex(DistanceComboBox.SelectedIndex - 1), null),
                        // FIAR style: always PREF (the preferred range is set in Settings).
                        (FiarKeys ? "PREF" : profile.RangeDefaultKey, "Back to the preferred range (Settings → Range and scale) (key End).",
                            () => SetRangeIndex(Ranges.IndexOfClosest(settings.Active.PreferredRange)),
                            () => DistanceComboBox.SelectedIndex == Ranges.IndexOfClosest(settings.Active.PreferredRange)),
                        (">", "Range up (larger) (key Page up).", () => SetRangeIndex(DistanceComboBox.SelectedIndex + 1), null)
                    ], scaleKeys: true);
                    break;
                default:
                    if (FiarKeys)
                    {
                        // FIAR: the knob and the LIN | LOG key in one gold frame.
                        StackPanel knobAndScale = new();
                        knobAndScale.Children.Add(rangeKnob);
                        knobAndScale.Children.Add(SplitScaleKey());
                        KnobPanel.Children.Add(new Border
                        {
                            BorderBrush = PanelFrameBrush,
                            BorderThickness = new Thickness(1.5),
                            CornerRadius = new CornerRadius(2),
                            Padding = new Thickness(1, 2, 1, 2),
                            Margin = new Thickness(0, 4, 0, 6),
                            Child = knobAndScale
                        });
                    }
                    else
                    {
                        KnobPanel.Children.Add(rangeKnob);
                        AddKeyGroup("SCALE", [], scaleKeys: true);
                    }
                    break;
            }
            switch (profile.TiltControl)
            {
                case AnalogTiltControl.Keys:
                    AddKeyGroup("EL TILT",
                    [
                        ("UP", "Antenna elevation tilt up (key ↑).", () => TiltAntenna(1, 0), null),
                        ("0", "Elevation tilt neutral.", NeutralElevation, () => radar.TiltElevation == 0),
                        ("DN", "Antenna elevation tilt down (key ↓).", () => TiltAntenna(-1, 0), null)
                    ], repeat: true);
                    AddKeyGroup("AZ TILT",
                    [
                        ("L", "Antenna azimuth tilt left (key ←).", () => TiltAntenna(0, -AzimuthSign), null),
                        ("0", "Azimuth tilt neutral.", NeutralAzimuth, () => radar.TiltAzimuth == 0),
                        ("R", "Antenna azimuth tilt right (key →).", () => TiltAntenna(0, AzimuthSign), null)
                    ], repeat: true);
                    break;
                case AnalogTiltControl.Joystick:
                    KnobPanel.Children.Add(tiltStick);
                    break;
                default:
                    KnobPanel.Children.Add(elevationKnob);
                    KnobPanel.Children.Add(azimuthKnob);
                    break;
            }
            if (!profile.ShowDhSelector)
            {
            }
            else if (profile.DhControl == AnalogControl.Keys)
            {
                AddKeyGroup("DH",
                [
                    ("−", $"Decision height {DecisionHeightStep} ft lower (Shift+↓).", () => SetDecisionHeight(runway.MDH - DecisionHeightStep), null),
                    ("RWY", "Decision height of the runway (Shift+Home).", () => SetDecisionHeight(runway.DefaultMDH), () => runway.MDH == runway.DefaultMDH),
                    ("+", $"Decision height {DecisionHeightStep} ft higher (Shift+↑).", () => SetDecisionHeight(runway.MDH + DecisionHeightStep), null)
                ], repeat: true);
            }
            else
            {
                KnobPanel.Children.Add(dhKnob);
            }
            if (profile.BrightnessControl == AnalogControl.Keys)
            {
                AddKeyGroup("BRT",
                [
                    ("−", "Scope brightness down (Ctrl+↓).", () => ChangeBrightness(-1), null),
                    ("100", "Scope brightness 100% (Ctrl+Home).", () => SetBrightness(100), () => settings.Active.BrightnessAnalog == 100),
                    ("+", "Scope brightness up, above 100% for dim monitors (Ctrl+↑).", () => ChangeBrightness(1), null)
                ], repeat: true);
            }
            else
            {
                KnobPanel.Children.Add(brightnessKnob);
            }
            if (profile.RainClutter)
            {
                clutterKnob.Index = clutterFilterStep;
                KnobPanel.Children.Add(clutterKnob);
            }
            UpdateAnalogKeys();
        }

        /// <summary>Small console keys of the analog panel.</summary>
        private const double SmallKeyWidth = 28;
        private const double SmallKeyHeight = 32;

        /// <summary>A group of small keys, three per row, on a recessed plate with its engraved name.</summary>
        /// <param name="repeat">Keys that repeat while held (tilt, DH, BRT; not the range, which steps only on a deliberate
        /// press); the middle keys (0, RWY, 100: those with a lamp) never repeat.</param>
        /// <param name="scaleKeys">Also the LIN / LOG keys of the distance scale, at the bottom of the group (FIAR: one
        /// double key).</param>
        private void AddKeyGroup(string title, (string Text, string Tip, Action Action, Func<bool>? Lit)[] keys, bool latching = false, bool repeat = false, bool scaleKeys = false)
        {
            StackPanel group = new() { Margin = new Thickness(0, 4, 0, 6) };
            bool fiar = FiarKeys;
            TextBlock caption = new() { Text = title, Style = (Style)FindResource("EngravedLabel") };
            if (fiar) caption.Foreground = PanelEngravingBrush;
            group.Children.Add(caption);
            System.Windows.Controls.Primitives.UniformGrid grid = new() { Columns = 3 };
            foreach ((string text, string tip, Action action, Func<bool>? lit) in keys)
            {
                Button key = new() { Content = text, ToolTip = tip };
                StyleAsKey(key, text, SmallKeyWidth, SmallKeyHeight);
                key.Margin = new Thickness(1, 0, 1, 2);
                key.Click += (s, e) => action();
                if (repeat && lit == null) MakeRepeating(key, action);
                grid.Children.Add(key);
                analogKeys.Add((key, lit, latching));
            }
            FrameworkElement content = grid;
            if (scaleKeys)
            {
                StackPanel stack = new();
                if (keys.Length > 0) stack.Children.Add(grid);
                stack.Children.Add(fiar ? SplitScaleKey() : ConsoleScaleKeys());
                content = stack;
            }
            group.Children.Add(fiar
                // FIAR: keys in a thin gold frame, as on the panels of that console.
                ? new Border
                {
                    BorderBrush = PanelFrameBrush,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(1, 3, 1, 1),
                    Child = content
                }
                : new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x20, 0x1D)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x0D)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(0, 2, 0, 0),
                    Child = content
                });
            KnobPanel.Children.Add(group);
        }

        /// <summary>Console keys LIN and LOG of the distance scale (the one in use lit), two in a row.</summary>
        private System.Windows.Controls.Primitives.UniformGrid ConsoleScaleKeys()
        {
            System.Windows.Controls.Primitives.UniformGrid row = new() { Columns = 2, HorizontalAlignment = HorizontalAlignment.Center };
            foreach ((string text, bool log) in new[] { ("LIN", false), ("LOG", true) })
            {
                Button key = new() { Content = text, ToolTip = ScaleKeyTip(log) };
                StyleAsKey(key, text, 40, SmallKeyHeight);
                key.Margin = new Thickness(1, 0, 1, 2);
                key.Click += (s, e) => SetDistanceScale(log);
                row.Children.Add(key);
                analogKeys.Add((key, () => settings.Active.CompressedScale == log, true));
            }
            return row;
        }

        /// <summary>FIAR: the double key LIN | LOG of the distance scale (letters top to bottom, the half in use lit).</summary>
        private StackPanel SplitScaleKey()
        {
            StackPanel pair = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 2) };
            foreach ((string letters, bool log) in new[] { ("L\nI\nN", false), ("L\nO\nG", true) })
            {
                Button half = new()
                {
                    Content = new TextBlock
                    {
                        Text = letters,
                        TextAlignment = TextAlignment.Center,
                        FontSize = 12,
                        LineHeight = 13,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    ToolTip = ScaleKeyTip(log),
                    Style = (Style)FindResource("PanelKey"),
                    Width = 24,
                    Height = 58,
                    Margin = new Thickness(0)
                };
                half.Click += (s, e) => SetDistanceScale(log);
                pair.Children.Add(half);
                analogKeys.Add((half, () => settings.Active.CompressedScale == log, true));
            }
            return pair;
        }

        private static string ScaleKeyTip(bool log) => log
            ? "Log distance scale: the last miles larger, the far ones closer (how much: Settings → Units) (key C)."
            : "Linear distance scale (key C).";

        /// <summary>Range keys of the FIAR panel style.</summary>
        private const double PanelKeyWidth = 36;
        private const double PanelKeyHeight = 40;
        private static readonly Brush PanelFrameBrush = CreateFrozenBrush(Color.FromRgb(0xB8, 0xA0, 0x4A));
        private static readonly Brush PanelEngravingBrush = CreateFrozenBrush(Color.FromRgb(0xC9, 0xB4, 0x6A));

        /// <summary>
        /// Range keys as on the FIAR console: two columns of large square keys (number over NM) in a gold frame, the
        /// range in use bright, a dot on the preferred one; under them the key back to the preferred range.
        /// </summary>
        private void AddRangePanel(Profile profile)
        {
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            StackPanel group = new() { Margin = new Thickness(0, 4, 0, 6) };
            TextBlock title = new() { Text = "RANGE", Style = (Style)FindResource("EngravedLabel"), HorizontalAlignment = HorizontalAlignment.Center };
            title.Foreground = PanelEngravingBrush;
            group.Children.Add(title);
            System.Windows.Controls.Primitives.UniformGrid grid = new() { Columns = 2 };
            int preferred = Ranges.IndexOfClosest(profile.PreferredRange);
            for (int i = 0; i < Ranges.Values.Length; i++)
            {
                int index = i;
                string value = Ranges.Values[i].ToString(invariant);
                Grid face = new();
                StackPanel lines = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
                lines.Children.Add(new TextBlock { Text = value, FontSize = value.Length > 2 ? 13 : 15, HorizontalAlignment = HorizontalAlignment.Center });
                lines.Children.Add(new TextBlock { Text = "NM", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) });
                face.Children.Add(lines);
                if (i == preferred)
                {
                    face.Children.Add(new System.Windows.Shapes.Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = CreateFrozenBrush(Color.FromRgb(0x2A, 0x26, 0x22)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 2)
                    });
                }
                Button key = new()
                {
                    Content = face,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    ToolTip = $"Range {value} NM" + (i == preferred ? " (preferred range, Settings → Range and scale)." : ".") + " Keys Page up / Page down / End.",
                    Style = (Style)FindResource("PanelKey"),
                    Width = PanelKeyWidth,
                    Height = PanelKeyHeight,
                    Margin = new Thickness(2)
                };
                key.Click += (s, e) => SetRangeIndex(index);
                grid.Children.Add(key);
                analogKeys.Add((key, () => DistanceComboBox.SelectedIndex == index, true));
            }
            // One gold frame: range keys, PREF and the LIN | LOG key.
            StackPanel framed = new();
            framed.Children.Add(grid);
            group.Children.Add(new Border
            {
                BorderBrush = PanelFrameBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2, 3, 2, 3),
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = framed
            });
            Button back = new()
            {
                // Always PREF (the preferred range is set in Settings).
                Content = new TextBlock { Text = "PREF", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                ToolTip = "Back to the preferred range (Settings → Range and scale) (key End).",
                Style = (Style)FindResource("PanelKey"),
                Width = 64,
                Height = 26,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            back.Click += (s, e) => SetRangeIndex(Ranges.IndexOfClosest(settings.Active.PreferredRange));
            framed.Children.Add(back);
            analogKeys.Add((back, null, false));
            framed.Children.Add(SplitScaleKey());
            KnobPanel.Children.Add(group);
        }

        /// <summary>The key acts when pressed, then again after 0.45 s and every 0.12 s while held.</summary>
        private static void MakeRepeating(Button key, Action action)
        {
            key.ClickMode = ClickMode.Press;
            System.Windows.Threading.DispatcherTimer timer = new();
            timer.Tick += (s, e) =>
            {
                if (!key.IsPressed)
                {
                    timer.Stop();
                    return;
                }
                timer.Interval = TimeSpan.FromSeconds(0.12);
                action();
            };
            key.PreviewMouseLeftButtonDown += (s, e) =>
            {
                timer.Interval = TimeSpan.FromSeconds(0.45);
                timer.Start();
            };
            key.PreviewMouseLeftButtonUp += (s, e) => timer.Stop();
            key.LostMouseCapture += (s, e) => timer.Stop();
        }

        /// <summary>Lamps of the analog keys (range in use, neutral, runway DH, 100%).</summary>
        private void UpdateAnalogKeys()
        {
            // Latching keys (one key per range): the selected one stays pressed in; spring-loaded keys only light up.
            foreach ((Button key, Func<bool>? lit, bool latching) in analogKeys)
            {
                key.Tag = lit?.Invoke() == true ? (latching ? "Selected" : "Lit") : "Unlit";
            }
        }

        /// <summary>
        /// Ranges ticked in the profile: the range box, knob and keys offer only these; the range in use moves to the
        /// closest one when it is no longer ticked.
        /// </summary>
        private void ApplyRanges(Profile profile)
        {
            double[] values = Ranges.Of(profile.DisplayRanges);
            if (distances.Select(d => (double)d).SequenceEqual(values)) return;
            double current = DistanceComboBox.SelectedItem is Distance selected ? selected : runway.Distance;
            Ranges.Values = values;
            distances = values.Select(v => (Distance)v).ToArray();
            rangeKnob.Positions = values.Length;
            rangeKnob.InvalidateVisual();
            DistanceComboBox.ItemsSource = distances;
            DistanceComboBox.SelectedIndex = IndexOfDistance(current);
        }

        private void SetRangeIndex(int index)
        {
            DistanceComboBox.SelectedIndex = Math.Clamp(index, 0, DistanceComboBox.Items.Count - 1);
            UpdateKnobs();
        }

        private static readonly Style AnalogToolTipStyle = CreateAnalogToolTipStyle();

        private static Style CreateAnalogToolTipStyle()
        {
            Style style = new(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.BackgroundProperty, CreateFrozenBrush(Color.FromRgb(0x1A, 0x1B, 0x18))));
            style.Setters.Add(new Setter(Control.ForegroundProperty, CreateFrozenBrush(Color.FromRgb(0xFF, 0xB8, 0x40))));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, CreateFrozenBrush(Color.FromRgb(0x5A, 0x5C, 0x56))));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#B612")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Seal();
            return style;
        }

        /// <summary>Steps of each tilt knob below and above the neutral position, as last drawn.</summary>
        private readonly Dictionary<Knob, (int Down, int Up)> knobLayouts = [];

        /// <summary>
        /// Puts the knobs in the position of the current range and tilt (also changed with keyboard and mouse wheel).
        /// </summary>
        private void UpdateKnobs()
        {
            // The tilt range follows from the beam and the scan limits: the steps on the two sides of the neutral
            // position (0) may differ. Azimuth as shown (left/right possibly swapped).
            bool swapped = AzimuthSign < 0;
            foreach ((Knob knob, double tilt, double min, double max, int down, int up, string low, string high) in new[]
            {
                (elevationKnob, radar.TiltElevation, radar.TiltElevationMin, radar.TiltElevationMax,
                    radar.ElevationStepsDown, radar.ElevationStepsUp, "DN", "UP"),
                (azimuthKnob, radar.TiltAzimuth * AzimuthSign,
                    swapped ? -radar.TiltAzimuthMax : radar.TiltAzimuthMin, swapped ? -radar.TiltAzimuthMin : radar.TiltAzimuthMax,
                    swapped ? radar.AzimuthStepsRight : radar.AzimuthStepsLeft, swapped ? radar.AzimuthStepsLeft : radar.AzimuthStepsRight, "L", "R")
            })
            {
                if (!knobLayouts.TryGetValue(knob, out (int Down, int Up) layout) || layout != (down, up))
                {
                    knobLayouts[knob] = (down, up);
                    knob.Positions = down + up + 1;
                    knob.LabelFor = i => i == 0 && down > 0 ? low : i == down ? "0" : i == down + up && up > 0 ? high : null;
                    knob.InvalidateVisual();
                }
                // At the end of the range (possibly a shorter last step) the knob is on its last position.
                int offset = tilt >= max - 0.001 ? up
                    : tilt <= min + 0.001 ? -down
                    : radar.TiltStep > 0 ? (int)Math.Round(tilt / radar.TiltStep, MidpointRounding.AwayFromZero) : 0;
                knob.Index = Math.Clamp(down + offset, 0, down + up);
            }
            rangeKnob.Index = Math.Max(0, DistanceComboBox.SelectedIndex);
            UpdateAnalogKeys();
        }

        /// <summary>
        /// Analog mode: one key with a lamp for each runway of the airport in use (none when it has only one), and
        /// one for each published approach (glide path) of the runway (none when it has only one). The lamp shows
        /// the one in use. The keys are rebuilt only when the runways or approaches change.
        /// </summary>
        private void UpdateAnalogButtons()
        {
            bool analog = viewOptions.Analog;
            // Keys of the other mode or key style: built again in the style of this one.
            string keysLayout = $"{analog}|{settings.Active.KeyStyle}";
            if (keysAnalog != keysLayout)
            {
                keysAnalog = keysLayout;
                RunwayButtons.Children.Clear();
                runwayButtons.Clear();
                GlideButtons.Children.Clear();
                glideButtons.Clear();
            }
            Runway[] airport = runwayList.Where(r => string.Equals(r.ICAO, runway.ICAO, StringComparison.OrdinalIgnoreCase)).ToArray();
            RunwayButtonPanel.Visibility = airport.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (!runwayButtons.Select(b => b.Runway).SequenceEqual(airport))
            {
                RunwayButtons.Children.Clear();
                runwayButtons.Clear();
                if (airport.Length > 1)
                {
                    foreach (Runway r in airport)
                    {
                        Button button = ConsoleKey(r.BaseDesignator.ToUpperInvariant(), $"Runway {r.BaseDesignator}");
                        button.Click += (s, e) => SelectRunway(r);
                        RunwayButtons.Children.Add(button);
                        runwayButtons.Add((r, button));
                    }
                }
            }
            foreach ((Runway r, Button button) in runwayButtons)
            {
                button.Tag = r.RunwayKey == runway.RunwayKey ? "Selected" : "Unlit";
            }
            Runway[] glide = approaches.Length > 1 ? approaches : [];
            GlideButtonPanel.Visibility = glide.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (!glideButtons.Select(b => b.Approach).SequenceEqual(glide))
            {
                GlideButtons.Children.Clear();
                glideButtons.Clear();
                foreach (Runway a in glide)
                {
                    Button button = ConsoleKey(Runway.FormatGlideSlope(a.GlideSlope), $"Approach with a {Runway.FormatGlideSlope(a.GlideSlope)}° glide path");
                    button.Click += (s, e) => SelectApproach(a);
                    GlideButtons.Children.Add(button);
                    glideButtons.Add((a, button));
                }
            }
            foreach ((Runway a, Button button) in glideButtons)
            {
                button.Tag = a == runway ? "Selected" : "Unlit";
            }
        }

        /// <summary>Mode and key style the runway and glide path keys were built for.</summary>
        private string? keysAnalog;

        /// <summary>Analog console keys in the FIAR style (profile).</summary>
        private bool FiarKeys => settings.Active.KeyStyle == AnalogKeyStyle.Fiar;

        /// <summary>Runway or glide path key: square console key (analog) or flat key (modern display).</summary>
        private Button ConsoleKey(string text, string toolTip)
        {
            Button key = new()
            {
                Content = text,
                ToolTip = toolTip
            };
            if (viewOptions.Analog)
            {
                StyleAsKey(key, text);
            }
            else
            {
                key.Style = (Style)FindResource("ModernKey");
                key.Height = 28;
                key.Margin = new Thickness(1, 0, 1, 2);
                key.Tag = "Unlit";
            }
            return key;
        }

        /// <summary>
        /// Square console key: 45 wide, with big characters (smaller when the text is longer). The whole key lights
        /// up when its <see cref="FrameworkElement.Tag"/> is "Lit".
        /// </summary>
        private void StyleAsKey(Button key, string text, double width = 45, double height = 48)
        {
            key.Style = (Style)FindResource(FiarKeys ? "PanelKey" : "ConsoleButton");
            key.Width = width;
            key.Height = height;
            key.Margin = new Thickness(2, 0, 2, 2);
            key.FontSize = KeyFontSize(text, width - 9, height < 40 ? 16 : KeyMaxFontSize);
            key.Tag = "Unlit";
        }

        /// <summary>Widest text on a key (the key is 45 wide, with its border and a little margin).</summary>
        private const double KeyTextWidth = 36;
        private const double KeyMaxFontSize = 22;
        private static readonly Typeface KeyTypeface = new(
            new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Barlow Condensed"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        /// <summary>Largest font size (up to <paramref name="maxSize"/>) at which the text fits on a key.</summary>
        private double KeyFontSize(string text, double textWidth = KeyTextWidth, double maxSize = KeyMaxFontSize)
        {
            if (string.IsNullOrEmpty(text)) return maxSize;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (double size = maxSize; size > 7; size -= 0.5)
            {
                FormattedText formatted = new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    KeyTypeface, size, Brushes.Black, pixelsPerDip);
                if (formatted.WidthIncludingTrailingWhitespace <= textWidth) return size;
            }
            return 7;
        }

        /// <summary>Selects a runway of the list (also when the ICAO filter would hide it).</summary>
        private void SelectRunway(Runway r)
        {
            ShowAllIfHidden(r);
            RunwayComboBox.SelectedItem = r;
        }

        /// <summary>
        /// Sign of the azimuth tilt as shown and commanded: 1 = as seen by the pilot, -1 = swapped (as seen from the
        /// runway), see <see cref="Profile.AzimuthTiltSwapped"/>.
        /// </summary>
        private int AzimuthSign => settings.Active.AzimuthTiltSwapped ? -1 : 1;

        /// <summary>
        /// Brightness of the radar picture by steps of 10% (from 10% to 150%), separate for the modern display and
        /// the analog scope, saved in the profile.
        /// </summary>
        private void ChangeBrightness(int steps)
        {
            Profile profile = settings.Active;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            int current = analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            SetBrightness(current + steps * Profile.BrightnessStep);
        }

        private void SetBrightness(int percent)
        {
            Profile profile = settings.Active;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            int current = analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            int value = Math.Clamp(percent, Profile.MinBrightness, Profile.MaxBrightness);
            if (value == current) return;
            if (analog) profile.BrightnessAnalog = value; else profile.BrightnessModern = value;
            SettingsStore.Save(settings);
            // Above 100% the colours change: the views are drawn again with the boosted theme.
            if (current > 100 || value > 100) ApplyProfile(); else ApplyBrightness();
        }

        /// <summary>Boost of the colours above 100% brightness: 0 (up to 100%) to 1 (at the maximum).</summary>
        private double BrightnessBoost(Profile profile)
        {
            int percent = profile.DisplayMode == DisplayMode.Analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            return Math.Clamp((percent - 100) / (double)(Profile.MaxBrightness - 100), 0, 1);
        }

        /// <summary>
        /// Dims what is drawn on the radar views (lines, tracks, labels, texts). The modern display dims all of it,
        /// the analog scope only the phosphor: frame, glass and console panel stay as they are.
        /// </summary>
        private void ApplyBrightness()
        {
            Profile profile = settings.Active;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            int percent = analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            // Up to 100% the picture is dimmed; above it the colours of the theme are boosted instead.
            Vertical.Opacity = Math.Min(percent, 100) / 100.0;
            Horizontal.Opacity = Math.Min(percent, 100) / 100.0;
            BrightnessText.Text = $"{percent}%";
            brightnessKnob.Index = Math.Clamp(percent / Profile.BrightnessStep - 1, 0, brightnessKnob.Positions - 1);
            UpdateAnalogKeys();
        }

        /// <summary>
        /// Modern display or analog scope: switched with the button or the A key, saved in the profile.
        /// </summary>
        /// <summary>
        /// Key P / Shift+P: next / previous profile of the list (in a circle), without opening the settings. The
        /// runway and the traffic stay; the antenna goes back to neutral (the beam of the other profile may differ).
        /// </summary>
        private void SwitchProfile(int direction)
        {
            List<Profile> profiles = settings.Profiles;
            if (profiles.Count < 2)
            {
                ShowBanner("ONLY ONE PROFILE");
                return;
            }
            int index = profiles.IndexOf(settings.Active);
            Profile next = profiles[((index + direction) % profiles.Count + profiles.Count) % profiles.Count];
            settings.ActiveProfile = next.Name;
            SettingsStore.Save(settings);
            radar.Neutral();
            ApplyProfile();
            UpdateKnobs();
            ShowBanner($"PROFILE: {next.Name.ToUpperInvariant()}");
        }

        private Border? banner;
        private System.Windows.Threading.DispatcherTimer? bannerTimer;

        /// <summary>A short message in the middle of the views for two seconds (e.g. the profile chosen with P).</summary>
        private void ShowBanner(string text)
        {
            if (banner == null)
            {
                banner = new Border
                {
                    Padding = new Thickness(14, 8, 14, 8),
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Child = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold }
                };
                Panel.SetZIndex(banner, 1000);
                if (Content is Grid root)
                {
                    Grid.SetColumn(banner, 0);
                    root.Children.Add(banner);
                }
                bannerTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                bannerTimer.Tick += (s, e) =>
                {
                    bannerTimer.Stop();
                    banner.Visibility = Visibility.Collapsed;
                };
            }
            TextBlock label = (TextBlock)banner.Child;
            label.Text = text;
            bool analog = viewOptions.Analog;
            // Analog: amber on dark, as the console readouts; modern: white on dark.
            banner.Background = CreateFrozenBrush(Color.FromArgb(0xE0, 0x14, 0x15, 0x12));
            banner.BorderBrush = CreateFrozenBrush(analog ? Color.FromRgb(0x5A, 0x5C, 0x56) : Color.FromRgb(0x80, 0x80, 0x80));
            label.Foreground = CreateFrozenBrush(analog ? Color.FromRgb(0xFF, 0xB8, 0x40) : System.Windows.Media.Colors.White);
            label.FontFamily = analog ? new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#B612") : new FontFamily("Segoe UI");
            banner.Visibility = Visibility.Visible;
            bannerTimer!.Stop();
            bannerTimer.Start();
        }

        /// <summary>Opens (or brings to front) the test traffic window, beside the main window.</summary>
        private void OpenTestTraffic()
        {
            if (testTrafficWindow == null)
            {
                testTrafficWindow = new TestTrafficWindow(testTraffic, OpenJoystick,
                    () => runway.MagneticVariation ?? settings.Active.MagneticVariation,
                    () => aurora.LastMetar is MetarReport report && string.Equals(report.Icao, runway.ICAO, StringComparison.OrdinalIgnoreCase) ? report.Text : null)
                { Owner = this };
                testTrafficWindow.Left = Math.Max(0, Left + 40);
                testTrafficWindow.Top = Math.Max(0, Top + 60);
                testTrafficWindow.SetRain(rainOverride is RainLevel current ? (int)current + 1 : 0, clutterFilterStep);
                testTrafficWindow.RainChanged += level => rainOverride = level;
                testTrafficWindow.ClutterFilterChanged += SetClutterFilter;
                testTrafficWindow.Closed += (s, e) =>
                {
                    testTrafficWindow = null;
                    rainOverride = null;
                    TestButton.Tag = viewOptions.Analog ? "Unlit" : null;
                    Redraw();
                };
                testTrafficWindow.Show();
                TestButton.Tag = viewOptions.Analog ? "Lit" : null;
            }
            else
            {
                if (testTrafficWindow.WindowState == WindowState.Minimized) testTrafficWindow.WindowState = WindowState.Normal;
                testTrafficWindow.Activate();
            }
        }

        /// <summary>Joystick settings and live test (modal over the window that opens them).</summary>
        private void OpenJoystick(Window owner)
        {
            new JoystickWindow(settings) { Owner = owner }.ShowDialog();
        }

        private JoystickController? joystick;
        private readonly System.Windows.Threading.DispatcherTimer joystickTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

        /// <summary>The joystick is read about 25 times a second (while the program runs, also in background).</summary>
        private void StartJoystick()
        {
            joystick = new JoystickController(
                () => settings.Joystick,
                () => testTrafficWindow,
                (elevation, azimuth) => TiltAntenna(elevation, azimuth * AzimuthSign),
                NeutralAntenna,
                ShowBanner);
            joystickTimer.Tick += (s, e) =>
            {
                try
                {
                    joystick.Tick();
                }
                catch
                {
                    // A device removed in the middle of a reading: the next tick tries again.
                }
            };
            joystickTimer.Start();
        }

        private Border? testTrafficSign;

        /// <summary>"TEST TRAFFIC" at the top of the views while virtual aircraft are flying (never mixed up with real traffic).</summary>
        private void UpdateTestTrafficSign()
        {
            bool on = testTraffic.Count > 0;
            if (testTrafficSign == null)
            {
                if (!on) return;
                testTrafficSign = new Border
                {
                    Background = CreateFrozenBrush(Color.FromArgb(0xD0, 0x30, 0x20, 0x00)),
                    BorderBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false,
                    Child = new TextBlock { Text = "TEST TRAFFIC", FontWeight = FontWeights.Bold, FontSize = 14, Foreground = CreateFrozenBrush(Color.FromRgb(0xFF, 0x8C, 0x00)) }
                };
                Panel.SetZIndex(testTrafficSign, 999);
                if (Content is Grid root)
                {
                    Grid.SetColumn(testTrafficSign, 0);
                    root.Children.Add(testTrafficSign);
                }
            }
            testTrafficSign.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Key C: linear or log (compressed) distance scale (saved in the profile).</summary>
        private void ToggleDistanceScale() => SetDistanceScale(!settings.Active.CompressedScale);

        /// <summary>LIN / LOG keys and key C: linear or log (compressed) distance scale, saved in the profile.</summary>
        private void SetDistanceScale(bool log)
        {
            Profile profile = settings.Active;
            if (profile.CompressedScale == log) return;
            profile.CompressedScale = log;
            SettingsStore.Save(settings);
            ApplyProfile();
            ShowBanner(log
                ? $"LOG SCALE ({profile.CompressionKnee.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} NM)"
                : "LINEAR SCALE");
        }

        private void ToggleDisplayMode()
        {
            Profile profile = settings.Active;
            if (profile.LockDisplayMode)
            {
                ShowBanner("DISPLAY MODE LOCKED");
                return;
            }
            profile.DisplayMode = profile.DisplayMode == DisplayMode.Analog ? DisplayMode.Modern : DisplayMode.Analog;
            SettingsStore.Save(settings);
            ApplyProfile();
        }

        /// <summary>Glow of the phosphor of the analog scope.</summary>
        private readonly System.Windows.Media.Effects.DropShadowEffect phosphorGlow = new()
        {
            Color = Theme.DefaultPhosphor,
            ShadowDepth = 0,
            BlurRadius = 8,
            Opacity = 0.85
        };

        /// <summary>
        /// Switches the window between the modern display and the analog console.
        /// </summary>
        private void ApplyDisplayMode(bool analog)
        {
            // Analog: the information is on the console panel instead of the screen.
            infoPanel.Visibility = analog ? Visibility.Collapsed : Visibility.Visible;
            ConsoleHost.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            foreach (Canvas canvas in new[] { Vertical, Horizontal })
            {
                // Transparent in the analog mode: the glass is behind, and the glow follows only the drawn lines.
                canvas.Background = analog ? Brushes.Transparent : viewOptions.Theme.Brush(StyleElement.Background);
                canvas.Effect = analog ? phosphorGlow : null;
            }
            if (analog)
            {
                ControlPanel.Background = new SolidColorBrush(ScopeBezel.PanelColor);
            }
            else
            {
                ControlPanel.ClearValue(Border.BackgroundProperty);
            }
            ApplyControlStyles(analog);
            // Analog: tooltips as the rest of the console (dark, amber text) instead of the standard yellow ones.
            if (analog) Resources[typeof(ToolTip)] = AnalogToolTipStyle;
            else Resources.Remove(typeof(ToolTip));
            Visibility modern = analog ? Visibility.Collapsed : Visibility.Visible;
            // Analog: no drop-down lists, no DH field (the console readout and the knob are the DH): airport window
            // and keys instead.
            AptPanel.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            IcaoPanel.Visibility = modern;
            DhPanel.Visibility = analog || !settings.Active.ShowDhSelector ? Visibility.Collapsed : Visibility.Visible;
            ApplyKeyPanels(analog);
            aptEntry.Icao = runway.ICAO;
            DistanceComboBox.Visibility = modern;
            ScalePanel.Visibility = modern;
            TiltPanel.Visibility = modern;
            BrightnessPanel.Visibility = modern;
            DhDownButton.Visibility = modern;
            DhUpButton.Visibility = modern;
            GlidePathPanel.Visibility = modern;
            LabelsButton.Visibility = modern;
            // The analog scope has only the published approaches (no free angle).
            if (analog && runway.IsUnpublished && selectedApproach != null)
            {
                SelectApproach(selectedApproach, keepDecisionHeight: true);
            }
            KnobPanel.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            BuildAnalogControls();
            LayoutDisplay();
            UpdateKnobs();
            UpdateAnalogButtons();
            ApplyBrightness();
        }

        /// <summary>
        /// Runway and glide path key groups: on a recessed plate with engraved titles (analog), or plain with the titles
        /// of the modern display; there the glide path keys are just above the free angle box.
        /// </summary>
        private void ApplyKeyPanels(bool analog)
        {
            foreach (Border plate in new[] { RunwayPlate, GlidePlate })
            {
                if (analog)
                {
                    plate.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x20, 0x1D));
                    plate.BorderBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x0D));
                    plate.Padding = new Thickness(0, 2, 0, 0);
                }
                else
                {
                    plate.Background = Brushes.Transparent;
                    plate.BorderBrush = Brushes.Transparent;
                    plate.Padding = new Thickness(0);
                }
            }
            if (analog)
            {
                RunwayKeysLabel.Style = (Style)FindResource("EngravedLabel");
                RunwayKeysLabel.Text = "RWY";
            }
            else
            {
                RunwayKeysLabel.Style = null;
                RunwayKeysLabel.Margin = new Thickness(2, 0, 0, 2);
                RunwayKeysLabel.Text = "Runway";
            }
            GlideKeysLabel.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            // Analog: after the distance box (collapsed there); modern: inside the GP panel, between title and box.
            Panel target = analog ? SelectorStack : GlidePathPanel;
            if (GlideButtonPanel.Parent != target)
            {
                ((Panel)GlideButtonPanel.Parent).Children.Remove(GlideButtonPanel);
                if (analog)
                {
                    target.Children.Insert(SelectorStack.Children.IndexOf(DistanceComboBox) + 1, GlideButtonPanel);
                }
                else
                {
                    target.Children.Insert(GlidePathPanel.Children.IndexOf(GlidePathLabel) + 1, GlideButtonPanel);
                }
            }
            GlideButtonPanel.Margin = analog ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 2);
        }

        /// <summary>
        /// Analog mode: buttons as the keys of an equipment (engraved capital labels, lamps), and the text fields
        /// as readout windows (dark, amber text). Modern display: the normal controls.
        /// </summary>
        private void ApplyControlStyles(bool analog)
        {
            // Analog: short engraved names on square keys (the full name in the tooltip); the mode key names the
            // display it switches to.
            (Button Button, string Modern, string Analog, string Tip, int Height)[] buttons =
            [
                (ModeButton, "Analog (A)", "MODERN", "Switch to the modern display (key A).", 26),
                (CoordinationButton, "Coordination", "COORD", "Coordination light panel with the tower (voiceless coordination).", 26),
                (TestButton, "Test traffic (T)", "TEST", "Test traffic: aircraft to practise with (key T).", 26),
                (SettingsButton, "Settings...", "SETUP", "Settings.", 30)
            ];
            foreach ((Button button, string modern, string analogText, string tip, int height) in buttons)
            {
                if (analog)
                {
                    StyleAsKey(button, analogText);
                    button.Content = analogText;
                    button.ToolTip = tip;
                }
                else
                {
                    button.ClearValue(StyleProperty);
                    button.ClearValue(FontSizeProperty);
                    button.Content = modern;
                    button.Width = 98;
                    button.Height = height;
                    button.Margin = new Thickness(0, 4, 0, 0);
                    button.Tag = null;
                    button.ToolTip = button == ModeButton ? "Switch between the modern display and the analog scope (key A)." : button == CoordinationButton || button == TestButton ? tip : null;
                }
            }
            SpareKey.Visibility = Visibility.Collapsed;
            SystemSeparator.Visibility = analog ? Visibility.Collapsed : Visibility.Visible;
            SystemPlate.Background = analog ? new SolidColorBrush(Color.FromRgb(0x1F, 0x20, 0x1D)) : Brushes.Transparent;
            SystemPlate.BorderBrush = analog ? new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x0D)) : Brushes.Transparent;
            SystemPlate.Padding = analog ? new Thickness(0, 2, 0, 0) : new Thickness(0);
            // Lamp: the coordination key is lit while its panel is open. The mode key is a plain command.
            CoordinationButton.Tag = analog ? (coordinationWindow != null ? "Lit" : "Unlit") : null;
            TestButton.Tag = analog ? (testTrafficWindow != null ? "Lit" : "Unlit") : null;
            // Display mode locked in the profile: no mode button (analog: a blank key keeps the place of the others).
            bool locked = settings.Active.LockDisplayMode;
            ModeButton.Visibility = locked && !analog ? Visibility.Collapsed : Visibility.Visible;
            if (locked && analog)
            {
                ModeButton.Content = "";
                ModeButton.ToolTip = "Display mode locked in this profile (Settings): change profile with P / Shift+P.";
            }
        }

        /// <summary>
        /// Places the views: full area in the modern display; in the analog mode a round screen in a metal ring
        /// on the console panel, with the two views one above the other inside it (as on the old PAR scopes).
        /// </summary>
        private void LayoutDisplay()
        {
            bool analog = viewOptions.Analog;
            bool right = settings.Active.RunwaySide == RunwaySide.Right;
            double width = ScopeHost.ActualWidth;
            double height = ScopeHost.ActualHeight;
            if (!analog || width <= 0 || height <= 0)
            {
                ScopeArea.ClearValue(WidthProperty);
                ScopeArea.ClearValue(HeightProperty);
                ScopeArea.HorizontalAlignment = HorizontalAlignment.Stretch;
                ScopeArea.VerticalAlignment = VerticalAlignment.Stretch;
                ScopeArea.Clip = null;
                ScopeArea.Background = null;
                Vertical.Margin = new Thickness(0);
                Horizontal.Margin = new Thickness(0);
                BezelLayer.Children.Clear();
                DisplayArea.Background = viewOptions.Theme.Brush(StyleElement.Background);
                return;
            }
            double ring = Math.Max(5, Math.Min(width, height) * 0.018);
            // The screen is as wide as the window allows; in a low window its top and bottom (only frame and
            // glass) are cut by the window edges, up to MinVisibleFraction of the diameter, so the views stay large.
            const double MinVisibleFraction = 0.75;
            double radius = Math.Min(width / 2 - ring - 4, height / (2 * MinVisibleFraction));
            radius = Math.Max(60, Math.Min(radius, Math.Max(width, height)));
            double diameter = 2 * radius;
            double visible = Math.Min(diameter, height);
            double cut = (diameter - visible) / 2;
            ScopeArea.Width = diameter;
            ScopeArea.Height = visible;
            ScopeArea.HorizontalAlignment = HorizontalAlignment.Center;
            ScopeArea.VerticalAlignment = VerticalAlignment.Center;
            ScopeArea.Clip = new EllipseGeometry(new Point(radius, visible / 2), radius, radius);
            ScopeArea.Background = glassBrush;
            // The antenna side is moved in, so the origin of both views is inside the circle; the top and bottom
            // margins shrink as the circle is cut by the window.
            double inner = diameter * 0.12;
            double outer = diameter * 0.04;
            double edge = Math.Max(0, diameter * 0.07 - cut);
            Vertical.Margin = right ? new Thickness(outer, edge, inner, 0) : new Thickness(inner, edge, outer, 0);
            Horizontal.Margin = right ? new Thickness(outer, 0, inner, edge) : new Thickness(inner, 0, outer, edge);
            DisplayArea.Background = new SolidColorBrush(ScopeBezel.PanelColor);
            ScopeBezel.Draw(BezelLayer, new Size(width, height), new Point(width / 2, height / 2), radius, ring);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            coordinationWindow?.Close();
            joystickTimer.Stop();
            Open = false;
            timer.Stop();
            aurora.Close();
            // Remember runway and range for the next start.
            if (selectedApproach != null)
            {
                settings.LastRunway = runway.ToString();
                settings.LastRange = runway.Distance;
                settings.LastGlideSlope = selectedApproach?.GlideSlope;
            }
            SaveWindowPlacement();
            SettingsStore.Save(settings);
        }

        private const double MinWindowWidth = 400;
        private const double MinWindowHeight = 300;

        /// <summary>
        /// Restores the window size and position of the last session. If the saved position is no longer on
        /// any screen (e.g. a monitor was disconnected or the layout changed), the window keeps the saved size
        /// (reduced to fit if needed) but is centred on the primary screen.
        /// </summary>
        private void RestoreWindowPlacement()
        {
            WindowPlacement? p = settings.Window;
            if (p == null || !double.IsFinite(p.Left) || !double.IsFinite(p.Top)
                || !double.IsFinite(p.Width) || !double.IsFinite(p.Height))
            {
                return;
            }
            Rect allScreens = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Width = Math.Clamp(p.Width, MinWindowWidth, Math.Max(MinWindowWidth, allScreens.Width));
            Height = Math.Clamp(p.Height, MinWindowHeight, Math.Max(MinWindowHeight, allScreens.Height));
            // The title bar must be reachable with the mouse: its centre and both of its ends must be on screen.
            double titleY = p.Top + 10;
            bool visible = allScreens.Contains(new Point(p.Left + Width / 2, titleY))
                && allScreens.Contains(new Point(p.Left + 40, titleY))
                && allScreens.Contains(new Point(p.Left + Width - 40, titleY));
            if (visible)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = p.Left;
                Top = p.Top;
            }
            else
            {
                Width = Math.Min(Width, SystemParameters.WorkArea.Width);
                Height = Math.Min(Height, SystemParameters.WorkArea.Height);
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            // Maximize after setting the normal bounds, so the window is maximized on the screen it was on.
            if (p.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        /// <summary>
        /// Saves the window's normal bounds (also when maximized or minimized) and whether it is maximized.
        /// </summary>
        private void SaveWindowPlacement()
        {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
            settings.Window = new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == WindowState.Maximized
            };
        }

        /// <summary>Aurora has been connected at least once in this session.</summary>
        private bool auroraEverConnected;

        /// <summary>
        /// One minute after the start, still no connection to Aurora (STS FAIL): most likely the access of third-party
        /// programs is off in Aurora. Said once per session.
        /// </summary>
        private void StartAuroraConnectionHint()
        {
            System.Windows.Threading.DispatcherTimer hint = new() { Interval = TimeSpan.FromMinutes(1) };
            hint.Tick += (s, e) =>
            {
                hint.Stop();
                if (aurora.Connected || auroraEverConnected || !IsLoaded) return;
                MessageBox.Show(this,
                    "AuroraPAR is not connected to Aurora yet (STS FAIL).\n\n" +
                    "Start Aurora on this PC and allow the third-party programs to connect:\n" +
                    "Aurora → Settings → Other → Software → 3rd Party software access (on).\n\n" +
                    "The connection is retried automatically: STS OK appears as soon as it works.",
                    "Aurora PAR - Connection to Aurora", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            hint.Start();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartAuroraConnectionHint();
            try
            {
                runways = await DataFile.GetRunways(dataPath);
                runwayList = GroupRunways(runways);
                // The filter is for the session: it starts empty (the runway of the last session is still selected).
                SetFilterText("");
                ApplyRunwayFilter(userTyped: false);
                if (runways.Length == 0)
                {
                    MessageBox.Show(this, $"No valid runway found in {Path.GetFullPath(dataPath)}.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot read the runway file {Path.GetFullPath(dataPath)}:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RunwayComboBox.SelectionChanged += RunwayComboBox_SelectionChanged;
            // Restore the runway used last time.
            // The last runway is saved with the name of its line ("LIPC 11 2.8"): its runway in the list, and its angle.
            Runway? lastLine = runways.FirstOrDefault(r => r.ToString() == settings.LastRunway);
            Runway? last = lastLine == null ? null : runwayList.FirstOrDefault(r => r.RunwayKey == lastLine.RunwayKey);
            restoreGlideSlope = settings.LastGlideSlope ?? lastLine?.GlideSlope;
            if (last != null)
            {
                restoringSession = true;
                ShowAllIfHidden(last);
                RunwayComboBox.SelectedItem = last;
                restoringSession = false;
            }
            // Range at start, as chosen in the profile.
            Profile profile = settings.Active;
            double startRange = profile.StartupRange switch
            {
                StartupRange.LastUsed when last != null && settings.LastRange is double lastRange => lastRange,
                StartupRange.Fixed => profile.PreferredRange,
                _ => runway.DefaultDistance
            };
            DistanceComboBox.SelectedIndex = IndexOfDistance(startRange);
            timer.Start();
            await ConnectionLoop();
        }

        private void RunwayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Runway first)
            {
                // The list was only filtered again: the runway in use stays as it is.
                if (filteringRunways && selectedApproach != null && first.RunwayKey == selectedApproach.RunwayKey) return;
                Runway previous = runway;
                approaches = ApproachesOf(first);
                // Approach: the one of the last session or the one in use before reloading the file (same angle),
                // otherwise the first line of the file for this runway.
                double? wantedGlideSlope = restoringSession ? restoreGlideSlope
                    : reloadingRunways ? (selectedApproach?.GlideSlope) : null;
                Runway r = (wantedGlideSlope is double gp ? approaches.FirstOrDefault(a => Math.Abs(a.GlideSlope - gp) < 0.005) : null) ?? first;
                selectedApproach = r;
                runway = r;
                // The decision height changed on the fly is not kept: back to the runway file value.
                runway.MDH = runway.DefaultMDH;
                // New runway, new antenna: back to neutral.
                radar.Neutral();
                DhTextBox.Text = FormatHeight(runway.MDH);
                profileView.SetRunway(runway);
                horizontalView.SetRunway(runway);
                // Range for the new runway, as chosen in the profile (at start the startup rule applies instead).
                Profile profile = settings.Active;
                double range = restoringSession ? r.DefaultDistance : reloadingRunways ? previous.Distance : profile.RunwayChangeRange switch
                {
                    RunwayChangeRange.KeepCurrent => previous.Distance,
                    RunwayChangeRange.Fixed => profile.PreferredRange,
                    _ => r.DefaultDistance
                };
                DistanceComboBox.SelectedIndex = IndexOfDistance(range);
                // Make sure runway and distance box always agree, even if the index did not change.
                if (DistanceComboBox.SelectedItem is Distance d)
                {
                    runway.Distance = d;
                }
                UpdateGlidePathSelector();
                InvalidateViews();
            }
        }

        /// <summary>True while the filter box is changed by the program.</summary>
        private bool settingFilter;
        /// <summary>True while the runway list is filtered again (the runway in use must not be selected again).</summary>
        private bool filteringRunways;
        /// <summary>Callsign whose airport has already been proposed in the filter (once per connection).</summary>
        private string? suggestedCallsign;

        private void SetFilterText(string text)
        {
            settingFilter = true;
            try { IcaoFilterBox.Text = text; }
            finally { settingFilter = false; }
        }

        /// <summary>
        /// Shows in the runway list only the airports starting with the letters typed in the ICAO box (all when
        /// empty). If no airport matches, the box turns red and the list is not changed. <paramref name="userTyped"/>:
        /// an airport with a single runway is then selected at once, and with several the list opens when the ICAO is
        /// complete.
        /// </summary>
        private void ApplyRunwayFilter(bool userTyped, bool keepSelection = true)
        {
            string text = IcaoFilterBox.Text.Trim().ToUpperInvariant();
            Runway[] filtered = text.Length == 0 ? runwayList
                : runwayList.Where(r => r.ICAO.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length == 0 && runwayList.Length > 0)
            {
                IcaoFilterBox.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0xC8));
                return;
            }
            IcaoFilterBox.ClearValue(Control.BackgroundProperty);
            filteringRunways = true;
            try
            {
                RunwayComboBox.ItemsSource = filtered;
                // The runway in use stays shown if it is in the list.
                Runway? current = selectedApproach == null || !keepSelection ? null : filtered.FirstOrDefault(r => r.RunwayKey == selectedApproach.RunwayKey);
                RunwayComboBox.SelectedItem = current;
            }
            finally
            {
                filteringRunways = false;
            }
            if (!userTyped || RunwayComboBox.SelectedItem != null) return;
            if (filtered.Length == 1)
            {
                RunwayComboBox.SelectedItem = filtered[0];
            }
            else if (text.Length == 4 && filtered.Length > 1)
            {
                RunwayComboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>
        /// Letters typed in the ICAO box: red when no airport starts with them; the airport is set with the fourth
        /// letter, or with Enter when the letters fit a single airport.
        /// </summary>
        private void IcaoTyped(bool enter)
        {
            string text = IcaoFilterBox.Text.Trim().ToUpperInvariant();
            string[] airports = runwayList.Select(r => r.ICAO.ToUpperInvariant())
                .Where(icao => icao.StartsWith(text, StringComparison.Ordinal)).Distinct().ToArray();
            if (text.Length > 0 && airports.Length == 0)
            {
                IcaoFilterBox.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0xC8));
                return;
            }
            IcaoFilterBox.ClearValue(Control.BackgroundProperty);
            if (text.Length == 4 && airports.Contains(text)) SelectAirport(text);
            else if (enter && text.Length > 0 && airports.Length == 1) SelectAirport(airports[0]);
        }

        /// <summary>The ICAO box shows the airport in use again (after Esc, or when it loses the focus).</summary>
        private void ShowAirportInUse()
        {
            IcaoFilterBox.ClearValue(Control.BackgroundProperty);
            if (IcaoFilterBox.Text != runway.ICAO) SetFilterText(runway.ICAO);
        }

        /// <summary>Empties the filter if it hides the given runway (e.g. the one of the last session).</summary>
        private void ShowAllIfHidden(Runway runwayToShow)
        {
            if (RunwayComboBox.ItemsSource is Runway[] shown && shown.Contains(runwayToShow)) return;
            SetFilterText("");
            ApplyRunwayFilter(userTyped: false);
        }

        /// <summary>
        /// The airport of the callsign connected in Aurora (LIPC_APP: LIPC), if the file has it, has priority over
        /// anything typed in the filter: it is set there, and if the runway in use belongs to another airport the first
        /// runway of this one is selected. Once per callsign, so later changes by the controller are respected.
        /// Without a matching callsign nothing changes (the runway of the last session stays).
        /// </summary>
        private void SuggestAirport(string? callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign) || callsign == suggestedCallsign) return;
            suggestedCallsign = callsign;
            string icao = callsign.Split('_', '-')[0].Trim().ToUpperInvariant();
            if (icao.Length != 4) return;
            SelectAirport(icao);
        }

        /// <summary>
        /// Sets an airport: the filter shows it, and if the runway in use belongs to another airport its first runway
        /// is selected. False when the file has no such airport (nothing changes).
        /// </summary>
        private bool SelectAirport(string code)
        {
            string icao = code.Trim().ToUpperInvariant();
            Runway? first = runwayList.FirstOrDefault(r => string.Equals(r.ICAO, icao, StringComparison.OrdinalIgnoreCase));
            if (first == null) return false;
            SetFilterText(icao);
            ApplyRunwayFilter(userTyped: false);
            if (selectedApproach == null || !string.Equals(selectedApproach.ICAO, icao, StringComparison.OrdinalIgnoreCase))
            {
                RunwayComboBox.SelectedItem = first;
            }
            return true;
        }

        /// <summary>Glide path of the approach to restore at start.</summary>
        private double? restoreGlideSlope;

        /// <summary>One entry per runway (airport and designator without the angle), the first line of the file for each.</summary>
        private static Runway[] GroupRunways(Runway[] lines)
        {
            return lines.GroupBy(r => r.RunwayKey).Select(g => g.First()).ToArray();
        }

        /// <summary>Published approaches of a runway: the lines of the file with the same airport and designator, by angle.</summary>
        private Runway[] ApproachesOf(Runway first)
        {
            string key = first.RunwayKey;
            return runways.Where(r => r.RunwayKey == key)
                .GroupBy(r => Math.Round(r.GlideSlope, 3))
                .Select(g => g.First())
                .OrderBy(r => r.GlideSlope)
                .ToArray();
        }

        /// <summary>
        /// Another published approach of the same runway (other glide path angle): its own line of the file (DH,
        /// touchdown...), same range, antenna tilt and tracks. <paramref name="keepDecisionHeight"/>: the DH in use is
        /// kept (back from an unpublished angle), otherwise the DH of the line.
        /// </summary>
        private void SelectApproach(Runway approach, bool keepDecisionHeight = false)
        {
            if (approach == runway) { UpdateGlidePathSelector(); return; }
            double mdh = runway.MDH;
            double distance = runway.Distance;
            selectedApproach = approach;
            runway = approach;
            runway.MDH = keepDecisionHeight ? mdh : runway.DefaultMDH;
            runway.Distance = distance;
            DhTextBox.Text = FormatHeight(runway.MDH);
            profileView.SetRunway(runway, keepTracks: true);
            horizontalView.SetRunway(runway, keepTracks: true);
            UpdateGlidePathSelector();
            InvalidateViews();
        }

        /// <summary>
        /// Modern display: an angle typed by the controller that is not one of the published approaches. Same runway
        /// and DH in use, only the glide path (and so the touchdown point if not given in the file, and the MAPt) changes.
        /// </summary>
        private void SetUnpublishedGlideSlope(double degrees)
        {
            Runway basis = selectedApproach ?? runway;
            Runway copy = basis.WithGlideSlope(degrees);
            copy.MDH = runway.MDH;
            copy.Distance = runway.Distance;
            runway = copy;
            profileView.SetRunway(runway, keepTracks: true);
            horizontalView.SetRunway(runway, keepTracks: true);
            UpdateGlidePathSelector();
            InvalidateViews();
        }

        /// <summary>Lowest and highest glide path angle that can be typed (degrees).</summary>
        private const double MinGlideSlope = 1.0;
        private const double MaxGlideSlope = 7.0;

        /// <summary>Reads the angle typed in the GP box: a published one selects that approach, another one is unpublished.</summary>
        private void ApplyGlidePathText()
        {
            if (updatingGlidePath) return;
            string text = GlidePathTextBox.Text.Trim().Replace(',', '.').TrimEnd('°').Trim();
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double degrees)
                || degrees < MinGlideSlope || degrees > MaxGlideSlope)
            {
                UpdateGlidePathSelector();
                return;
            }
            degrees = Math.Round(degrees, 2);
            Runway? published = approaches.FirstOrDefault(a => Math.Abs(a.GlideSlope - degrees) < 0.005);
            if (published != null)
            {
                SelectApproach(published, keepDecisionHeight: runway.IsUnpublished);
            }
            else if (viewOptions.Analog)
            {
                UpdateGlidePathSelector();
            }
            else if (Math.Abs(degrees - runway.GlideSlope) >= 0.005)
            {
                SetUnpublishedGlideSlope(degrees);
            }
            else
            {
                UpdateGlidePathSelector();
            }
        }

        /// <summary>Shows the angle in use in the GP box, orange when unpublished (the published angles are the GP keys).</summary>
        private void UpdateGlidePathSelector()
        {
            updatingGlidePath = true;
            try
            {
                List<string> items = approaches.Select(a => Runway.FormatGlideSlope(a.GlideSlope)).ToList();
                GlidePathTextBox.Text = Runway.FormatGlideSlope(runway.GlideSlope);
                if (runway.IsUnpublished)
                {
                    GlidePathTextBox.Foreground = Brushes.DarkOrange;
                    GlidePathTextBox.FontWeight = FontWeights.Bold;
                    GlidePathLabel.Text = "GP (°) UNPUBL.";
                }
                else
                {
                    GlidePathTextBox.ClearValue(Control.ForegroundProperty);
                    GlidePathTextBox.ClearValue(Control.FontWeightProperty);
                    GlidePathLabel.Text = "GP (°)";
                }
                GlidePathPanel.ToolTip = (approaches.Length > 1
                        ? $"Glide path: {approaches.Length} published approaches for this runway in runways.par ({string.Join(", ", items)}°): choose one with the keys."
                        : "Glide path of this runway in runways.par.")
                    + $"\nAnother angle ({MinGlideSlope:0.0} to {MaxGlideSlope:0.0}) can be typed and confirmed with Enter: it is an UNPUBLISHED APPROACH (shown in orange)."
                    + "\nThe DH stays the same; the missed approach point (MAPt DIST) moves with the angle."
                    + "\nEsc: back to the angle in use. Not saved: choosing the runway again goes back to the file.";
            }
            finally
            {
                updatingGlidePath = false;
            }
            if (viewOptions.Analog) UpdateKnobs();
            UpdateAnalogButtons();
        }

        private const double DecisionHeightStep = 10;

        private static string FormatHeight(double feet)
        {
            return feet.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Changes the decision height of the current runway for this session only (never saved).
        /// </summary>
        private void SetDecisionHeight(double feet)
        {
            runway.MDH = Math.Clamp(Math.Round(feet), 0, 5000);
            DhTextBox.Text = FormatHeight(runway.MDH);
            InvalidateViews();
            UpdateAnalogKeys();
        }

        private void ApplyDecisionHeightText()
        {
            if (double.TryParse(DhTextBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double feet)
                || double.TryParse(DhTextBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out feet))
            {
                if (Math.Round(feet) != runway.MDH)
                {
                    SetDecisionHeight(feet);
                    return;
                }
            }
            DhTextBox.Text = FormatHeight(runway.MDH);
        }

        /// <summary>Runway editor (from Settings): it is a dialog of the window that opened it.</summary>
        private async void OpenRunwayEditor(Window owner)
        {
            RunwayEditorWindow window = new(dataPath, runways, (selectedApproach ?? RunwayComboBox.SelectedItem as Runway)?.ToString(), settings, () =>
            {
                SettingsStore.Save(settings);
                InvalidateViews();
            })
            {
                Owner = owner
            };
            window.ShowDialog();
            if (window.Saved)
            {
                await ReloadRunways();
            }
        }

        /// <summary>
        /// Reads the runway file again after editing and selects the same runway as before, if it still exists.
        /// </summary>
        private async Task ReloadRunways()
        {
            string? currentName = selectedApproach?.RunwayKey;
            double? unpublished = runway.IsUnpublished ? runway.GlideSlope : null;
            try
            {
                runways = await DataFile.GetRunways(dataPath);
                runwayList = GroupRunways(runways);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot read the runway file {dataPath}:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            reloadingRunways = true;
            try
            {
                // New objects read from the file: the runway is selected again below.
                ApplyRunwayFilter(userTyped: false, keepSelection: false);
                Runway? same = runwayList.FirstOrDefault(r => r.RunwayKey == currentName);
                if (same != null)
                {
                    ShowAllIfHidden(same);
                    RunwayComboBox.SelectedItem = same;
                    // An unpublished angle in use stays (modern display), unless the file now has it.
                    if (unpublished is double degrees && !viewOptions.Analog
                        && !approaches.Any(a => Math.Abs(a.GlideSlope - degrees) < 0.005))
                    {
                        SetUnpublishedGlideSlope(degrees);
                    }
                }
            }
            finally
            {
                reloadingRunways = false;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow window = new(settings, ApplyProfile, OpenRunwayEditor, OpenTestTraffic, OpenJoystick)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        /// <summary>
        /// Applies the active profile's settings to the screen.
        /// </summary>
        /// <summary>
        /// Keyboard: arrows tilt the antenna (up/down elevation, left/right azimuth), Home brings it back to neutral.
        /// Ignored while typing in a text field.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox or AptEntry) return;
            // Shift + arrows / Home: decision height; Ctrl + arrows / Home: brightness; Page up / down / End: range.
            // (Same actions as the keys and knobs of the analog console, with the window in front.)
            ModifierKeys modifiers = Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Alt);
            if (modifiers == ModifierKeys.Shift && e.Key is Key.Up or Key.Down or Key.Home)
            {
                SetDecisionHeight(e.Key == Key.Up ? runway.MDH + DecisionHeightStep : e.Key == Key.Down ? runway.MDH - DecisionHeightStep : runway.DefaultMDH);
                e.Handled = true;
                return;
            }
            if (modifiers == ModifierKeys.Control && e.Key is Key.Up or Key.Down or Key.Home)
            {
                if (e.Key == Key.Home) SetBrightness(100); else ChangeBrightness(e.Key == Key.Up ? 1 : -1);
                e.Handled = true;
                return;
            }
            switch (e.Key)
            {
                case Key.PageUp when modifiers == 0: SetRangeIndex(DistanceComboBox.SelectedIndex + 1); break;
                case Key.PageDown when modifiers == 0: SetRangeIndex(DistanceComboBox.SelectedIndex - 1); break;
                case Key.End when modifiers == 0: SetRangeIndex(Ranges.IndexOfClosest(settings.Active.PreferredRange)); break;
                case Key.Up: TiltAntenna(1, 0); break;
                case Key.Down: TiltAntenna(-1, 0); break;
                case Key.Left: TiltAntenna(0, -AzimuthSign); break;
                case Key.Right: TiltAntenna(0, AzimuthSign); break;
                case Key.Home: NeutralAntenna(); break;
                case Key.L: ToggleLabels(); break;
                case Key.A: ToggleDisplayMode(); break;
                case Key.P: SwitchProfile(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); break;
                case Key.T: OpenTestTraffic(); break;
                case Key.C when modifiers == 0: ToggleDistanceScale(); break;
                default: return;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Shows or hides the labels of all tracks, in both views (for this session).
        /// </summary>
        private void ToggleLabels()
        {
            // The analog scope has no labels.
            if (viewOptions.Analog) return;
            viewOptions.ShowLabels = !viewOptions.ShowLabels;
            if (viewOptions.ShowLabels)
            {
                // Showing the labels again also brings back those hidden one by one.
                profileView.ShowAllLabels();
                horizontalView.ShowAllLabels();
            }
            LabelsButton.Content = viewOptions.ShowLabels ? "Hide labels (L)" : "Show labels (L)";
            Redraw();
        }

        private void TiltAntenna(int elevationSteps, int azimuthSteps)
        {
            // Beam off (advanced function not in use): the controls stay, a hint says how to turn it on.
            if (!radar.BeamEnabled)
            {
                FrameworkElement analogTarget = settings.Active.TiltControl switch
                {
                    AnalogTiltControl.Knobs => elevationSteps != 0 ? elevationKnob : azimuthKnob,
                    AnalogTiltControl.Joystick => tiltStick,
                    _ => KnobPanel
                };
                ShowTiltHint(viewOptions.Analog ? analogTarget : TiltPanel);
                return;
            }
            if (radar.Tilt(elevationSteps, azimuthSteps))
            {
                InvalidateViews();
            }
            UpdateKnobs();
        }

        private System.Windows.Controls.ToolTip? tiltHint;
        private System.Windows.Threading.DispatcherTimer? tiltHintTimer;

        /// <summary>Shows for a few seconds, next to the tilt control used, how to turn the antenna tilt on.</summary>
        private void ShowTiltHint(FrameworkElement target)
        {
            tiltHint ??= new System.Windows.Controls.ToolTip
            {
                Content = "Antenna tilt: turn on the antenna beam to use it\n(Settings → Radar → Narrow antenna beam moved by the tilt).",
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };
            if (viewOptions.Analog) tiltHint.Style = AnalogToolTipStyle;
            else tiltHint.ClearValue(StyleProperty);
            tiltHint.IsOpen = false;
            tiltHint.PlacementTarget = target;
            tiltHint.IsOpen = true;
            if (tiltHintTimer == null)
            {
                tiltHintTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                tiltHintTimer.Tick += (s, e) =>
                {
                    tiltHintTimer.Stop();
                    if (tiltHint != null) tiltHint.IsOpen = false;
                };
            }
            tiltHintTimer.Stop();
            tiltHintTimer.Start();
        }

        private void NeutralAntenna()
        {
            if (radar.Neutral())
            {
                InvalidateViews();
            }
            UpdateKnobs();
        }

        private void ApplyProfile()
        {
            Profile profile = settings.Active;
            radar.ApplyProfile(profile);
            ApplyRanges(profile);
            viewOptions.Qfe = profile.PressureReference == PressureReference.QFE;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            viewOptions.Analog = analog;
            viewOptions.EchoGrowth = profile.EchoGrowth;
            consolePanel.Drums = profile.Readouts == ReadoutStyle.Drums;
            aptEntry.Drums = profile.Readouts == ReadoutStyle.Drums;
            viewOptions.Theme = analog
                ? Theme.Analog(ColorText.Parse(profile.AnalogColor, Theme.DefaultPhosphor), BrightnessBoost(profile), profile.Style)
                : Theme.Modern(profile.Style, BrightnessBoost(profile));
            viewOptions.RangeMarks = profile.RangeMarks;
            // Reminders of all the lines (approaches) of the runway.
            viewOptions.Reminders = r => settings.RemindersFor(runways.Where(x => x.RunwayKey == r.RunwayKey).Select(x => x.ToString()).Append(r.ToString()));
            viewOptions.RangeTextBelowHorizon = profile.RangeTextBelowHorizon;
            viewOptions.DistanceKnee = profile.CompressedScale ? profile.CompressionKnee : 0;
            LinButton.Tag = profile.CompressedScale ? "Unlit" : "Lit";
            LogButton.Tag = profile.CompressedScale ? "Lit" : "Unlit";
            phosphorGlow.Color = viewOptions.Theme.Glow;
            // The old scopes had no altitude scale.
            viewOptions.ShowAltitudeScale = profile.ShowAltitudeScale && !analog;
            viewOptions.ShowAltitudeLines = profile.ShowAltitudeLines;
            viewOptions.ScaleInMetres = profile.AltitudeScaleUnit == LengthUnit.Metres;
            viewOptions.HistoryEnabled = profile.HistoryEnabled;
            viewOptions.HistoryDots = profile.HistoryDots;
            viewOptions.HistoryInterval = profile.HistoryInterval;
            viewOptions.TrackSymbol = profile.TrackSymbol;
            viewOptions.ThresholdSymbol = profile.ThresholdSymbol;
            viewOptions.TouchdownSymbol = profile.TouchdownSymbol;
            viewOptions.AntennaSymbol = profile.AntennaSymbol;
            viewOptions.DhSymbol = profile.DhSymbol;
            viewOptions.DhLineSide = profile.DhLineSide;
            viewOptions.DhLineLength = profile.DhLineLength;
            viewOptions.FinalSegment = profile.FinalSegment;
            viewOptions.FinalSegmentDash = profile.FinalSegmentDash;
            viewOptions.DhDropSide = profile.DhDropSide;
            viewOptions.DhDropLength = profile.DhDropLength;
            viewOptions.DhAzimuthLength = profile.DhAzimuthLength;
            viewOptions.DhAzimuthSymbol = profile.DhAzimuthSymbol;
            viewOptions.HistorySymbol = profile.HistorySymbol;
            viewOptions.CoastSymbol = profile.CoastSymbol;
            viewOptions.CoastSeconds = profile.CoastSeconds;
            viewOptions.ShowBeamEdges = profile.ShowBeamEdges;
            viewOptions.Identities.DropAfter = TimeSpan.FromSeconds(Math.Max(10, profile.CoastSeconds + 2));
            viewOptions.ElevationLabel = profile.ElevationLabel;
            viewOptions.Identities.RandomEnabled = profile.RandomTrackIds;
            viewOptions.AzimuthLabel = profile.AzimuthLabel;
            viewOptions.Version++;
            DhLabel.Text = $"{Pressure.Names(profile.MinimaLabel).Height} (ft)";
            bool right = profile.RunwaySide == RunwaySide.Right;
            profileView.SetRunwayOnRight(right);
            horizontalView.SetRunwayOnRight(right);
            // Information area in the top corner on the runway side, away from the far end of the scan limits.
            // Leave room for the altitude scale, drawn on the same side.
            double margin = viewOptions.ShowAltitudeScale && viewOptions.DistanceKnee <= 0 ? 75 : 4;
            ApplyDisplayMode(analog);
            if (right)
            {
                infoPanel.ClearValue(Canvas.LeftProperty);
                Canvas.SetRight(infoPanel, margin);
            }
            else
            {
                infoPanel.ClearValue(Canvas.RightProperty);
                Canvas.SetLeft(infoPanel, margin);
            }
            Canvas.SetTop(infoPanel, 0);
            TextAlignment alignment = right ? TextAlignment.Right : TextAlignment.Left;
            infoText.TextAlignment = alignment;
            courseText.TextAlignment = alignment;
            glidePathText.TextAlignment = alignment;
            missedApproachText.TextAlignment = alignment;
            infoText2.TextAlignment = alignment;
            statusText.TextAlignment = alignment;
            dataText.TextAlignment = alignment;
            tiltText.TextAlignment = alignment;
            InvalidateViews();
        }

        /// <summary>
        /// Keeps trying to (re)connect to Aurora while the window is open.
        /// It runs separately from the refresh timer, so a slow or failing connection attempt
        /// (about 2 s on Windows when Aurora is closed) never delays the drawing.
        /// </summary>
        private async Task ConnectionLoop()
        {
            while (Open)
            {
                if (!aurora.Connected)
                {
                    if (await aurora.TryConnect())
                    {
                        // Ask for the QNH again right after (re)connecting.
                        qnhRefreshNeeded = true;
                    }
                }
                await Task.Delay(ReconnectDelay);
            }
        }

        /// <summary>
        /// Index of the available display distance closest to the given one.
        /// </summary>
        private int IndexOfDistance(double distance)
        {
            return Ranges.IndexOfClosest(distance);
        }

        private void DistanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ( e.AddedItems.Count > 0 && e.AddedItems[0] is Distance d)
            {
                runway.Distance = d;
                // Redraw now with the last known traffic: the range change is immediate.
                InvalidateViews();
                rangeKnob.Index = Math.Max(0, DistanceComboBox.SelectedIndex);
                UpdateAnalogKeys();
            }
        }
        private async void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Runway current = runway;
                List<Aircraft> aircrafts = [];
                if (aurora.Connected)
                {
                    if (qnhRefreshNeeded || current.ICAO != lastQnhIcao || DateTime.UtcNow - lastQnhUpdate >= QnhRefreshInterval)
                    {
                        qnhRefreshNeeded = false;
                        int nqnh = await aurora.GetQNH(current);
                        if (nqnh != 0)
                        {
                            qnh = nqnh;
                        }
                        else if (current.ICAO != lastQnhIcao)
                        {
                            // No METAR for the new airport: do not keep showing the old airport's QNH.
                            qnh = 0;
                        }
                        lastQnhIcao = current.ICAO;
                        lastQnhUpdate = DateTime.UtcNow;
                    }
                    if (DateTime.UtcNow - lastCallsignCheck >= CallsignCheckInterval)
                    {
                        lastCallsignCheck = DateTime.UtcNow;
                        string? own = await aurora.GetConnectedCallsign();
                        connectedCallsign = own;
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            coordinationWindow?.SetCallsign(own);
                            SuggestAirport(own);
                        });
                    }
                    string[] callsigns = await aurora.GetTrafficList();
                    foreach (string callsign in callsigns)
                    {
                        Aircraft? aircraft = await aurora.GetTrafficPosition(callsign);
                        if (aircraft != null)
                        {
                            aircrafts.Add(aircraft);
                        }
                    }
                }
                // Test traffic (key T): virtual aircraft added to the list, also without Aurora.
                if (testTraffic.Count > 0)
                {
                    aircrafts.AddRange(testTraffic.Snapshot(current, DateTime.UtcNow));
                }
                if (!aurora.Connected && connectedCallsign != null)
                {
                    connectedCallsign = null;
                    lastCallsignCheck = DateTime.MinValue;
                    _ = Dispatcher.BeginInvoke(() => coordinationWindow?.SetCallsign(null));
                }
                verticalSpeed.Update(aircrafts, DateTime.UtcNow);
                if (aurora.Connected)
                {
                    // Only the real traffic tells the refresh rate of Aurora.
                    refreshMonitor.Update(aircrafts.Where(a => !a.IsTest).ToList(), DateTime.UtcNow);
                    dataInterval = refreshMonitor.IntervalSeconds;
                }
                else
                {
                    refreshMonitor.Reset();
                    dataInterval = double.NaN;
                }
                lastAircrafts = aircrafts;
                Draw();
            }
            catch (Exception)
            {
                // Never let a single failed refresh crash the program; the next refresh will try again.
            }
            finally
            {
                if (Open)
                {
                    timer.Start();
                }
            }
        }
        /// <summary>
        /// Redraw from the refresh timer's thread.
        /// </summary>
        private void Draw()
        {
            if (!Open) return;
            Dispatcher.Invoke(Redraw);
        }
        /// <summary>
        /// Rebuilds the static parts of both views (after a change of runway, range, size or settings) and redraws.
        /// </summary>
        private void InvalidateViews()
        {
            profileView.Invalidate();
            horizontalView.Invalidate();
            Redraw();
        }
        /// <summary>
        /// Updates both views with the last known traffic. Must be called on the window's (UI) thread.
        /// Only what changed is redrawn: static elements are rebuilt only after <see cref="InvalidateViews"/>.
        /// </summary>
        private void Redraw()
        {
            if (!Open) return;
            List<Aircraft> aircrafts = lastAircrafts;
            UpdateTestTrafficSign();
            // Beam centre that follows the glide path: a new approach (angle) moves it.
            if (radar.GlidePathAngle != runway.GlideSlope)
            {
                radar.GlidePathAngle = runway.GlideSlope;
                if (radar.NeutralFollowsGlidePath)
                {
                    radar.ClampTilt();
                    profileView.Invalidate();
                    horizontalView.Invalidate();
                    UpdateKnobs();
                }
            }
            // Track filter (modern display): smoothed positions of the tracks at this moment.
            trackFilter.Apply(aircrafts, DateTime.UtcNow, viewOptions.Analog ? TrackSmoothing.Off : settings.Active.TrackSmoothing);
            // Fictitious IDs: given to the tracks inside the scan.
            viewOptions.Identities.Update(aircrafts.Where(a => radar.IsSeen(a, runway)).Select(a => a.Callsign), DateTime.UtcNow);
            UpdateInfo();
            profileView.Render(aircrafts);
            horizontalView.Render(aircrafts);
        }
        private static void SetText(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
        }

        private void UpdateInfo()
        {
            if (viewOptions.Analog)
            {
                UpdateKnobs();
                aptEntry.Icao = runway.ICAO;
            }
            else if (!IcaoFilterBox.IsKeyboardFocusWithin && IcaoFilterBox.Text != runway.ICAO)
            {
                SetFilterText(runway.ICAO);
            }
            Profile profile = settings.Active;
            bool qfe = profile.PressureReference == PressureReference.QFE;
            string pressure = qnh == 0 ? "----" : Pressure.Format(qfe ? Pressure.QfeFromQnh(qnh, runway.Elevation) : qnh, profile.PressureUnit);
            (string altitudeName, string heightName) = Pressure.Names(profile.MinimaLabel);
            string minimum = qfe
                ? $"{heightName} {FormatHeight(runway.MDH)} ft"
                : $"{altitudeName} {FormatHeight(runway.MDH + runway.Elevation)} ft";
            string course = runway.FinalCourse(profile.MagneticVariation).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            string glidePath = Runway.FormatGlideSlope(runway.GlideSlope);
            string missedApproach = runway.MissedApproachPointText();
            // Texts changed only when different, so an open tooltip is not disturbed by the refresh.
            SetText(infoText, $"RWY {runway.BaseDesignator}");
            SetText(courseText, $"CRS {course}");
            bool unpublished = runway.IsUnpublished;
            SetText(glidePathText, unpublished ? $"GP {glidePath}° UNPUBLISHED APPROACH" : $"GP {glidePath}°");
            SetText(missedApproachText, $"MAPt DIST {missedApproach}{(missedApproach == "----" ? "" : " NM")}");
            Brush highlight = unpublished ? Brushes.Orange : Brushes.White;
            FontWeight weight = unpublished ? FontWeights.Bold : FontWeights.Normal;
            foreach (TextBlock block in new[] { glidePathText, missedApproachText })
            {
                if (block.Foreground != highlight) block.Foreground = highlight;
                if (block.FontWeight != weight) block.FontWeight = weight;
            }
            SetText(infoText2, $"{(qfe ? "QFE" : "QNH")} {pressure}\n{minimum}");
            if (viewOptions.Analog)
            {
                consolePanel.Update(new ConsoleData(
                    runway.ICAO,
                    runway.BaseDesignator,
                    course,
                    qfe ? "QFE" : "QNH",
                    pressure,
                    qfe ? heightName : altitudeName,
                    FormatHeight(qfe ? runway.MDH : runway.MDH + runway.Elevation),
                    runway.GlideSlope,
                    missedApproach,
                    runway.Distance,
                    radar.TiltElevation,
                    radar.TiltAzimuth,
                    profile.AzimuthTiltSwapped,
                    aurora.Connected,
                    dataInterval,
                    !double.IsNaN(dataInterval) && dataInterval > MaxGoodDataInterval));
            }
            // Antenna tilt: shown only when not neutral, so the controller does not forget it.
            if (radar.IsNeutral)
            {
                tiltText.Visibility = Visibility.Collapsed;
            }
            else
            {
                List<string> parts = [];
                if (radar.TiltElevation != 0)
                {
                    parts.Add($"EL TILT {Math.Abs(radar.TiltElevation).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {(radar.TiltElevation > 0 ? "UP" : "DN")}");
                }
                if (radar.TiltAzimuth != 0)
                {
                    double shown = radar.TiltAzimuth * AzimuthSign;
                    parts.Add($"AZ TILT {Math.Abs(shown).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {(shown > 0 ? "R" : "L")}");
                }
                tiltText.Text = string.Join("\n", parts);
                tiltText.Visibility = Visibility.Visible;
            }
            if (aurora.Connected)
            {
                auroraEverConnected = true;
                statusText.Text = "STS OK";
                statusText.Foreground = Brushes.Green;
            }
            else
            {
                statusText.Text = "STS FAIL";
                statusText.Foreground = Brushes.Red;
            }
            // Measured update interval of the traffic (shown when there is moving traffic to measure it).
            double interval = dataInterval;
            if (double.IsNaN(interval))
            {
                dataText.Visibility = Visibility.Collapsed;
            }
            else
            {
                dataText.Visibility = Visibility.Visible;
                string seconds = interval.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                if (interval > MaxGoodDataInterval)
                {
                    dataText.Text = $"DATA {seconds}s - SET AURORA TRAFFIC REFRESH TO 0.5s";
                    dataText.Foreground = Brushes.Red;
                }
                else
                {
                    dataText.Text = $"DATA {seconds}s";
                    dataText.Foreground = Brushes.Green;
                }
            }
        }
        private void Window_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            int i = DistanceComboBox.SelectedIndex;
            if (e.Delta > 0)
            {
                if((i+1) < DistanceComboBox.Items.Count)
                {
                    DistanceComboBox.SelectedIndex = i + 1;
                }
            } else
            {
                if((i-1) >= 0)
                {
                    DistanceComboBox.SelectedIndex = i - 1;
                }
            }
        }
    }
}
