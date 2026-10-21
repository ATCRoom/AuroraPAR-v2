using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// The surveillance radar (RDR) in its own window, for a second monitor. It has no owner, so it can stay on the
    /// other monitor and the main window can go in front of it. R: the main window to the front.
    /// </summary>
    internal sealed class RadarWindow : Window
    {
        /// <summary>Key R: the main window to the front.</summary>
        public event Action? SwitchRequested;
        /// <summary>Page up / Page down: one step of the range list (+1 larger).</summary>
        public event Action<int>? RangeStepRequested;

        /// <summary>Where the window was when it closed (null while open).</summary>
        public WindowPlacement? LastPlacement { get; private set; }

        public RadarWindow(RadarPanel panel, WindowPlacement? placement, ImageSource? icon)
        {
            Title = $"{AppInfo.ProductName} ({AppInfo.Version}) - RDR";
            Background = Brushes.Black;
            if (icon != null) Icon = icon;
            Width = 900;
            Height = 700;
            MinWidth = 300;
            MinHeight = 250;
            if (placement != null && IsOnScreen(placement))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = placement.Left;
                Top = placement.Top;
                Width = Math.Max(MinWidth, placement.Width);
                Height = Math.Max(MinHeight, placement.Height);
                if (placement.Maximized) Loaded += (s, e) => WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            panel.InWindow = true;
            panel.Visibility = Visibility.Visible;
            Content = panel;
            PreviewKeyDown += (s, e) =>
            {
                if (Keyboard.FocusedElement is TextBox) return;
                if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
                switch (e.Key)
                {
                    case Key.R: SwitchRequested?.Invoke(); break;
                    case Key.PageUp: RangeStepRequested?.Invoke(1); break;
                    case Key.PageDown: RangeStepRequested?.Invoke(-1); break;
                    default: return;
                }
                e.Handled = true;
            };
            Closing += (s, e) =>
            {
                Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
                LastPlacement = new WindowPlacement
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Maximized = WindowState == WindowState.Maximized
                };
            };
            // The panel goes back to its owner (the main window can show it again).
            Closed += (s, e) => Content = null;
        }

        /// <summary>At least a good part of the title bar is on the screens (a monitor may have been disconnected).</summary>
        private static bool IsOnScreen(WindowPlacement p)
        {
            if (!double.IsFinite(p.Left) || !double.IsFinite(p.Top) || !double.IsFinite(p.Width) || !double.IsFinite(p.Height)) return false;
            Rect screens = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Rect titleBar = new(p.Left, p.Top, Math.Max(1, p.Width), 30);
            titleBar.Intersect(screens);
            return !titleBar.IsEmpty && titleBar.Width >= 100;
        }
    }
}
