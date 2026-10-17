using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>Help: the user manual (online) and the list of the keyboard and mouse shortcuts.</summary>
    internal static class Help
    {
        /// <summary>User manual. Changes with the move to ATCRoom (see TODO.md, migration).</summary>
        public const string ManualUrl = "https://github.com/altanico/AuroraPAR-v2/blob/master/docs/MANUAL.md";

        /// <summary>
        /// All the keyboard and mouse shortcuts, by group. The one list shown by the Keys window: when a key is added
        /// to MainWindow_PreviewKeyDown (or a mouse action), add it here too (and to the manual, section 6).
        /// </summary>
        public static readonly (string Group, (string Keys, string Action)[] Items)[] Shortcuts =
        [
            ("Range", [
                ("Mouse wheel over the display", "Range up / down"),
                ("Page up / Page down", "Range larger / smaller"),
                ("End", "Preferred range (as the PREF key)")
            ]),
            ("Antenna tilt", [
                ("↑ / ↓", "Elevation tilt up / down"),
                ("← / →", "Azimuth tilt left / right"),
                ("Home", "Antenna back to neutral")
            ]),
            ("Decision height, brightness and course", [
                ("Shift+↑ / Shift+↓", "Decision height 10 ft higher / lower"),
                ("Shift+Home", "Decision height of the runway"),
                ("Ctrl+↑ / Ctrl+↓", "Brightness up / down"),
                ("Ctrl+Home", "Brightness 100%"),
                ("Alt+← / Alt+→", "Final course 1° less / more, this session (CRS selector)"),
                ("Alt+Home", "Final course of the runway")
            ]),
            ("Display", [
                ("A", "Modern / analog display (unless locked in the profile)"),
                ("P / Shift+P", "Next / previous profile"),
                ("C", "Linear / log distance scale"),
                ("S", "Track smoothing On / Off (this session only)"),
                ("X", "Deviation crosshair on / off (modern display, saved in the profile)"),
                ("O", "Terrain / obstacles: stepped line → filled → hidden (modern display; saved in the profile)"),
                ("M", "MTI filter on / off: ground clutter weaker / full (this session)"),
                ("L", "Hide / show all labels")
            ]),
            ("Labels (modern display)", [
                ("Drag a label", "Move it (a leader line joins it to its track)"),
                ("Click on a track or its label", "Select it for the deviation crosshair (again: back to the aircraft closest to the runway)"),
                ("Double click on a label", "Back to its place"),
                ("Right click on a label", "Hide it (with a Track ID field: menu with Set ID...)"),
                ("Right click near a track", "Hide / show its label (several tracks: a menu)")
            ]),
            ("Windows", [
                ("T", "Test traffic"),
                ("1 – 5 / 0", "Coordination panel (must be open): press lights 1–5 / Reset"),
                ("F1", "This list of keys"),
                ("Esc / Enter in a text box", "Cancel / confirm what is typed")
            ]),
            ("Analog console", [
                ("Drag a knob up / down", "Turn it"),
                ("Double click on a knob", "Back to neutral / runway DH / 100%")
            ])
        ];

        /// <summary>Opens the manual in the browser; false if it cannot be opened (no browser).</summary>
        public static bool OpenManual()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ManualUrl) { UseShellExecute = true });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>The keyboard and mouse shortcuts (Keys button, F1), built from <see cref="Help.Shortcuts"/>.</summary>
    internal sealed class ShortcutsWindow : Window
    {
        internal ShortcutsWindow()
        {
            Title = $"AuroraPAR v2 {AppInfo.FullVersion} — keys and mouse";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Grid grid = new() { Margin = new Thickness(16, 12, 16, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            foreach ((string group, (string Keys, string Action)[] items) in Help.Shortcuts)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock header = new()
                {
                    Text = group,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 2)
                };
                Grid.SetRow(header, row++);
                Grid.SetColumnSpan(header, 2);
                grid.Children.Add(header);
                foreach ((string keys, string action) in items)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    TextBlock key = new() { Text = keys, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(8, 1, 24, 1) };
                    TextBlock what = new() { Text = action, Margin = new Thickness(0, 1, 0, 1) };
                    Grid.SetRow(key, row);
                    Grid.SetRow(what, row++);
                    Grid.SetColumn(what, 1);
                    grid.Children.Add(key);
                    grid.Children.Add(what);
                }
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock note = new()
            {
                Text = "Keys are ignored while typing in a text box. Joystick / gamepad: Settings → Joystick...",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(note, row++);
            Grid.SetColumnSpan(note, 2);
            grid.Children.Add(note);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 4) };
            Button manual = new() { Content = "User manual", Width = 100, Height = 26, Margin = new Thickness(0, 0, 8, 0) };
            manual.Click += (s, e) => Help.OpenManual();
            Button close = new() { Content = "Close", Width = 80, Height = 26, IsCancel = true };
            close.Click += (s, e) => Close();
            buttons.Children.Add(manual);
            buttons.Children.Add(close);
            Grid.SetRow(buttons, row);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);
            Content = grid;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key is Key.Escape or Key.F1)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }
    }
}
