using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Effects;

namespace AuroraPAR
{
    /// <summary>
    /// Values shown on the console panel of the analog mode.
    /// </summary>
    internal record ConsoleData(
        string Icao,
        string Runway,
        string FinalCourse,
        string PressureName,
        string PressureText,
        string MinimumName,
        string MinimumText,
        double GlideSlope,
        string MissedApproach,
        double Range,
        double TiltElevation,
        double TiltAzimuth,
        bool AzimuthSwapped,
        bool Connected,
        double DataInterval,
        bool DataSlow);

    /// <summary>
    /// Console panel next to the scope in the analog mode: segment readouts (airport, runway, pressure, minimum,
    /// range, antenna tilt) and status lamps (STS, antenna refresh rate, tilt), with labels in the B612 cockpit font.
    /// </summary>
    internal class ConsolePanel : Border
    {
        public static readonly FontFamily CockpitFont = new(new Uri("pack://application:,,,/"), "./Fonts/#B612");
        private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD0)));
        private static readonly Brush EngravingBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8C, 0x8E, 0x88)));

        /// <summary>Characters of every readout: the same for all, so the windows line up.</summary>
        private const int Cells = 5;

        private readonly Readout icao = new(Cells);
        private readonly Readout runway = new(Cells);
        private readonly Readout pressure = new(Cells);
        private readonly Readout minimum = new(Cells);
        private readonly Readout finalCourse = new(Cells);
        private readonly Readout glideSlope = new(Cells);
        private readonly Readout missedApproach = new(Cells);
        private readonly Readout range = new(Cells);
        private readonly Readout tiltElevation = new(Cells);
        private readonly Readout tiltAzimuth = new(Cells);
        private readonly TextBlock pressureLabel = Label("QNH");
        private readonly TextBlock minimumLabel = Label("DA FT");
        private readonly TextBlock rangeLabel = Label("RANGE NM");
        private readonly Lamp statusLamp = new();
        private readonly Lamp refreshLamp = new();
        private readonly Lamp tiltLamp = new();
        private readonly FrameworkElement refreshRow;
        /// <summary>
        /// Tooltip of ANT. R/R: one object whose text is updated, so it stays open while the measured value changes
        /// (replacing the tooltip at every refresh closed it after a fraction of a second).
        /// </summary>
        private readonly TextBlock refreshToolTipText = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

        public ConsolePanel()
        {
            Width = 196;
            Padding = new Thickness(10, 10, 12, 10);
            Background = new SolidColorBrush(ScopeBezel.PanelColor);
            StackPanel stack = new();
            stack.Children.Add(new TextBlock
            {
                Text = "PRECISION APPROACH RADAR",
                FontFamily = CockpitFont,
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                Foreground = EngravingBrush,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(Row(Label("APT"), icao, "Airport (ICAO)."));
            stack.Children.Add(Row(Label("RWY"), runway, "Runway (designator in runways.par)."));
            stack.Children.Add(Row(Label("CRS"), finalCourse, "Final course (magnetic). Calculated, not the published value: runway true heading from runways.par corrected with the magnetic variation (of the runway, or the default in Settings), rounded to the degree. Check it against the approach chart."));
            stack.Children.Add(Row(Label("GP DEG"), glideSlope, "Glide path angle of the approach, in degrees. With several approaches for the runway in runways.par, chosen with the GP knob."));
            stack.Children.Add(Row(Label("MAPt DIST"), missedApproach, "Missed approach point: distance in NM from the touchdown point where the glide path reaches the decision height (DH / OCH set with the DH knob). Compare it with the approach chart (MAPt / RPI DIST) to check runways.par; it is the distance for \"approach terminating at ...\"."));
            stack.Children.Add(Row(minimumLabel, minimum, "Minimum: altitude with QNH, height with QFE (set with the DH knob)."));
            stack.Children.Add(Row(pressureLabel, pressure, "Pressure setting: QNH, or QFE computed for the threshold elevation."));
            stack.Children.Add(Row(rangeLabel, range, "Displayed range."));
            stack.Children.Add(Row(Label("EL TILT"), tiltElevation, "Antenna elevation tilt in degrees (U up, D down)."));
            stack.Children.Add(Row(Label("AZ TILT"), tiltAzimuth, "Antenna azimuth tilt in degrees (L left, R right): as seen by the pilot, or as seen from the runway when swapped in Settings."));
            stack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x18)),
                Margin = new Thickness(0, 8, 0, 8)
            });
            stack.Children.Add(LampRow(statusLamp, "STS", "Connection to Aurora: green connected, red not connected."));
            refreshRow = LampRow(refreshLamp, "ANT. R/R", "");
            refreshRow.ToolTip = new ToolTip { Content = refreshToolTipText };
            stack.Children.Add(refreshRow);
            stack.Children.Add(LampRow(tiltLamp, "TILT", "Antenna tilted (not in neutral position)."));
            Child = stack;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = CockpitFont,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = LabelBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static FrameworkElement Row(TextBlock label, FrameworkElement display, string toolTip)
        {
            DockPanel row = new() { Margin = new Thickness(0, 0, 0, 6), ToolTip = toolTip, Background = Brushes.Transparent };
            ToolTips.KeepOpen(row);
            display.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(display, Dock.Right);
            row.Children.Add(display);
            row.Children.Add(label);
            return row;
        }

        private static FrameworkElement LampRow(Lamp lamp, string text, string toolTip)
        {
            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 7),
                ToolTip = toolTip,
                Background = Brushes.Transparent
            };
            ToolTips.KeepOpen(row);
            lamp.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(lamp);
            TextBlock label = Label(text);
            row.Children.Add(label);
            return row;
        }

        /// <summary>Readouts as mechanical drum counters (otherwise 14-segment displays).</summary>
        public bool Drums
        {
            set
            {
                foreach (Readout readout in new[] { icao, runway, pressure, minimum, finalCourse, glideSlope, missedApproach, range, tiltElevation, tiltAzimuth })
                {
                    readout.Drums = value;
                }
            }
        }

        public void Update(ConsoleData data)
        {
            icao.Text = data.Icao;
            runway.Text = data.Runway;
            pressureLabel.Text = data.PressureName;
            pressure.Text = data.PressureText;
            minimumLabel.Text = $"{data.MinimumName} FT";
            minimum.Text = data.MinimumText;
            finalCourse.Text = data.FinalCourse;
            glideSlope.Text = Runway.FormatGlideSlope(data.GlideSlope);
            missedApproach.Text = data.MissedApproach;
            rangeLabel.Text = "RANGE " + Distance.UnitText;
            range.Text = Distance.ValueText(data.Range);
            tiltElevation.Text = Tilt(data.TiltElevation, "U", "D");
            tiltAzimuth.Text = Tilt(data.AzimuthSwapped ? -data.TiltAzimuth : data.TiltAzimuth, "R", "L");
            statusLamp.State = data.Connected ? LampState.Green : LampState.Red;
            bool blink = DateTime.Now.Millisecond < 500;
            string measured;
            if (double.IsNaN(data.DataInterval))
            {
                refreshLamp.State = LampState.Off;
                measured = "Not measured yet: it needs moving traffic (about 10-20 s).";
            }
            else
            {
                string seconds = data.DataInterval.ToString("0.0", CultureInfo.InvariantCulture);
                refreshLamp.State = data.DataSlow ? (blink ? LampState.Red : LampState.Off) : LampState.Green;
                measured = $"Measured now: traffic updated every {seconds} s.";
            }
            string toolTip = "ANT. R/R - antenna refresh rate: how often Aurora updates the traffic positions.\n"
                + "GREEN: good.\n"
                + "RED (flashing): too slow, tracks move in jumps. In Aurora set the traffic refresh rate to 0.5 s.\n"
                + "OFF: not measured yet.\n\n"
                + measured;
            // Changed only when different, so an open tooltip is not closed at every refresh.
            if (refreshToolTipText.Text != toolTip) refreshToolTipText.Text = toolTip;
            tiltLamp.State = data.TiltElevation != 0 || data.TiltAzimuth != 0 ? LampState.Amber : LampState.Off;
        }

        private static string Tilt(double value, string positive, string negative)
        {
            string number = Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture);
            return value == 0 ? number : (value > 0 ? positive : negative) + number;
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }

    internal enum LampState
    {
        Off,
        Green,
        Amber,
        Red
    }

    /// <summary>
    /// Round indicator lamp of the console.
    /// </summary>
    internal class Lamp : FrameworkElement
    {
        private const double Radius = 7;
        private LampState state = LampState.Off;
        private readonly DropShadowEffect glow = new() { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.9 };

        public Lamp()
        {
            Width = 2 * Radius + 4;
            Height = 2 * Radius + 4;
        }

        public LampState State
        {
            get => state;
            set
            {
                if (state == value) return;
                state = value;
                if (state == LampState.Off)
                {
                    Effect = null;
                }
                else
                {
                    glow.Color = Lit(state);
                    Effect = glow;
                }
                InvalidateVisual();
            }
        }

        private static Color Lit(LampState state) => state switch
        {
            LampState.Green => Color.FromRgb(0x50, 0xFF, 0x50),
            LampState.Amber => Color.FromRgb(0xFF, 0xB0, 0x20),
            LampState.Red => Color.FromRgb(0xFF, 0x30, 0x20),
            _ => Color.FromRgb(0x50, 0x50, 0x4C)
        };

        protected override void OnRender(DrawingContext dc)
        {
            Point c = new(ActualWidth / 2, ActualHeight / 2);
            // Metal ring.
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0x96)), new Pen(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x16)), 1), c, Radius + 1.5, Radius + 1.5);
            Color color = Lit(state);
            Color dark = state == LampState.Off
                ? Color.FromRgb(0x24, 0x24, 0x22)
                : Color.FromRgb((byte)(color.R * 0.55), (byte)(color.G * 0.55), (byte)(color.B * 0.55));
            Color center = state == LampState.Off
                ? Color.FromRgb(0x4A, 0x4A, 0x46)
                : Color.FromRgb((byte)Math.Min(255, color.R + 0x60), (byte)Math.Min(255, color.G + 0x60), (byte)Math.Min(255, color.B + 0x60));
            RadialGradientBrush lens = new(center, dark) { GradientOrigin = new Point(0.4, 0.35) };
            dc.DrawEllipse(lens, null, c, Radius, Radius);
        }
    }

    /// <summary>
    /// 14-segment display window, as on the digital readouts of the old equipment: amber segments, with the unlit
    /// segments faintly visible. A '.' lights the decimal point of the previous character.
    /// </summary>
    internal class SegmentDisplay : FrameworkElement
    {
        private const double CharHeight = 20;
        private const double CharWidth = 12;
        private const double CharGap = 5;
        private const double Pad = 5;
        private const double Thickness = 1.8;
        private const double Slant = 0.12;

        private static readonly Brush OnBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x30)));
        private static readonly Brush GhostBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xA8, 0x30)));
        private static readonly Brush WindowBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x07, 0x05)));
        private static readonly Pen WindowPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x10)), 1.5));
        private static readonly Pen OnPen = Frozen(new Pen(OnBrush, Thickness) { StartLineCap = PenLineCap.Triangle, EndLineCap = PenLineCap.Triangle });
        private static readonly Pen GhostPen = Frozen(new Pen(GhostBrush, Thickness) { StartLineCap = PenLineCap.Triangle, EndLineCap = PenLineCap.Triangle });
        private static readonly Pen OnDiagonalPen = Frozen(new Pen(OnBrush, Thickness * 0.85) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
        private static readonly Pen GhostDiagonalPen = Frozen(new Pen(GhostBrush, Thickness * 0.85) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });

        /// <summary>
        /// Segments: a top, b upper right, c lower right, d bottom, e lower left, f upper left, g middle left,
        /// h middle right, i upper left diagonal, j upper centre, k upper right diagonal, l lower left diagonal,
        /// m lower centre, n lower right diagonal.
        /// </summary>
        private static readonly Dictionary<char, string> Characters = new()
        {
            [' '] = "",
            ['0'] = "abcdefkl",
            ['1'] = "bc",
            ['2'] = "abdegh",
            ['3'] = "abcdh",
            ['4'] = "bcfgh",
            ['5'] = "acdfgh",
            ['6'] = "acdefgh",
            ['7'] = "abc",
            ['8'] = "abcdefgh",
            ['9'] = "abcdfgh",
            ['A'] = "abcefgh",
            ['B'] = "abcdhjm",
            ['C'] = "adef",
            ['D'] = "abcdjm",
            ['E'] = "adefg",
            ['F'] = "aefg",
            ['G'] = "acdefh",
            ['H'] = "bcefgh",
            ['I'] = "adjm",
            ['J'] = "bcde",
            ['K'] = "efgkn",
            ['L'] = "def",
            ['M'] = "bcefik",
            ['N'] = "bcefin",
            ['O'] = "abcdef",
            ['P'] = "abefgh",
            ['Q'] = "abcdefn",
            ['R'] = "abefghn",
            ['S'] = "acdfgh",
            ['T'] = "ajm",
            ['U'] = "bcdef",
            ['V'] = "efkl",
            ['W'] = "bcefln",
            ['X'] = "ikln",
            ['Y'] = "ikm",
            ['Z'] = "adkl",
            ['-'] = "gh",
            ['+'] = "ghjm",
            ['/'] = "kl",
            ['\\'] = "in",
            ['*'] = "ghijklmn",
            ['='] = "dgh",
            ['_'] = "d",
            ['\''] = "j",
            ['('] = "kn",
            [')'] = "il",
            ['<'] = "kn",
            ['>'] = "il"
        };

        private readonly int cells;
        private string text = "";

        public SegmentDisplay(int cells)
        {
            this.cells = cells;
            Width = 2 * Pad + cells * CharWidth + (cells - 1) * CharGap + 2;
            Height = CharHeight + 2 * Pad;
        }

        /// <summary>Text shown, right aligned; characters beyond the window are cut on the right.</summary>
        public string Text
        {
            get => text;
            set
            {
                value ??= "";
                if (text == value) return;
                text = value;
                InvalidateVisual();
            }
        }

        /// <summary>Splits the text in cells: character and decimal point.</summary>
        private List<(char Character, bool Point)> Cells() => Split(text, cells);

        /// <summary>Splits a text in <paramref name="cells"/> cells (right aligned): character and decimal point.</summary>
        internal static List<(char Character, bool Point)> Split(string text, int cells)
        {
            List<(char, bool)> list = [];
            foreach (char raw in text.ToUpperInvariant())
            {
                if ((raw == '.' || raw == ',') && list.Count > 0 && !list[^1].Item2)
                {
                    list[^1] = (list[^1].Item1, true);
                    continue;
                }
                list.Add((raw == '.' || raw == ',' ? ' ' : raw, raw == '.' || raw == ','));
            }
            if (list.Count > cells) list.RemoveRange(cells, list.Count - cells);
            while (list.Count < cells) list.Insert(0, (' ', false));
            return list;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRoundedRectangle(WindowBrush, WindowPen, new Rect(0, 0, ActualWidth, ActualHeight), 2, 2);
            List<(char Character, bool Point)> list = Cells();
            for (int i = 0; i < list.Count; i++)
            {
                double left = Pad + 1 + i * (CharWidth + CharGap);
                string lit = Characters.TryGetValue(list[i].Character, out string? segments) ? segments : "";
                DrawCell(dc, left, Pad, lit, list[i].Point);
            }
        }

        private static void DrawCell(DrawingContext dc, double left, double top, string lit, bool point)
        {
            double w = CharWidth;
            double h = CharHeight;
            double mid = h / 2;
            double g = Thickness * 0.9;
            // Italic slant, as on the segment displays: x moves right going up.
            Point P(double x, double y) => new(left + x + (h - y) * Slant - h * Slant / 2, top + y);
            void Segment(char name, Point a, Point b, bool diagonal)
            {
                bool on = lit.Contains(name);
                dc.DrawLine(diagonal ? (on ? OnDiagonalPen : GhostDiagonalPen) : (on ? OnPen : GhostPen), a, b);
            }
            Segment('a', P(g, 0), P(w - g, 0), false);
            Segment('b', P(w, g), P(w, mid - g), false);
            Segment('c', P(w, mid + g), P(w, h - g), false);
            Segment('d', P(g, h), P(w - g, h), false);
            Segment('e', P(0, mid + g), P(0, h - g), false);
            Segment('f', P(0, g), P(0, mid - g), false);
            Segment('g', P(g, mid), P(w / 2 - g * 0.6, mid), false);
            Segment('h', P(w / 2 + g * 0.6, mid), P(w - g, mid), false);
            Segment('j', P(w / 2, g * 1.2), P(w / 2, mid - g * 1.2), false);
            Segment('m', P(w / 2, mid + g * 1.2), P(w / 2, h - g * 1.2), false);
            double dx = g * 1.1;
            double dy = g * 1.5;
            Segment('i', P(dx, dy), P(w / 2 - dx, mid - dy), true);
            Segment('k', P(w - dx, dy), P(w / 2 + dx, mid - dy), true);
            Segment('l', P(dx, h - dy), P(w / 2 - dx, mid + dy), true);
            Segment('n', P(w - dx, h - dy), P(w / 2 + dx, mid + dy), true);
            // Decimal point.
            dc.DrawEllipse(point ? OnBrush : GhostBrush, null, P(w + CharGap / 2 + 0.5, h), Thickness * 0.65, Thickness * 0.65);
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
    /// <summary>
    /// Airport entry of the analog console: a readout window (same 14-segment display as the console) that takes
    /// the keyboard. It shows the airport in use; click it and type the ICAO (the last character blinks as the
    /// cursor): with four characters, or Enter, the code is sent to <see cref="Submit"/>; if that refuses it
    /// (unknown airport) the window flashes and shows the airport in use again. Esc cancels. Ctrl+V pastes.
    /// </summary>
    internal class AptEntry : Border
    {
        private const int Cells = 4;
        private readonly Readout display = new(Cells);
        private readonly System.Windows.Threading.DispatcherTimer blink = new() { Interval = TimeSpan.FromMilliseconds(450) };
        private readonly System.Windows.Threading.DispatcherTimer flash = new() { Interval = TimeSpan.FromMilliseconds(160) };
        private string icao = "";
        private string typed = "";
        private bool editing;
        private bool cursorOn = true;
        private int flashStep;

        /// <summary>The window as mechanical drum counter (otherwise 14-segment display).</summary>
        public bool Drums
        {
            set => display.Drums = value;
        }

        /// <summary>Receives the code typed; returns false when it is not valid (unknown airport).</summary>
        public Func<string, bool>? Submit { get; set; }

        public AptEntry()
        {
            Child = display;
            Focusable = true;
            FocusVisualStyle = null;
            Cursor = Cursors.IBeam;
            Background = Brushes.Transparent;
            ToolTip = "Airport (ICAO). Click and type the 4 letters: the runway keys of that airport appear. Enter confirms, Esc cancels. With a callsign like LIPC_APP connected in Aurora, its airport is set at the connection.";
            blink.Tick += (s, e) =>
            {
                cursorOn = !cursorOn;
                Refresh();
            };
            flash.Tick += (s, e) =>
            {
                flashStep++;
                if (flashStep >= 6)
                {
                    flash.Stop();
                    flashStep = 0;
                }
                Refresh();
            };
        }

        /// <summary>Airport in use, shown when nothing is being typed.</summary>
        public string Icao
        {
            get => icao;
            set
            {
                value ??= "";
                if (icao == value) return;
                icao = value;
                Refresh();
            }
        }

        private void Refresh()
        {
            string text;
            if (flash.IsEnabled && flashStep % 2 == 0)
            {
                text = "";
            }
            else if (editing)
            {
                text = typed + (cursorOn && typed.Length < Cells ? "_" : "");
            }
            else
            {
                text = icao;
            }
            display.Text = text.PadRight(Cells);
        }

        private void Cancel()
        {
            editing = false;
            blink.Stop();
            typed = "";
            Refresh();
        }

        private void Commit()
        {
            string code = typed;
            editing = false;
            blink.Stop();
            typed = "";
            FocusHelper.ReleaseKeyboard(this);
            if (Submit?.Invoke(code) == true)
            {
                Refresh();
            }
            else
            {
                flashStep = 0;
                flash.Start();
                Refresh();
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            e.Handled = true;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            flash.Stop();
            editing = true;
            typed = "";
            cursorOn = true;
            blink.Start();
            Refresh();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            if (editing) Cancel();
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            if (!editing) return;
            foreach (char c in e.Text.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c) && c < 128 && typed.Length < Cells) typed += c;
            }
            e.Handled = true;
            cursorOn = true;
            if (typed.Length >= Cells) Commit(); else Refresh();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!editing) return;
            switch (e.Key)
            {
                case Key.Back:
                    if (typed.Length > 0) typed = typed[..^1];
                    cursorOn = true;
                    Refresh();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    e.Handled = true;
                    if (typed.Length > 0) Commit(); else { Cancel(); FocusHelper.ReleaseKeyboard(this); }
                    break;
                case Key.Escape:
                    e.Handled = true;
                    Cancel();
                    FocusHelper.ReleaseKeyboard(this);
                    break;
                case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                    e.Handled = true;
                    try
                    {
                        string pasted = new string((Clipboard.GetText() ?? "").Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray()).ToUpperInvariant();
                        if (pasted.Length > 0)
                        {
                            typed = pasted[..Math.Min(Cells, pasted.Length)];
                            Commit();
                        }
                    }
                    catch (Exception)
                    {
                        // Clipboard busy: nothing is pasted.
                    }
                    break;
            }
        }
    }
}
