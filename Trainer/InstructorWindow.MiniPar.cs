using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the column of the plan view (with its CENTRE selector) and, under it, the
    /// mini-PAR, with a splitter between them (its height saved) and "PAR ▴ Hide" / "PAR ▾ Show" (saved).
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private MiniParView? miniPar;

        private Grid BuildViewColumn(PlanView view, MiniParView par)
        {
            Grid column = new();
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 });
            RowDefinition splitterRow = new() { Height = GridLength.Auto };
            RowDefinition parRow = new() { MinHeight = 0 };
            column.RowDefinitions.Add(splitterRow);
            column.RowDefinitions.Add(parRow);

            Grid planHost = new();
            planHost.Children.Add(view);
            planHost.Children.Add(new CentreBar(view));
            Button show = OverlayButton("PAR ▾ Show", "Show the mini-PAR under the plan view.");
            show.HorizontalAlignment = HorizontalAlignment.Left;
            show.VerticalAlignment = VerticalAlignment.Bottom;
            show.Margin = new Thickness(10, 0, 0, 10);
            planHost.Children.Add(show);
            column.Children.Add(planHost);

            GridSplitter splitter = new()
            {
                Height = 7,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x45)),
                ToolTip = "Drag: more room for the plan view or for the mini-PAR."
            };
            Grid.SetRow(splitter, 1);
            column.Children.Add(splitter);

            Grid parHost = new();
            parHost.Children.Add(par);
            Button hide = OverlayButton("PAR ▴ Hide", "Hide the mini-PAR: the plan view takes the whole height.");
            hide.HorizontalAlignment = HorizontalAlignment.Right;
            hide.VerticalAlignment = VerticalAlignment.Top;
            hide.Margin = new Thickness(0, 4, 10, 0);
            parHost.Children.Add(hide);
            Grid.SetRow(parHost, 2);
            column.Children.Add(parHost);

            void Apply()
            {
                bool shown = settings.MiniParShown;
                double height = double.IsFinite(settings.MiniParHeight) ? Math.Clamp(settings.MiniParHeight, 120, 900) : 320;
                parRow.Height = shown ? new GridLength(height) : new GridLength(0);
                parHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                splitter.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                show.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            }
            splitter.DragCompleted += (s, e) =>
            {
                if (parRow.ActualHeight > 0) settings.MiniParHeight = Math.Round(parRow.ActualHeight);
                save();
            };
            hide.Click += (s, e) => { settings.MiniParShown = false; save(); Apply(); };
            show.Click += (s, e) => { settings.MiniParShown = true; save(); Apply(); };
            Apply();
            return column;
        }

        /// <summary>A button over the black views: light border and text, as those of the RDR.</summary>
        private static Button OverlayButton(string text, string tip) => new()
        {
            Content = text,
            ToolTip = tip,
            Focusable = false,
            Padding = new Thickness(10, 2, 10, 2),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x2A, 0x3A, 0x4A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xD8)),
            BorderThickness = new Thickness(1.5)
        };
    }
}
