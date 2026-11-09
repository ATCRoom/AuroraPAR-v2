using System.Windows;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: Panels LIGHT / DARK (the user, 9 Nov; Simulation settings, from the next start). DARK: the inside of
    /// the Instructor+ panels and of the Aircraft performance window in the colours of TWR Trainer's dark panels; Windows' own
    /// controls styled by Trainer/DarkPanels.xaml. LIGHT (the default) changes nothing.
    /// </summary>
    internal static class PanelTheme
    {
        private static bool started;
        private static ResourceDictionary? styles;

        /// <summary>DARK, fixed for the whole run (set once, at the first Instructor+ of the program).</summary>
        public static bool Dark { get; private set; }

        /// <summary>Taken once: a change in the settings applies from the next start.</summary>
        public static void Start(bool dark)
        {
            if (started) return;
            started = true;
            Dark = dark;
        }

        public static readonly Brush DarkBack = Frozen(Color.FromRgb(0x1A, 0x21, 0x28));
        private static readonly Brush DarkText = Frozen(Color.FromRgb(0xD6, 0xE2, 0xEC));

        /// <summary>The text of the rows of the lists (black on the light panels).</summary>
        public static Brush Text => Dark ? DarkText : Brushes.Black;

        private static readonly Brush DarkDim = Frozen(Color.FromRgb(0xA9, 0xB6, 0xC0));
        private static readonly Brush DarkWrong = Frozen(Color.FromRgb(0x6B, 0x2A, 0x2A));
        private static readonly Brush LightWrong = Frozen(Color.FromRgb(255, 215, 215));

        /// <summary>The dimmed texts (dim grey on the light panels).</summary>
        public static Brush Dim => Dark ? DarkDim : Brushes.DimGray;

        /// <summary>The background of a wrong value in a box (pink on the light panels, dark red on the dark ones).</summary>
        public static Brush Wrong => Dark ? DarkWrong : LightWrong;

        /// <summary>The styles of the controls inside a dark panel.</summary>
        public static ResourceDictionary Styles =>
            styles ??= (ResourceDictionary)Application.LoadComponent(new Uri("/AuroraPARTrainer;component/DarkPanels.xaml", UriKind.Relative));

        /// <summary>A dark panel (or window) from here down: its styles, its text and its background.</summary>
        public static void Apply(System.Windows.Controls.Control host)
        {
            if (!Dark) return;
            host.Resources.MergedDictionaries.Add(Styles);
            host.Foreground = DarkText;
            host.Background = DarkBack;
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }
    }
}
