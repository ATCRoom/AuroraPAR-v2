using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the QNH in SIMULATION SETTINGS (the same as the "QNH (no Aurora)" box of the
    /// main window): typed by hand without Aurora, grey with Aurora (its METAR gives it). From METAR without Aurora
    /// asks the internet (<see cref="OnlineMetar"/>) and fills the wind and the QNH.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly TextBox qnhBox = new() { Width = 60, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right };
        private readonly TextBlock metarSource = new() { Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250 };
        private Func<bool> auroraConnected = () => false;
        private Func<int> currentQnh = () => 0;
        private Action<int>? setManualQnh;
        private bool fetchingMetar;

        /// <summary>From the main window: Aurora connected, the QNH in use (hPa, 0 unknown), and how to set the one typed by hand.</summary>
        internal void AttachWeather(Func<bool> connected, Func<int> qnh, Action<int> setQnh)
        {
            auroraConnected = connected;
            currentQnh = qnh;
            setManualQnh = setQnh;
        }

        private WrapPanel BuildQnhRow()
        {
            WrapPanel row = new() { Margin = new Thickness(0, 6, 0, 0) };
            qnhBox.ToolTip = "QNH (hPa; or inHg, e.g. 29.92). Without Aurora: typed here (Enter), or From METAR (from the internet). With Aurora: from its METAR (grey). The same as the QNH box of the main window. Mouse wheel: 1 hPa (+Shift: 5).";
            row.Children.Add(Piece(Caption("QNH"), qnhBox, Caption("hPa")));
            row.Children.Add(metarSource);
            qnhBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { ApplyQnhTyped(); e.Handled = true; }
                else if (e.Key == Key.Escape)
                {
                    // Back to the QNH in use (the box still has the focus, so ShowQnh would not write it).
                    qnhBox.Text = currentQnh() > 0 ? currentQnh().ToString(CultureInfo.InvariantCulture) : "";
                    qnhBox.SelectAll();
                    e.Handled = true;
                }
            };
            qnhBox.LostKeyboardFocus += (s, e) => ApplyQnhTyped();
            Wheel(qnhBox, 1, 5, 850, 1090, "0", ApplyQnhTyped, () => currentQnh() > 0 ? currentQnh() : 1013, afterClick: true);
            return row;
        }

        /// <summary>The QNH box (each refresh): grey with Aurora, the value in use when not being typed in.</summary>
        private void ShowQnh()
        {
            bool connected = auroraConnected();
            qnhBox.IsEnabled = !connected;
            if (connected && metarSource.Text.EndsWith("(internet)", StringComparison.Ordinal)) metarSource.Text = "";
            if (connected && metarSource.Text.Length == 0) metarSource.Text = "from Aurora";
            if (!connected && metarSource.Text == "from Aurora") metarSource.Text = "";
            if (qnhBox.IsKeyboardFocused) return;
            int qnh = currentQnh();
            string text = qnh > 0 ? qnh.ToString(CultureInfo.InvariantCulture) : "";
            if (qnhBox.Text != text)
            {
                qnhBox.Text = text;
                qnhBox.ClearValue(BackgroundProperty);
            }
        }

        /// <summary>The QNH typed (or set with the wheel): hPa 850–1090, or inHg (a value below 40) turned into hPa.</summary>
        private void ApplyQnhTyped()
        {
            if (auroraConnected() || setManualQnh == null) return;
            string text = qnhBox.Text.Trim().Replace(',', '.');
            if (text.Length == 0) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) { qnhBox.Background = WrongBack; return; }
            int hpa = (int)Math.Round(value < 40 ? value * 33.8639 : value);
            if (hpa < 850 || hpa > 1090) { qnhBox.Background = WrongBack; return; }
            qnhBox.ClearValue(BackgroundProperty);
            if (hpa == currentQnh()) return;
            setManualQnh(hpa);
            metarSource.Text = "typed";
            qnhBox.Text = hpa.ToString(CultureInfo.InvariantCulture);
        }

        private static readonly Brush WrongBack = new SolidColorBrush(Color.FromRgb(255, 215, 215));

        /// <summary>From METAR without Aurora: the METAR of the airport from the internet; its wind and its QNH.</summary>
        private async void WindFromInternet()
        {
            if (fetchingMetar) return;
            string icao = (bridge.Reference ?? radarRunway()).ICAO.Trim().ToUpperInvariant();
            fetchingMetar = true;
            metarSource.Text = $"asking the internet for {icao}…";
            string? metarText;
            try
            {
                metarText = await OnlineMetar.FetchAsync(icao);
            }
            finally
            {
                fetchingMetar = false;
            }
            if (metarText == null)
            {
                metarSource.Text = "";
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show(this, $"No METAR for {icao} from the internet (no connection, or no report for this airport). Type the wind and the QNH by hand.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MetarWind.TryParse(metarText, out double from, out double speed, out double gust))
            {
                if (!double.IsNaN(from)) windFromBox.Text = from.ToString("000", CultureInfo.InvariantCulture);
                windSpeedBox.Text = Math.Round(speed).ToString(CultureInfo.InvariantCulture);
                windGustBox.Text = Math.Round(gust).ToString(CultureInfo.InvariantCulture);
                ApplyWind();
            }
            if (OnlineMetar.QnhOf(metarText) is int hpa && !auroraConnected()) setManualQnh?.Invoke(hpa);
            metarSource.Text = $"METAR {icao} {OnlineMetar.TimeOf(metarText)} (internet)";
            metarSource.ToolTip = metarText;
            ShowQnh();
        }
    }
}
