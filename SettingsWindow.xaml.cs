using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AuroraPAR
{
    /// <summary>
    /// Profile management and settings. Every change is applied to the screen and saved immediately.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings settings;
        private readonly Action applyToScreen;
        /// <summary>
        /// True while the controls are being filled from the settings, so their change events are ignored.
        /// </summary>
        private bool refreshing;
        /// <summary>
        /// Actions that fill the number fields from the active profile.
        /// </summary>
        private readonly List<Action> refreshers = [];

        internal SettingsWindow(AppSettings settings, Action applyToScreen, Action<Window>? editRunways = null, Action? openTestTraffic = null,
            Action<Window>? openJoystick = null)
        {
            InitializeComponent();
            // Never larger than the screen (each section scrolls if needed), and its top edge on the screen.
            Rect workArea = SystemParameters.WorkArea;
            Width = Math.Min(Width, workArea.Width);
            Height = Math.Min(Height, workArea.Height);
            Loaded += (s, e) =>
            {
                Rect area = SystemParameters.WorkArea;
                if (Top + ActualHeight > area.Bottom) Top = area.Bottom - ActualHeight;
                if (Top < area.Top) Top = area.Top;
                if (Left + ActualWidth > area.Right) Left = area.Right - ActualWidth;
                if (Left < area.Left) Left = area.Left;
            };
            this.settings = settings;
            this.applyToScreen = applyToScreen;
            EditRunwaysButton.Visibility = editRunways == null ? Visibility.Collapsed : Visibility.Visible;
            EditRunwaysButton.Click += (s, e) => editRunways?.Invoke(this);
            TestTrafficButton.Visibility = openTestTraffic == null ? Visibility.Collapsed : Visibility.Visible;
            TestTrafficButton.Click += (s, e) =>
            {
                // The settings are modal: closed first, then the test traffic window opens beside the radar.
                Close();
                if (openTestTraffic != null) Dispatcher.BeginInvoke(openTestTraffic);
            };
            JoystickButton.Visibility = openJoystick == null ? Visibility.Collapsed : Visibility.Visible;
            JoystickButton.Click += (s, e) => openJoystick?.Invoke(this);
            ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
            RenameButton.Click += (s, e) => RenameProfile();
            ProfileNameTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) RenameProfile();
            };
            NewButton.Click += (s, e) => DuplicateProfile();
            DeleteButton.Click += (s, e) => DeleteProfile();
            ImportButton.Click += (s, e) => ImportProfile();
            ExportButton.Click += (s, e) => ExportProfile();
            ReloadBundledButton.Click += (s, e) => ReloadBundledProfiles();
            ModernRadio.Checked += (s, e) => SetProfileValue(p => p.DisplayMode == DisplayMode.Modern, p => p.DisplayMode = DisplayMode.Modern);
            AnalogRadio.Checked += (s, e) => SetProfileValue(p => p.DisplayMode == DisplayMode.Analog, p => p.DisplayMode = DisplayMode.Analog);
            RangeMilesCheck.Checked += (s, e) => SetProfileValue(p => p.RangeDefaultMiles, p => p.RangeDefaultMiles = true);
            RangeMilesCheck.Unchecked += (s, e) => SetProfileValue(p => !p.RangeDefaultMiles, p => p.RangeDefaultMiles = false);
            LockModeCheck.Checked += (s, e) => SetProfileValue(p => p.LockDisplayMode, p => p.LockDisplayMode = true);
            LockModeCheck.Unchecked += (s, e) => SetProfileValue(p => !p.LockDisplayMode, p => p.LockDisplayMode = false);
            DhSideComboBox.ItemsSource = new[] { "left", "right", "both", "none" };
            DhSideComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DhSideComboBox.SelectedIndex < 0) return;
                DhHorizontalSide value = (DhHorizontalSide)DhSideComboBox.SelectedIndex;
                SetProfileValue(p => p.DhLineSide == value, p => p.DhLineSide = value);
            };
            DhLengthComboBox.ItemsSource = DhLengths.Select(l => l == 0 ? "to the edge of the view" : FormatNumber(l) + " NM").ToList();
            DhLengthComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DhLengthComboBox.SelectedIndex < 0) return;
                double value = DhLengths[DhLengthComboBox.SelectedIndex];
                SetProfileValue(p => p.DhLineLength == value, p => p.DhLineLength = value);
            };
            DhAzimuthLengthComboBox.ItemsSource = DhAzimuthLengths.Select(l => l == 0 ? "up to the scan limits" : FormatNumber(l) + " m each side").ToList();
            DhAzimuthLengthComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DhAzimuthLengthComboBox.SelectedIndex < 0) return;
                double value = DhAzimuthLengths[DhAzimuthLengthComboBox.SelectedIndex];
                SetProfileValue(p => p.DhAzimuthLength == value, p => p.DhAzimuthLength = value);
            };
            DistanceScaleComboBox.ItemsSource = CompressionKnees.Select(k => $"knee {FormatNumber(k)} NM").ToList();
            DistanceScaleComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DistanceScaleComboBox.SelectedIndex < 0) return;
                double knee = CompressionKnees[DistanceScaleComboBox.SelectedIndex];
                SetProfileValue(p => p.CompressionKnee == knee, p => p.CompressionKnee = knee);
            };
            FinalSegmentComboBox.ItemsSource = new[] { "solid to the runway", "dashed from the DH", "dashed from the threshold" };
            FinalSegmentComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || FinalSegmentComboBox.SelectedIndex < 0) return;
                FinalSegment value = (FinalSegment)FinalSegmentComboBox.SelectedIndex;
                SetProfileValue(p => p.FinalSegment == value, p => p.FinalSegment = value);
            };
            FinalDashComboBox.ItemsSource = new[] { "dashed", "dotted" };
            FinalDashComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || FinalDashComboBox.SelectedIndex < 0) return;
                LineDash value = FinalDashComboBox.SelectedIndex == 0 ? LineDash.Dashed : LineDash.Dotted;
                SetProfileValue(p => p.FinalSegmentDash == value, p => p.FinalSegmentDash = value);
            };
            DhDropSideComboBox.ItemsSource = new[] { "down", "up", "both", "none", "full height" };
            DhDropSideComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DhDropSideComboBox.SelectedIndex < 0) return;
                DhVerticalSide value = (DhVerticalSide)DhDropSideComboBox.SelectedIndex;
                SetProfileValue(p => p.DhDropSide == value, p => p.DhDropSide = value);
            };
            DhDropLengthComboBox.ItemsSource = DhDropLengths.Select(l => l == 0 ? "as the DH (to ground)" : FormatNumber(l) + " ft").ToList();
            DhDropLengthComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DhDropLengthComboBox.SelectedIndex < 0) return;
                double value = DhDropLengths[DhDropLengthComboBox.SelectedIndex];
                SetProfileValue(p => p.DhDropLength == value, p => p.DhDropLength = value);
            };
            LabelPlacementComboBox.ItemsSource = new[] { "45° up right (line)", "45° up left (line)", "45° down right (line)", "45° down left (line)", "Next to the track" };
            LabelPlacementComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || LabelPlacementComboBox.SelectedIndex < 0) return;
                LabelPlacement value = (LabelPlacement)LabelPlacementComboBox.SelectedIndex;
                SetProfileValue(p => p.LabelPlacement == value, p => p.LabelPlacement = value);
                LabelOffsetComboBox.IsEnabled = value != LabelPlacement.NextToTrack;
            };
            List<double> labelOffsets = [15, 20, 25, 30, 40, 50, 60, 80, 100, 120];
            LabelOffsetComboBox.ItemsSource = labelOffsets.Select(px => $"{px} px").ToList();
            LabelOffsetComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || LabelOffsetComboBox.SelectedIndex < 0) return;
                double value = labelOffsets[LabelOffsetComboBox.SelectedIndex];
                SetProfileValue(p => p.LabelOffsetDistance == value, p => p.LabelOffsetDistance = value);
            };
            refreshers.Add(() =>
            {
                LabelPlacementComboBox.SelectedIndex = (int)Active.LabelPlacement;
                int nearest = labelOffsets.Select((px, i) => (Math.Abs(px - Active.LabelOffsetDistance), i)).Min().i;
                LabelOffsetComboBox.SelectedIndex = nearest;
                LabelOffsetComboBox.IsEnabled = Active.LabelPlacement != LabelPlacement.NextToTrack;
            });
            ShowCourseCheck.Checked += (s, e) => SetProfileValue(p => p.ShowCourseSelector, p => p.ShowCourseSelector = true);
            ShowCourseCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowCourseSelector, p => p.ShowCourseSelector = false);
            ShowDhCheck.Checked += (s, e) => SetProfileValue(p => p.ShowDhSelector, p => p.ShowDhSelector = true);
            RainClutterCheck.Checked += (s, e) => SetProfileValue(p => p.RainClutter, p => p.RainClutter = true);
            RainClutterCheck.Unchecked += (s, e) => SetProfileValue(p => !p.RainClutter, p => p.RainClutter = false);
            ShowDhCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowDhSelector, p => p.ShowDhSelector = false);
            RunwayLeftRadio.Checked += (s, e) => SetRunwaySide(RunwaySide.Left);
            RunwayRightRadio.Checked += (s, e) => SetRunwaySide(RunwaySide.Right);
            foreach (double value in Ranges.All)
            {
                double range = value;
                CheckBox check = new() { Content = range.ToString(System.Globalization.CultureInfo.InvariantCulture), Tag = range, Margin = new Thickness(0, 0, 14, 4) };
                check.Checked += (s, e) => SetRangeOffered(range, true);
                check.Unchecked += (s, e) => SetRangeOffered(range, false);
                RangeChecksPanel.Children.Add(check);
            }
            StartRangeLastRadio.Checked += (s, e) => SetStartupRange(StartupRange.LastUsed);
            StartRangeRunwayRadio.Checked += (s, e) => SetStartupRange(StartupRange.RunwayDefault);
            StartRangeFixedRadio.Checked += (s, e) => SetStartupRange(StartupRange.Fixed);
            ChangeRangeKeepRadio.Checked += (s, e) => SetRunwayChangeRange(RunwayChangeRange.KeepCurrent);
            ChangeRangeRunwayRadio.Checked += (s, e) => SetRunwayChangeRange(RunwayChangeRange.RunwayDefault);
            ChangeRangeFixedRadio.Checked += (s, e) => SetRunwayChangeRange(RunwayChangeRange.Fixed);
            FixedRangeComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || FixedRangeComboBox.SelectedItem is not double range) return;
                Active.PreferredRange = range;
                Commit();
            };
            CloseButton.Click += (s, e) => Close();
            ScanEffectCheck.Checked += (s, e) => SetProfileValue(p => p.ScanEffect, p => p.ScanEffect = true);
            ScanEffectCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ScanEffect, p => p.ScanEffect = false);
            ScanEffectSpeedComboBox.ItemsSource = Enum.GetValues<ScanEffectSpeed>().Select(ScanEffect.DisplayName).ToList();
            ScanEffectSpeedComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || ScanEffectSpeedComboBox.SelectedIndex < 0) return;
                ScanEffectSpeed speed = Enum.GetValues<ScanEffectSpeed>()[ScanEffectSpeedComboBox.SelectedIndex];
                SetProfileValue(p => p.ScanEffectSpeed == speed, p => p.ScanEffectSpeed = speed);
            };
            BuildAnalogControlFields();
            SmoothingComboBox.ItemsSource = new List<string> { "Off", "On" };
            SmoothingComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || SmoothingComboBox.SelectedIndex < 0) return;
                TrackSmoothing smoothing = SmoothingComboBox.SelectedIndex == 0 ? TrackSmoothing.Off : TrackSmoothing.Light;
                SetProfileValue(p => p.TrackSmoothing == smoothing, p => p.TrackSmoothing = smoothing);
            };
            BuildRadarFields();
            BuildSymbolRows();
            HistoryCheck.Checked += (s, e) => SetProfileValue(p => p.HistoryEnabled, p => p.HistoryEnabled = true);
            HistoryCheck.Unchecked += (s, e) => SetProfileValue(p => !p.HistoryEnabled, p => p.HistoryEnabled = false);
            HistoryDotsBox.LostFocus += (s, e) => ApplyHistoryDots();
            HistoryIntervalBox.LostFocus += (s, e) => ApplyHistoryInterval();
            HistoryIntervalBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyHistoryInterval();
            };
            MagVarBox.LostFocus += (s, e) => ApplyMagneticVariation();
            MagVarBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyMagneticVariation();
            };
            HistoryDotsBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyHistoryDots();
            };
            EditLabelsButton.Click += (s, e) =>
            {
                LabelEditorWindow editor = new(settings, Commit) { Owner = this };
                editor.ShowDialog();
            };
            QnhRadio.Checked += (s, e) => SetProfileValue(p => p.PressureReference == PressureReference.QNH, p => p.PressureReference = PressureReference.QNH);
            QfeRadio.Checked += (s, e) => SetProfileValue(p => p.PressureReference == PressureReference.QFE, p => p.PressureReference = PressureReference.QFE);
            HpaRadio.Checked += (s, e) => SetProfileValue(p => p.PressureUnit == PressureUnit.HectoPascal, p => p.PressureUnit = PressureUnit.HectoPascal);
            InHgRadio.Checked += (s, e) => SetProfileValue(p => p.PressureUnit == PressureUnit.InchesOfMercury, p => p.PressureUnit = PressureUnit.InchesOfMercury);
            DaDhRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.DaDh, p => p.MinimaLabel = MinimaLabel.DaDh);
            OcaOchRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.OcaOch, p => p.MinimaLabel = MinimaLabel.OcaOch);
            MdaMdhRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.MdaMdh, p => p.MinimaLabel = MinimaLabel.MdaMdh);
            LateralScaleCheck.Checked += (s, e) => SetProfileValue(p => p.ShowLateralScale, p => p.ShowLateralScale = true);
            LateralScaleCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowLateralScale, p => p.ShowLateralScale = false);
            AltitudeScaleCheck.Checked += (s, e) => SetProfileValue(p => p.ShowAltitudeScale, p => p.ShowAltitudeScale = true);
            AltitudeScaleCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowAltitudeScale, p => p.ShowAltitudeScale = false);
            AltitudeLinesInComboBox.ItemsSource = new[] { "in the LOG scale only", "in the LIN scale only", "in both scales" };
            AltitudeLinesInComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || AltitudeLinesInComboBox.SelectedIndex < 0) return;
                AltitudeLinesScale value = (AltitudeLinesScale)AltitudeLinesInComboBox.SelectedIndex;
                SetProfileValue(p => p.AltitudeLinesIn == value, p => p.AltitudeLinesIn = value);
            };
            AltitudeLinesCheck.Checked += (s, e) => SetProfileValue(p => p.ShowAltitudeLines, p => p.ShowAltitudeLines = true);
            AltitudeLinesCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowAltitudeLines, p => p.ShowAltitudeLines = false);
            DistanceNmRadio.Checked += (s, e) => SetProfileValue(p => p.DistanceUnit == DistanceUnit.NauticalMiles, p => p.DistanceUnit = DistanceUnit.NauticalMiles);
            DistanceKmRadio.Checked += (s, e) => SetProfileValue(p => p.DistanceUnit == DistanceUnit.Kilometres, p => p.DistanceUnit = DistanceUnit.Kilometres);
            FapLineCheck.Checked += (s, e) => SetProfileValue(p => p.ShowFapLine, p => p.ShowFapLine = true);
            FapLineCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowFapLine, p => p.ShowFapLine = false);
            CompactLabelsCheck.Checked += (s, e) => SetProfileValue(p => p.CompactLabels && !p.NumbersOnlyLabels, p => { p.CompactLabels = true; p.NumbersOnlyLabels = false; });
            NumbersOnlyLabelsCheck.Checked += (s, e) => SetProfileValue(p => p.NumbersOnlyLabels && !p.CompactLabels, p => { p.NumbersOnlyLabels = true; p.CompactLabels = false; });
            NumbersOnlyLabelsCheck.Unchecked += (s, e) => SetProfileValue(p => !p.NumbersOnlyLabels, p => p.NumbersOnlyLabels = false);
            CompactLabelsCheck.Unchecked += (s, e) => SetProfileValue(p => !p.CompactLabels, p => p.CompactLabels = false);
            FixedHeightsBox.LostFocus += (s, e) => ApplyFixedHeights();
            FixedHeightsBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyFixedHeights();
            };
            ScaleFeetRadio.Checked += (s, e) => SetProfileValue(p => p.AltitudeScaleUnit == LengthUnit.Feet, p => p.AltitudeScaleUnit = LengthUnit.Feet);
            ScaleMetresRadio.Checked += (s, e) => SetProfileValue(p => p.AltitudeScaleUnit == LengthUnit.Metres, p => p.AltitudeScaleUnit = LengthUnit.Metres);
            FileLocationText.Text = $"Settings file{(SettingsStore.IsPortable ? " (portable mode)" : "")}: {SettingsStore.FilePath}";
            FileLocationText.ToolTip = FileLocationText.Text;
            // Sections: the menu on the left shows one at a time (the last one chosen, during the session).
            SectionList.SelectionChanged += (s, e) => ShowSection(SectionList.SelectedIndex);
            SectionList.SelectedIndex = Math.Clamp(lastSection, 0, SectionList.Items.Count - 1);
            AdvancedCheck.IsChecked = settings.ShowAdvancedSettings;
            AdvancedCheck.Checked += (s, e) => SetAdvanced(true);
            AdvancedCheck.Unchecked += (s, e) => SetAdvanced(false);
            RefreshControls();
            ApplyAdvanced();
        }

        /// <summary>Section shown last (kept while the program runs).</summary>
        private static int lastSection;

        private void ShowSection(int index)
        {
            if (index < 0) return;
            lastSection = index;
            for (int i = 0; i < SectionHost.Children.Count; i++)
            {
                SectionHost.Children[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>Marks the options shown only with "Show advanced" (Tag = <see cref="AdvancedTag"/>).</summary>
        private const string AdvancedTag = "Advanced";

        private void SetAdvanced(bool show)
        {
            if (settings.ShowAdvancedSettings == show) return;
            settings.ShowAdvancedSettings = show;
            SettingsStore.Save(settings);
            ApplyAdvanced();
        }

        /// <summary>Shows or hides the advanced options of all the sections (they keep their values).</summary>
        private void ApplyAdvanced()
        {
            Visibility visibility = settings.ShowAdvancedSettings ? Visibility.Visible : Visibility.Collapsed;
            void Walk(object node)
            {
                if (node is FrameworkElement element && AdvancedTag.Equals(element.Tag)) element.Visibility = visibility;
                if (node is DependencyObject parent)
                {
                    foreach (object child in LogicalTreeHelper.GetChildren(parent)) Walk(child);
                }
            }
            Walk(SectionHost);
        }

        /// <summary>Range marks, colours and reminders (the parts of a <see cref="DisplayStylePanel"/>), for this profile.</summary>
        private Profile? styleProfile;
        /// <summary>Distance unit of the range marks table shown (it is rebuilt when the unit changes).</summary>
        private DistanceUnit styleUnit;

        private void HostStyleParts()
        {
            if (ReferenceEquals(styleProfile, Active) && styleUnit == Active.DistanceUnit) return;
            styleProfile = Active;
            styleUnit = Active.DistanceUnit;
            (UIElement rangeMarks, UIElement colours, UIElement reminders) = new DisplayStylePanel(settings, Commit).TakeParts();
            RangeMarksHost.Content = rangeMarks;
            ColoursHost.Content = colours;
            ReminderSectionHost.Content = reminders;
            ApplyAdvanced();
        }

        private Profile Active => settings.Active;

        /// <summary>
        /// Fills the controls from the active profile.
        /// </summary>
        private void RefreshControls()
        {
            HostStyleParts();
            refreshing = true;
            try
            {
                ProfileComboBox.ItemsSource = settings.Profiles.Select(p => p.Name).ToList();
                ProfileComboBox.SelectedItem = Active.Name;
                ProfileNameTextBox.Text = Active.Name;
                DeleteButton.IsEnabled = settings.Profiles.Count > 1;
                ModernRadio.IsChecked = Active.DisplayMode == DisplayMode.Modern;
                AnalogRadio.IsChecked = Active.DisplayMode == DisplayMode.Analog;
                LockModeCheck.IsChecked = Active.LockDisplayMode;
                ShowDhCheck.IsChecked = Active.ShowDhSelector;
                ShowCourseCheck.IsChecked = Active.ShowCourseSelector;
                RainClutterCheck.IsChecked = Active.RainClutter;
                DhSideComboBox.SelectedIndex = (int)Active.DhLineSide;
                DhLengthComboBox.SelectedIndex = Array.FindIndex(DhLengths, l => Math.Abs(l - Active.DhLineLength) < 0.01);
                DistanceScaleComboBox.SelectedIndex = SelectedScaleIndex();
                FinalSegmentComboBox.SelectedIndex = (int)Active.FinalSegment;
                FinalDashComboBox.SelectedIndex = Active.FinalSegmentDash == LineDash.Dotted ? 1 : 0;
                FinalDashComboBox.IsEnabled = Active.FinalSegment != FinalSegment.Solid;
                // Shown only when the glide path is drawn solid (Colours and lines).
                FinalSegmentPanel.Visibility = Active.Style.Get(StyleElement.GlidePath).Dash == LineDash.Solid ? Visibility.Visible : Visibility.Collapsed;
                DhAzimuthLengthComboBox.SelectedIndex = Array.FindIndex(DhAzimuthLengths, l => Math.Abs(l - Active.DhAzimuthLength) < 0.5);
                DhDropSideComboBox.SelectedIndex = (int)Active.DhDropSide;
                DhDropLengthComboBox.SelectedIndex = Array.FindIndex(DhDropLengths, l => Math.Abs(l - Active.DhDropLength) < 0.5);
                RangeControlComboBox.SelectedIndex = (int)Active.RangeControl;
                if (!RangeKeyTextBox.IsKeyboardFocused) RangeKeyTextBox.Text = Active.RangeDefaultKey;
                RangeMilesCheck.IsChecked = Active.RangeDefaultMiles;
                RangeKeyTextBox.IsEnabled = !Active.RangeDefaultMiles;
                TiltControlComboBox.SelectedIndex = (int)Active.TiltControl;
                DhControlComboBox.SelectedIndex = (int)Active.DhControl;
                BrightnessControlComboBox.SelectedIndex = (int)Active.BrightnessControl;
                ReadoutsComboBox.SelectedIndex = (int)Active.Readouts;
                KeyStyleComboBox.SelectedIndex = (int)Active.KeyStyle;
                RunwayLeftRadio.IsChecked = Active.RunwaySide == RunwaySide.Left;
                RunwayRightRadio.IsChecked = Active.RunwaySide == RunwaySide.Right;
                StartRangeLastRadio.IsChecked = Active.StartupRange == StartupRange.LastUsed;
                StartRangeRunwayRadio.IsChecked = Active.StartupRange == StartupRange.RunwayDefault;
                StartRangeFixedRadio.IsChecked = Active.StartupRange == StartupRange.Fixed;
                ChangeRangeKeepRadio.IsChecked = Active.RunwayChangeRange == RunwayChangeRange.KeepCurrent;
                ChangeRangeRunwayRadio.IsChecked = Active.RunwayChangeRange == RunwayChangeRange.RunwayDefault;
                ChangeRangeFixedRadio.IsChecked = Active.RunwayChangeRange == RunwayChangeRange.Fixed;
                double[] offered = Ranges.Of(Active.DisplayRanges);
                foreach (CheckBox check in RangeChecksPanel.Children.OfType<CheckBox>())
                {
                    check.IsChecked = offered.Contains((double)check.Tag);
                }
                FixedRangeComboBox.ItemsSource = offered;
                FixedRangeComboBox.SelectedIndex = Ranges.IndexOfClosest(offered, Active.PreferredRange);
                QnhRadio.IsChecked = Active.PressureReference == PressureReference.QNH;
                QfeRadio.IsChecked = Active.PressureReference == PressureReference.QFE;
                HpaRadio.IsChecked = Active.PressureUnit == PressureUnit.HectoPascal;
                InHgRadio.IsChecked = Active.PressureUnit == PressureUnit.InchesOfMercury;
                DaDhRadio.IsChecked = Active.MinimaLabel == MinimaLabel.DaDh;
                OcaOchRadio.IsChecked = Active.MinimaLabel == MinimaLabel.OcaOch;
                MdaMdhRadio.IsChecked = Active.MinimaLabel == MinimaLabel.MdaMdh;
                AltitudeScaleCheck.IsChecked = Active.ShowAltitudeScale;
                LateralScaleCheck.IsChecked = Active.ShowLateralScale;
                AltitudeLinesCheck.IsChecked = Active.ShowAltitudeLines;
                AltitudeLinesInComboBox.SelectedIndex = (int)Active.AltitudeLinesIn;
                AltitudeLinesInComboBox.IsEnabled = Active.ShowAltitudeLines;
                DistanceNmRadio.IsChecked = Active.DistanceUnit == DistanceUnit.NauticalMiles;
                DistanceKmRadio.IsChecked = Active.DistanceUnit == DistanceUnit.Kilometres;
                CompactLabelsCheck.IsChecked = Active.CompactLabels;
                NumbersOnlyLabelsCheck.IsChecked = Active.NumbersOnlyLabels;
                FapLineCheck.IsChecked = Active.ShowFapLine;
                bool metres = Active.AltitudeScaleUnit == LengthUnit.Metres;
                FixedHeightsUnit.Text = metres ? "m" : "ft";
                if (!FixedHeightsBox.IsKeyboardFocused)
                {
                    FixedHeightsBox.Text = FixedHeightsText(Active);
                }
                FixedHeightsNote.Text = "e.g. 600 or 300, 600";
                FixedHeightsNote.Foreground = Brushes.Gray;
                ScaleFeetRadio.IsChecked = Active.AltitudeScaleUnit == LengthUnit.Feet;
                ScaleMetresRadio.IsChecked = Active.AltitudeScaleUnit == LengthUnit.Metres;
                HistoryCheck.IsChecked = Active.HistoryEnabled;
                SmoothingComboBox.SelectedIndex = Active.TrackSmoothing == TrackSmoothing.Off ? 0 : 1;
                ScanEffectCheck.IsChecked = Active.ScanEffect;
                ScanEffectSpeedComboBox.SelectedIndex = Array.IndexOf(Enum.GetValues<ScanEffectSpeed>(), Active.ScanEffectSpeed);
                HistoryDotsBox.Text = Active.HistoryDots.ToString(CultureInfo.InvariantCulture);
                HistoryIntervalBox.Text = Active.HistoryInterval.ToString("0.#", CultureInfo.InvariantCulture);
                MagVarBox.Text = MagneticVariation.Format(Active.MagneticVariation);
                MagVarNote.Text = "e.g. 3E or 2W";
                MagVarNote.Foreground = Brushes.Gray;
                foreach (Action refresh in refreshers)
                {
                    refresh();
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        /// <summary>
        /// Saves the settings, applies them to the screen and refreshes the controls.
        /// </summary>
        private void Commit()
        {
            string? error = SettingsStore.Save(settings);
            if (error != null)
            {
                MessageBox.Show(this, $"Cannot save the settings file {SettingsStore.FilePath}:\n{error}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            applyToScreen();
            RefreshControls();
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (refreshing || ProfileComboBox.SelectedItem is not string name) return;
            settings.ActiveProfile = name;
            Commit();
        }

        private void RenameProfile()
        {
            string name = ProfileNameTextBox.Text.Trim();
            if (name == Active.Name) return;
            if (name.Length == 0)
            {
                MessageBox.Show(this, "The profile name cannot be empty.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (settings.Profiles.Any(p => p != Active && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"A profile named \"{name}\" already exists.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Active.Name = name;
            settings.ActiveProfile = name;
            Commit();
        }

        private void DuplicateProfile()
        {
            Profile copy = Active.Clone();
            copy.Name = settings.UniqueName(Active.Name + " copy");
            settings.Profiles.Add(copy);
            settings.ActiveProfile = copy.Name;
            Commit();
        }

        private void DeleteProfile()
        {
            if (settings.Profiles.Count <= 1) return;
            if (MessageBox.Show(this, $"Delete the profile \"{Active.Name}\"?", "Aurora PAR", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
            settings.Profiles.Remove(Active);
            settings.ActiveProfile = settings.Profiles[0].Name;
            Commit();
        }

        private void ImportProfile()
        {
            OpenFileDialog dialog = new()
            {
                Title = "Import profile",
                Filter = "AuroraPAR profile (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                Profile profile = SettingsStore.ImportProfile(dialog.FileName);
                profile.Name = settings.UniqueName(profile.Name.Trim());
                settings.Profiles.Add(profile);
                settings.ActiveProfile = profile.Name;
                Commit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot import the profile:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// The profiles that come with the program again: same name (any case) = replaced in place, the others added.
        /// The user's own profiles and all the other settings stay as they are; the active profile keeps its name.
        /// </summary>
        private void ReloadBundledProfiles()
        {
            List<Profile> bundled = SettingsStore.ReadBundledProfiles();
            if (bundled.Count == 0)
            {
                MessageBox.Show(this, $"No profiles found in {SettingsStore.BundledProfilesPath}.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            List<string> replaced = [];
            List<string> added = [];
            foreach (Profile profile in bundled)
            {
                (settings.Profiles.Any(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)) ? replaced : added).Add(profile.Name);
            }
            string text = "Reload the profiles that come with the program?\n\n";
            if (replaced.Count > 0) text += "Replaced (your changes to them are lost):\n  " + string.Join("\n  ", replaced) + "\n\n";
            if (added.Count > 0) text += "Added:\n  " + string.Join("\n  ", added) + "\n\n";
            text += "Your other profiles and all the other settings are not changed.";
            if (MessageBox.Show(this, text, "Reload bundled profiles", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            string activeName = settings.ActiveProfile;
            foreach (Profile profile in bundled)
            {
                int index = settings.Profiles.FindIndex(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    if (string.Equals(settings.Profiles[index].Name, activeName, StringComparison.OrdinalIgnoreCase)) activeName = profile.Name;
                    settings.Profiles[index] = profile;
                }
                else
                {
                    settings.Profiles.Add(profile);
                }
            }
            settings.ActiveProfile = activeName;
            Commit();
        }

        private void ExportProfile()
        {
            SaveFileDialog dialog = new()
            {
                Title = "Export profile",
                Filter = "AuroraPAR profile (*.json)|*.json",
                FileName = SafeFileName(Active.Name) + ".json"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                SettingsStore.ExportProfile(Active, dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot export the profile:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetRunwaySide(RunwaySide side)
        {
            if (refreshing || Active.RunwaySide == side) return;
            Active.RunwaySide = side;
            Commit();
        }

        /// <summary>Analog console: knob or keys for each control group (the tilt also as a small joystick).</summary>
        private void BuildAnalogControlFields()
        {
            RangeControlComboBox.ItemsSource = new[] { "Knob", "One key per range", "Keys  <  middle  >", "Range panel (FIAR keys)" };
            TiltControlComboBox.ItemsSource = new[] { "Knobs (EL, AZ)", "Keys (UP 0 DN, L 0 R)", "Small joystick (4 ways)" };
            DhControlComboBox.ItemsSource = new[] { "Knob", "Keys (−  RWY  +)" };
            BrightnessControlComboBox.ItemsSource = new[] { "Knob", "Keys (−  100  +)" };
            ReadoutsComboBox.ItemsSource = new[] { "Segments (amber)", "Drums (mechanical counters)" };
            KeyStyleComboBox.ItemsSource = new[] { "Console (grey keys)", "FIAR (backlit keys)" };
            KeyStyleComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || KeyStyleComboBox.SelectedIndex < 0) return;
                AnalogKeyStyle value = (AnalogKeyStyle)KeyStyleComboBox.SelectedIndex;
                SetProfileValue(p => p.KeyStyle == value, p => p.KeyStyle = value);
            };
            ReadoutsComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || ReadoutsComboBox.SelectedIndex < 0) return;
                ReadoutStyle value = (ReadoutStyle)ReadoutsComboBox.SelectedIndex;
                SetProfileValue(p => p.Readouts == value, p => p.Readouts = value);
            };
            RangeControlComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || RangeControlComboBox.SelectedIndex < 0) return;
                AnalogRangeControl value = (AnalogRangeControl)RangeControlComboBox.SelectedIndex;
                RangeKeyTextBox.IsEnabled = !Active.RangeDefaultMiles;
                SetProfileValue(p => p.RangeControl == value, p => p.RangeControl = value);
            };
            TiltControlComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || TiltControlComboBox.SelectedIndex < 0) return;
                AnalogTiltControl value = (AnalogTiltControl)TiltControlComboBox.SelectedIndex;
                SetProfileValue(p => p.TiltControl == value, p => p.TiltControl = value);
            };
            DhControlComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || DhControlComboBox.SelectedIndex < 0) return;
                AnalogControl value = (AnalogControl)DhControlComboBox.SelectedIndex;
                SetProfileValue(p => p.DhControl == value, p => p.DhControl = value);
            };
            BrightnessControlComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || BrightnessControlComboBox.SelectedIndex < 0) return;
                AnalogControl value = (AnalogControl)BrightnessControlComboBox.SelectedIndex;
                SetProfileValue(p => p.BrightnessControl == value, p => p.BrightnessControl = value);
            };
            RangeKeyTextBox.TextChanged += (s, e) =>
            {
                if (refreshing) return;
                string text = RangeKeyTextBox.Text.Trim().ToUpperInvariant();
                if (text.Length == 0) return;
                SetProfileValue(p => p.RangeDefaultKey == text, p => p.RangeDefaultKey = text);
            };
        }

        /// <summary>
        /// Changes a profile option from a radio button or check box, unless it already has that value.
        /// </summary>
        private void SetProfileValue(Func<Profile, bool> alreadySet, Action<Profile> set)
        {
            if (refreshing || alreadySet(Active)) return;
            set(Active);
            Commit();
        }

        private void ApplyMagneticVariation()
        {
            if (refreshing) return;
            string text = MagVarBox.Text.Trim();
            if (text.Length == 0) text = "0";
            if (!MagneticVariation.TryParse(text, out double variation))
            {
                MagVarNote.Text = "not valid: e.g. 3E, 2.5W or -2";
                MagVarNote.Foreground = Brushes.DarkRed;
                return;
            }
            if (variation != Active.MagneticVariation)
            {
                Active.MagneticVariation = variation;
                Commit();
            }
            else
            {
                RefreshControls();
            }
        }

        /// <summary>Text of the fixed height lines, in the unit of the heights, rounded.</summary>
        private static string FixedHeightsText(Profile profile)
        {
            bool metres = profile.AltitudeScaleUnit == LengthUnit.Metres;
            return string.Join(", ", profile.FixedHeightLines.Select(h => FormatNumber(Math.Round(metres ? h * 0.3048 : h))));
        }

        /// <summary>Fixed height lines from the text (values in the unit of the heights, separated by commas or spaces).</summary>
        private void ApplyFixedHeights()
        {
            if (refreshing) return;
            bool metres = Active.AltitudeScaleUnit == LengthUnit.Metres;
            // Unchanged text: nothing to do (the values shown are rounded in the unit).
            if (FixedHeightsBox.Text.Trim() == FixedHeightsText(Active)) return;
            List<double> heights = [];
            foreach (string part in FixedHeightsBox.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0
                    || (metres ? value * 3.28084 : value) > 60000)
                {
                    FixedHeightsNote.Text = "not valid: e.g. 600 or 300, 600";
                    FixedHeightsNote.Foreground = Brushes.DarkRed;
                    return;
                }
                heights.Add(metres ? value / 0.3048 : value);
            }
            if (heights.Count > Profile.MaxFixedHeightLines)
            {
                FixedHeightsNote.Text = $"at most {Profile.MaxFixedHeightLines} lines";
                FixedHeightsNote.Foreground = Brushes.DarkRed;
                return;
            }
            Active.FixedHeightLines = heights;
            Active.Normalize();
            Commit();
        }

        private void ApplyHistoryInterval()
        {
            if (refreshing) return;
            if (double.TryParse(HistoryIntervalBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && seconds >= Profile.MinHistoryInterval && seconds <= Profile.MaxHistoryInterval)
            {
                if (seconds != Active.HistoryInterval)
                {
                    Active.HistoryInterval = seconds;
                    Commit();
                }
                return;
            }
            System.Media.SystemSounds.Beep.Play();
            HistoryIntervalBox.Text = Active.HistoryInterval.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private void ApplyHistoryDots()
        {
            if (refreshing) return;
            if (int.TryParse(HistoryDotsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dots)
                && dots >= Profile.MinHistoryDots && dots <= Profile.MaxHistoryDots)
            {
                if (dots != Active.HistoryDots)
                {
                    Active.HistoryDots = dots;
                    Commit();
                }
                return;
            }
            System.Media.SystemSounds.Beep.Play();
            HistoryDotsBox.Text = Active.HistoryDots.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One row per symbol (track, threshold, touchdown point, antenna): shape and size.
        /// </summary>
        /// <summary>Lengths of the horizontal DH line on each side (NM); 0 = up to the edge of the view.</summary>
        private static readonly double[] DhLengths = [0.25, 0.5, 1, 1.5, 2, 3, 4, 5, 0];
        /// <summary>Knees of the compressed distance scale offered (NM): the smaller, the stronger the compression.</summary>
        private static readonly double[] CompressionKnees = [1, 2, 3, 4, 6, 10];

        /// <summary>Item of the log distance scale list: the closest knee.</summary>
        private int SelectedScaleIndex()
        {
            int closest = 0;
            for (int i = 1; i < CompressionKnees.Length; i++)
            {
                if (Math.Abs(CompressionKnees[i] - Active.CompressionKnee) < Math.Abs(CompressionKnees[closest] - Active.CompressionKnee)) closest = i;
            }
            return closest;
        }
        private static readonly double[] DhAzimuthLengths = [0, 50, 100, 200, 300, 500, 1000];
        private static readonly double[] DhDropLengths = [0, 50, 100, 200, 300, 500, 1000];

        /// <summary>Grid the next rows are added to, and whether they are advanced options (see <see cref="ApplyAdvanced"/>).</summary>
        private Grid builderGrid = null!;
        private bool builderAdvanced;

        /// <summary>Adds an element to <see cref="builderGrid"/>, marked as advanced when the rows are.</summary>
        private void Place(FrameworkElement element)
        {
            if (builderAdvanced) element.Tag = AdvancedTag;
            builderGrid.Children.Add(element);
        }

        private void BuildSymbolRows()
        {
            // Fifth column: the key that edits a custom symbol.
            SymbolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DhSymbolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SymbolShape[] all = Enum.GetValues<SymbolShape>();
            builderGrid = SymbolGrid;
            builderAdvanced = false;
            AddSymbolRow("Track", p => p.TrackSymbol, all.Where(s => s != SymbolShape.None && s != SymbolShape.Line).ToArray());
            AddSymbolRow("History dots", p => p.HistorySymbol, all.Where(s => s != SymbolShape.None).ToArray());
            builderAdvanced = true;
            AddSymbolRow("Coasting track", p => p.CoastSymbol, all.Where(s => s != SymbolShape.None && s != SymbolShape.Line).ToArray());
            AddSymbolRow("Threshold", p => p.ThresholdSymbol, all);
            AddSymbolRow("Touchdown point", p => p.TouchdownSymbol, all);
            AddSymbolRow("Antenna", p => p.AntennaSymbol, all);
            builderGrid = DhSymbolGrid;
            AddSymbolRow("DH point symbol (side view)", p => p.DhSymbol, all);
            AddSymbolRow("DH point symbol (azimuth view)", p => p.DhAzimuthSymbol, all);
            builderAdvanced = false;
            AddCheckRow("Threshold as a tick like the DH (instead of its symbol): side view from the ground up by the length of the vertical DH line, azimuth view by the length of the DH azimuth line; style of the DH lines",
                p => p.ThresholdTick, (p, v) => p.ThresholdTick = v);
            AddCheckRow("Small circle at the far end of the glide path and of the centreline (as on the AN/APN-22)",
                p => p.LineEndCircles, (p, v) => p.LineEndCircles = v);
            AddChoiceRow("Terrain / obstacles (terrain.par)", ["Filled from the ground (terrain, as on ARTMACS)", "Stepped line with the altitude (minimum altitudes, OCA; default)", "Not drawn"],
                p => (int)p.TerrainDisplay, (p, i) => p.TerrainDisplay = (TerrainDisplay)i,
                "Profile under the approach of the runway, from terrain.par next to runways.par (field Terrain / obstacles of the runway editor). Colour in Colours and lines → Terrain / obstacles.");
            AddCheckRow("Deviation crosshair of the selected aircraft in the side view, as on ARTMACS (±500 ft up / down, ±1000 ft left / right; click a track to select it; key X on / off)",
                p => p.DeviationCrosshair, (p, v) => p.DeviationCrosshair = v);
            AddCheckRow("Primary video only, as the PRI picture of ARTMACS (modern display): everything red, no labels, receiver noise",
                p => p.PrimaryVideo, (p, v) => p.PrimaryVideo = v);
            AddChoiceRow("Ground clutter (fixed echoes near the antenna; key M: MTI on / off)", ["Off", "Light", "Medium", "Strong"],
                p => (int)p.GroundClutter, (p, i) => p.GroundClutter = (GroundClutterLevel)i,
                "Echoes of the ground, buildings and hills, strongest near the antenna, always the same for a runway (invented from its name). Colour in Colours and lines → Ground clutter. Key M: MTI filter on (weaker, default) / off.");
        }

        private void AddSymbolRow(string label, Func<Profile, SymbolSetting> get, SymbolShape[] shapes)
        {
            int row = builderGrid.RowDefinitions.Count;
            builderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2) };
            ComboBox shapeBox = new()
            {
                Width = 170,
                ItemsSource = shapes.Select(shape => new SymbolChoice(shape)).ToList(),
                ItemTemplate = SymbolTemplate,
                Margin = new Thickness(0, 2, 6, 2)
            };
            TextBox sizeBox = new()
            {
                Width = 40,
                Height = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 4, 2),
                ToolTip = "Size in pixels (2 to 60)"
            };
            TextBlock unit = new() { Text = "px", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetRow(shapeBox, row);
            Grid.SetRow(sizeBox, row);
            Grid.SetRow(unit, row);
            Grid.SetColumn(shapeBox, 1);
            Grid.SetColumn(sizeBox, 2);
            Grid.SetColumn(unit, 3);
            Place(text);
            Place(shapeBox);
            Place(sizeBox);
            Place(unit);
            Button edit = new() { Content = "✎", Width = 26, Height = 22, Margin = new Thickness(6, 2, 0, 2), ToolTip = "Draw a custom symbol (grid or vector path)." };
            Grid.SetRow(edit, row);
            Grid.SetColumn(edit, 4);
            Place(edit);
            // Custom symbol: the editor; cancelled, the shape stays as it was.
            bool EditCustom()
            {
                SymbolEditorWindow editor = new(label, get(Active)) { Owner = this };
                if (editor.ShowDialog() != true) return false;
                SymbolSetting symbol = get(Active);
                symbol.Shape = SymbolShape.Custom;
                symbol.Path = editor.ResultPath;
                symbol.Cells = editor.ResultCells;
                symbol.Filled = editor.ResultFilled;
                Commit();
                return true;
            }
            edit.Click += (s, e) =>
            {
                if (!EditCustom()) return;
            };
            shapeBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || shapeBox.SelectedIndex < 0) return;
                SymbolShape shape = shapes[shapeBox.SelectedIndex];
                if (shape == get(Active).Shape) return;
                if (shape == SymbolShape.Custom)
                {
                    // Back to the shape in use until the editor is closed with OK.
                    if (!EditCustom()) RefreshControls();
                    return;
                }
                get(Active).Shape = shape;
                Commit();
            };
            void ApplySize()
            {
                if (refreshing) return;
                if (double.TryParse(sizeBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double size)
                    && size >= 2 && size <= 60)
                {
                    if (size != get(Active).Size)
                    {
                        get(Active).Size = size;
                        Commit();
                    }
                    return;
                }
                System.Media.SystemSounds.Beep.Play();
                sizeBox.Text = FormatNumber(get(Active).Size);
            }
            sizeBox.LostFocus += (s, e) => ApplySize();
            sizeBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplySize();
            };
            refreshers.Add(() =>
            {
                shapeBox.SelectedIndex = Array.IndexOf(shapes, get(Active).Shape);
                sizeBox.Text = FormatNumber(get(Active).Size);
            });
        }

        /// <summary>
        /// An item of the symbol lists: the drawn symbol followed by its name.
        /// </summary>
        public sealed class SymbolChoice
        {
            internal SymbolChoice(SymbolShape shape)
            {
                Name = Symbols.DisplayName(shape);
                Icon = Symbols.Create(shape, 14);
                Fill = Symbols.IsFilled(shape) ? Brushes.Black : null;
            }

            public string Name { get; }
            public Geometry Icon { get; }
            public Brush? Fill { get; }
        }

        /// <summary>
        /// Shows a symbol choice as the drawn symbol (centred in a small box) and its name.
        /// </summary>
        private static readonly DataTemplate SymbolTemplate = CreateSymbolTemplate();

        private static DataTemplate CreateSymbolTemplate()
        {
            FrameworkElementFactory panel = new(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            FrameworkElementFactory box = new(typeof(Canvas));
            box.SetValue(WidthProperty, 22.0);
            box.SetValue(HeightProperty, 18.0);
            box.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
            FrameworkElementFactory path = new(typeof(System.Windows.Shapes.Path));
            path.SetValue(Canvas.LeftProperty, 11.0);
            path.SetValue(Canvas.TopProperty, 9.0);
            path.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.Black);
            path.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
            path.SetBinding(System.Windows.Shapes.Path.DataProperty, new Binding(nameof(SymbolChoice.Icon)));
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(SymbolChoice.Fill)));
            box.AppendChild(path);
            FrameworkElementFactory text = new(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(SymbolChoice.Name)));
            text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(box);
            panel.AppendChild(text);
            DataTemplate template = new() { VisualTree = panel };
            template.Seal();
            return template;
        }

        private void BuildRadarFields()
        {
            builderGrid = RadarGrid;
            builderAdvanced = false;
            AddHeader("Approach limits (track green inside, red outside)");
            AddCheckRow("Upper approach limit (off: lower limit only, as on some PARs: above the glide path the track stays green, however high)",
                p => p.UpperApproachLimit, (p, v) => p.UpperApproachLimit = v);
            AddCheckRow("Approach limits in the azimuth view (off: lines not drawn there, as on the AN/TPN-31; the track still turns red outside)",
                p => p.AzimuthApproachLimits, (p, v) => p.AzimuthApproachLimits = v);
            AddNumberField("Above the glide path", p => p.ApproachAbove, (p, v) => p.ApproachAbove = v, 0.1, 5);
            AddNumberField("Below the glide path", p => p.ApproachBelow, (p, v) => p.ApproachBelow = v, 0.1, 5);
            AddNumberField("Left of the centreline", p => p.ApproachLeft, (p, v) => p.ApproachLeft = v, 0.1, 10);
            AddNumberField("Right of the centreline", p => p.ApproachRight, (p, v) => p.ApproachRight = v, 0.1, 10);
            AddNumberField("Approach limit lines end at (from the touchdown; 0 = the whole range)",
                p => p.ApproachLimitsTo * DistanceUnits.PerNM(p.DistanceUnit), (p, v) => p.ApproachLimitsTo = v / DistanceUnits.PerNM(p.DistanceUnit),
                0, 100, unitOf: p => DistanceUnits.Name(p.DistanceUnit));
            AddHeader("Antenna position and vertical scale of the views");
            AddChoiceRow("Vertical scale", ["Heights and distances (ft / m)", "Angles from the antenna (°), as on the PAR-E", "Angles with LOG, heights with LIN (key C switches both)"],
                p => (int)p.VerticalScale, (p, i) => p.VerticalScale = (VerticalScale)i,
                "Heights: the classic picture, glide path and centreline straight. Angles: the vertical axis is the angle seen from the antenna (elevation, azimuth), as on the PAR-E: the glide path, the limits and a centreline seen from an antenna beside the runway become curves, a direction from the antenna is a horizontal line. Works with LIN and LOG distances. Angles with LOG, heights with LIN: the LIN / LOG keys and key C switch the vertical scale too, so the glide path is straight in LIN and curved in LOG.");
            AddChoiceRow("Antenna position", ["On the runway centreline", "Left of the runway (seen landing)", "Right of the runway (seen landing)"],
                p => (int)p.AntennaSide, (p, i) => p.AntennaSide = (AntennaSide)i,
                "Where the antenna stands, at half the runway length. Beside the runway its azimuth is measured from its own position (parallel to the centreline): the azimuth scan and, with the angular scale, the centreline show it.");
            AddNumberField("Antenna distance from the centreline (beside the runway)", p => p.AntennaOffsetMetres, (p, v) => p.AntennaOffsetMetres = v, 10, 2000, unitText: "m");
            AddHeader("Safety limits (optional lines outside the approach limits, cut at the scan limits)");
            AddCheckRow("Safety limits in the elevation view", p => p.SafetyLimitsElevation, (p, v) => p.SafetyLimitsElevation = v);
            AddNumberField("Above the glide path", p => p.SafetyAbove, (p, v) => p.SafetyAbove = v, 0.1, 15, (p, v) => v > p.ApproachAbove);
            AddNumberField("Below the glide path", p => p.SafetyBelow, (p, v) => p.SafetyBelow = v, 0.1, 15, (p, v) => v > p.ApproachBelow);
            AddCheckRow("Safety limits in the azimuth view", p => p.SafetyLimitsAzimuth, (p, v) => p.SafetyLimitsAzimuth = v);
            AddNumberField("Left of the centreline", p => p.SafetyLeft, (p, v) => p.SafetyLeft = v, 0.1, 30, (p, v) => v > p.ApproachLeft);
            AddNumberField("Right of the centreline", p => p.SafetyRight, (p, v) => p.SafetyRight = v, 0.1, 30, (p, v) => v > p.ApproachRight);
            // The rest of the radar is advanced.
            builderAdvanced = true;
            AddHeader("Scan limits from the antenna (physical limits, fixed)");
            AddNumberField("Up", p => p.ScanUp, (p, v) => p.ScanUp = v, 1, Profile.MaxScanAngle, (p, v) => v > p.ScanDown);
            AddNumberField("Down (negative = below the horizon)", p => p.ScanDown, (p, v) => p.ScanDown = v, -Profile.MaxScanAngle, 30, (p, v) => v < p.ScanUp);
            AddNumberField("Left", p => p.ScanLeft, (p, v) => p.ScanLeft = v, 1, Profile.MaxScanAngle);
            AddNumberField("Right", p => p.ScanRight, (p, v) => p.ScanRight = v, 1, Profile.MaxScanAngle);
            AddHeader("Antenna beam (only the traffic inside the beam is seen)");
            AddCheckRow("Narrow antenna beam moved by the tilt (advanced; off: the beam is the scan limits and there is no tilt)",
                p => p.BeamEnabled, (p, v) =>
                {
                    p.BeamEnabled = v;
                    // Turned on with a beam as wide as the scan limits (no room for the tilt): a few degrees
                    // narrower, so the tilt works at once. A beam already set is kept.
                    if (v) p.NarrowBeam(onlyWhereFull: true);
                });
            AddNumberField("Elevation width", p => p.BeamElevation, (p, v) => p.BeamElevation = v, 0.5, 90);
            AddNumberField("Azimuth width", p => p.BeamAzimuth, (p, v) => p.BeamAzimuth = v, 0.5, 180);
            AddNumberField("Elevation centre in neutral", p => p.BeamElevationNeutral, (p, v) => p.BeamElevationNeutral = v, -30, 60);
            AddCheckRow("Elevation centre automatic: the glide path angle in use (instead of the value above)",
                p => p.BeamElevationNeutralAuto, (p, v) => p.BeamElevationNeutralAuto = v);
            AddNumberField("Azimuth centre in neutral (+ right)", p => p.BeamAzimuthNeutral, (p, v) => p.BeamAzimuthNeutral = v, -90, 90);
            AddCheckRow("Draw the edges of the beam (otherwise only the thicker range marks show it)",
                p => p.ShowBeamEdges, (p, v) => p.ShowBeamEdges = v);
            AddHeader("Antenna tilt (moves the beam inside the scan limits)");
            AddNumberField("Step", p => p.TiltStep, (p, v) => p.TiltStep = v, 0.5, 10);
            int row = RadarGrid.RowDefinitions.Count;
            RadarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            CheckBox swap = new()
            {
                Content = "Azimuth: swap left and right (as seen from the runway)",
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Not ticked: L/R of the azimuth tilt as seen by the pilot flying the approach (default).\n"
                    + "Ticked: as seen from the runway looking at the approach (controller's view): R is the pilot's left.\n"
                    + "Applies to the AZ TILT knob, the arrow keys, the AZ buttons and the L/R readouts."
            };
            swap.Checked += (s, e) => SetProfileValue(p => p.AzimuthTiltSwapped, p => p.AzimuthTiltSwapped = true);
            swap.Unchecked += (s, e) => SetProfileValue(p => !p.AzimuthTiltSwapped, p => p.AzimuthTiltSwapped = false);
            refreshers.Add(() => swap.IsChecked = Active.AzimuthTiltSwapped);
            Grid.SetRow(swap, row);
            Grid.SetColumnSpan(swap, 3);
            Place(swap);
            // Range marks beyond a distance (Range and scale).
            builderGrid = RangeExtraGrid;
            builderAdvanced = false;
            AddNumberField("Lines across the view up to (from the touchdown; 0 = all); beyond: short lines, distance written only on the largest marks",
                p => p.RangeMarks.FullLinesTo * DistanceUnits.PerNM(p.DistanceUnit), (p, v) => p.RangeMarks.FullLinesTo = v / DistanceUnits.PerNM(p.DistanceUnit),
                0, 100, unitOf: p => DistanceUnits.Name(p.DistanceUnit));
            AddChoiceRow("Distance scale", ["LIN and LOG (keys LIN | LOG, key C)", "LIN only", "LOG only"],
                p => (int)p.DistanceScaleLock, (p, i) => { p.DistanceScaleLock = (DistanceScaleLock)i; p.ApplyScaleLock(); },
                "Lock the profile to one distance scale, e.g. when its LOG is not the one of the radar it imitates: no LIN | LOG keys, key C off.");
            AddCheckRow("Distance written in the colour of its mark (e.g. 5 km magenta, 1 km cyan as on the PAR-E)",
                p => p.RangeMarks.TextInMarkColour, (p, v) => p.RangeMarks.TextInMarkColour = v);
            AddCheckRow("A mark at 0 (the touchdown point), with its distance written (0KM, as on the PAR-E)",
                p => p.RangeMarks.MarkAtZero, (p, v) => p.RangeMarks.MarkAtZero = v);
            AddCheckRow("Range lines over the whole height of the views, not only inside the scan limits (as on ARTMACS)",
                p => p.RangeMarks.LinesFullHeight, (p, v) => p.RangeMarks.LinesFullHeight = v);
            builderAdvanced = true;
            // Coasting tracks: in Tracks and labels (advanced).
            builderGrid = CoastGrid;
            AddNumberField("Coasting tracks (modern display): seconds shown out of the beam (0 = none)", p => p.CoastSeconds, (p, v) => p.CoastSeconds = v, 0, Profile.MaxCoastSeconds, unitText: "s");
            builderGrid = RadarGrid;
            builderAdvanced = false;
        }

        /// <summary>A check box over the whole width of the radar group, bound to a profile value.</summary>
        private void AddCheckRow(string text, Func<Profile, bool> get, Action<Profile, bool> set)
        {
            int row = builderGrid.RowDefinitions.Count;
            builderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // Long texts wrap instead of being cut at the edge of the window.
            CheckBox check = new()
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
                Margin = new Thickness(0, 4, 0, 2)
            };
            check.Checked += (s, e) => SetProfileValue(p => get(p), p => set(p, true));
            check.Unchecked += (s, e) => SetProfileValue(p => !get(p), p => set(p, false));
            refreshers.Add(() => check.IsChecked = get(Active));
            Grid.SetRow(check, row);
            Grid.SetColumnSpan(check, 3);
            Place(check);
        }

        private void AddHeader(string text)
        {
            int row = builderGrid.RowDefinitions.Count;
            builderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock header = new()
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xB8)),
                Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 4)
            };
            Grid.SetRow(header, row);
            Grid.SetColumnSpan(header, 3);
            Place(header);
        }

        /// <summary>
        /// Adds a number field bound to a profile value. The value is applied with Enter or when leaving the field;
        /// an invalid value is refused (the previous one comes back).
        /// </summary>
        private void AddNumberField(string label, Func<Profile, double> get, Action<Profile, double> set, double min, double max,
                                    Func<Profile, double, bool>? isValid = null, string unitText = "°", Func<Profile, string>? unitOf = null)
        {
            int row = builderGrid.RowDefinitions.Count;
            builderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock text = new()
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 8, 2)
            };
            TextBox box = new()
            {
                Width = 60,
                Height = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 4, 2),
                ToolTip = $"{FormatNumber(min)} to {FormatNumber(max)}"
            };
            TextBlock unit = new()
            {
                Text = unitText,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(text, row);
            Grid.SetRow(box, row);
            Grid.SetRow(unit, row);
            Grid.SetColumn(box, 1);
            Grid.SetColumn(unit, 2);
            Place(text);
            Place(box);
            Place(unit);
            void Apply()
            {
                if (refreshing) return;
                // Unchanged text (e.g. focus passing through): nothing to do, so a rounded value is not saved back.
                if (box.Text.Trim() == FormatNumber(get(Active))) return;
                if (double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    && value >= min && value <= max && (isValid == null || isValid(Active, value)))
                {
                    if (value != get(Active))
                    {
                        set(Active, value);
                        Commit();
                    }
                    return;
                }
                System.Media.SystemSounds.Beep.Play();
                box.Text = FormatNumber(get(Active));
            }
            box.LostFocus += (s, e) => Apply();
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Apply();
            };
            refreshers.Add(() =>
            {
                box.Text = FormatNumber(get(Active));
                if (unitOf != null) unit.Text = unitOf(Active);
            });
        }

        /// <summary>A list of choices bound to a profile value (the index of the item).</summary>
        private void AddChoiceRow(string label, string[] items, Func<Profile, int> get, Action<Profile, int> set, string? toolTip = null)
        {
            int row = builderGrid.RowDefinitions.Count;
            builderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2) };
            ComboBox box = new() { ItemsSource = items, Width = 300, Height = 22, Margin = new Thickness(0, 2, 4, 2), ToolTip = toolTip };
            text.ToolTip = toolTip;
            Grid.SetRow(text, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            Grid.SetColumnSpan(box, 2);
            Place(text);
            Place(box);
            box.SelectionChanged += (s, e) =>
            {
                if (refreshing || box.SelectedIndex < 0) return;
                int index = box.SelectedIndex;
                SetProfileValue(p => get(p) == index, p => set(p, index));
            };
            refreshers.Add(() => box.SelectedIndex = get(Active));
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Ticks or unticks a range offered by the range controls; the last one cannot be unticked.</summary>
        private void SetRangeOffered(double range, bool offered)
        {
            if (refreshing) return;
            List<double> ranges = [.. Ranges.Of(Active.DisplayRanges)];
            if (offered) ranges.Add(range); else ranges.RemoveAll(r => Math.Abs(r - range) < 0.01);
            if (ranges.Count == 0)
            {
                // At least one range: tick it again.
                RefreshControls();
                return;
            }
            Active.DisplayRanges = [.. Ranges.Of(ranges)];
            Commit();
        }

        private void SetStartupRange(StartupRange startupRange)
        {
            if (refreshing || Active.StartupRange == startupRange) return;
            Active.StartupRange = startupRange;
            Commit();
        }

        private void SetRunwayChangeRange(RunwayChangeRange runwayChangeRange)
        {
            if (refreshing || Active.RunwayChangeRange == runwayChangeRange) return;
            Active.RunwayChangeRange = runwayChangeRange;
            Commit();
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
