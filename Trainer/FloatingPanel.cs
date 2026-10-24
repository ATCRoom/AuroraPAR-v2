using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: a panel floating over the plan view (on a <see cref="Canvas"/>). Moved by
    /// dragging its bar, closed to the bar alone and opened again (▾ / ▴), back to its first place with a double-click on
    /// the bar; a resizable one (the mini-PAR) has a grip in its bottom right corner. It always stays inside the view:
    /// when the window becomes smaller it comes back in, and a content taller than the room left under it scrolls. A
    /// click on a panel brings it to the front. Place, open / closed and size are kept in a <see cref="PanelPlacement"/>.
    /// </summary>
    internal sealed class FloatingPanel : Border
    {
        private static readonly Brush BarBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x3E, 0x52)));
        private static readonly Brush BarText = Freeze(new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDA)));
        private static int topZ;

        private readonly PanelPlacement placement;
        private readonly Func<Size, Size, Point> firstPlace;
        private readonly Action changed;
        private readonly Border bar;
        private readonly ScrollViewer contentHost;
        private readonly FrameworkElement? sized;
        private readonly Button toggle;
        private Point? dragStart;
        private bool followsCanvas;
        private Point dragFrom;

        /// <param name="title">The name on the bar (capitals).</param>
        /// <param name="content">What the panel shows.</param>
        /// <param name="barExtra">Something on the right of the bar (a summary, a button), or null.</param>
        /// <param name="width">The width of the panel (a resizable one: its first width).</param>
        /// <param name="placement">Where it is, open or not, its size (changed here, saved by the owner in <paramref name="changed"/>).</param>
        /// <param name="firstPlace">Its first place, from the size of the view and of the panel.</param>
        /// <param name="resizable">With a grip to change its size: then <paramref name="content"/> gets the size.</param>
        /// <param name="height">The first height of the content of a resizable panel.</param>
        public FloatingPanel(string title, FrameworkElement content, FrameworkElement? barExtra, double width, PanelPlacement placement,
            Func<Size, Size, Point> firstPlace, Action changed, bool resizable = false, double height = 250)
        {
            this.placement = placement;
            this.firstPlace = firstPlace;
            this.changed = changed;
            Background = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
            BorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5D, 0x7A, 0x99)));
            // A clear border instead of a shadow (a shadow is drawn again at each refresh and blurs the text).
            BorderThickness = new Thickness(1.5);
            CornerRadius = new CornerRadius(4);
            if (resizable)
            {
                placement.Width = placement.Width is double w && double.IsFinite(w) ? Math.Max(MinWidthResizable, w) : width;
                placement.Height = placement.Height is double h && double.IsFinite(h) ? Math.Max(MinHeightResizable, h) : height;
            }
            Width = resizable ? placement.Width!.Value : width;

            // The bar: grip, title, the extra part, open / close.
            DockPanel barRow = new() { LastChildFill = true };
            toggle = new Button
            {
                FontSize = 9,
                Width = 18,
                Height = 16,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                Focusable = false,
                Foreground = Brushes.White,
                Background = BarBrush,
                BorderBrush = BarText,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Click += (s, e) => SetOpen(!placement.Open);
            DockPanel.SetDock(toggle, Dock.Right);
            barRow.Children.Add(toggle);
            if (barExtra != null)
            {
                DockPanel.SetDock(barExtra, Dock.Right);
                barRow.Children.Add(barExtra);
            }
            StackPanel name = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = "⋮⋮", Foreground = BarText, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            barRow.Children.Add(name);
            bar = new Border
            {
                Background = BarBrush,
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                Padding = new Thickness(6, 3, 4, 3),
                MinHeight = 24,
                Cursor = Cursors.SizeAll,
                Child = barRow,
                ToolTip = "Drag to move; ▴ / ▾ closes it to this bar or opens it; double-click: back to its first place."
            };
            bar.MouseLeftButtonDown += BarDown;
            bar.MouseMove += BarMove;
            bar.MouseLeftButtonUp += BarUp;
            // The capture lost while dragging (another window): where it is now is kept.
            bar.LostMouseCapture += (s, e) =>
            {
                if (dragStart == null) return;
                dragStart = null;
                changed();
            };

            // The content (scrolls when the room under the panel is less than its height).
            FrameworkElement inner = content;
            if (resizable)
            {
                sized = content;
                Grid grid = new();
                grid.Children.Add(content);
                Thumb grip = new()
                {
                    Width = 14,
                    Height = 14,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Cursor = Cursors.SizeNWSE,
                    Background = BarText,
                    Opacity = 0.7,
                    ToolTip = "Drag to change the size."
                };
                grip.DragDelta += (s, e) =>
                {
                    // From the size it has now (not the size asked), never more than the room to the right and below.
                    if (Parent is not Canvas canvas || content.ActualHeight <= 0) return;
                    double left = Canvas.GetLeft(this);
                    double top = Canvas.GetTop(this);
                    double roomRight = canvas.ActualWidth - (double.IsFinite(left) ? left : 0);
                    double roomBelow = canvas.ActualHeight - (double.IsFinite(top) ? top : 0) - Math.Max(24, bar.ActualHeight) - 4;
                    placement.Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinWidthResizable, Math.Max(MinWidthResizable, roomRight));
                    placement.Height = Math.Clamp(content.ActualHeight + e.VerticalChange, MinHeightResizable, Math.Max(MinHeightResizable, roomBelow));
                    Fit();
                };
                grip.DragCompleted += (s, e) => changed();
                grid.Children.Add(grip);
                inner = grid;
            }
            else
            {
                inner = new Border { Padding = new Thickness(7, 5, 7, 7), Child = content };
            }
            contentHost = new ScrollViewer
            {
                Content = inner,
                VerticalScrollBarVisibility = resizable ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            DockPanel layout = new();
            DockPanel.SetDock(bar, Dock.Top);
            layout.Children.Add(bar);
            layout.Children.Add(contentHost);
            Child = layout;

            PreviewMouseDown += (s, e) => BringToFront();
            SizeChanged += (s, e) => Fit();
            Loaded += (s, e) =>
            {
                if (!followsCanvas && Parent is Canvas canvas)
                {
                    followsCanvas = true;
                    canvas.SizeChanged += (_, _) => Fit();
                }
                Fit();
            };
            ShowOpen();
        }

        private const double MinWidthResizable = 300;
        private const double MinHeightResizable = 140;

        public bool IsOpen => placement.Open;

        private double PlacedWidth => placement.Width is double w && double.IsFinite(w) ? w : MinWidthResizable;
        private double PlacedHeight => placement.Height is double h && double.IsFinite(h) ? h : MinHeightResizable;

        public void SetOpen(bool open)
        {
            placement.Open = open;
            ShowOpen();
            changed();
        }

        private void ShowOpen()
        {
            contentHost.Visibility = placement.Open ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = placement.Open ? "▴" : "▾";
            toggle.ToolTip = placement.Open ? "Close to the bar." : "Open.";
        }

        private void BringToFront() => Panel.SetZIndex(this, ++topZ);

        private void BarDown(object sender, MouseButtonEventArgs e)
        {
            if (Parent is not Canvas canvas) return;
            if (e.ClickCount >= 2)
            {
                // Back to the first place.
                placement.Left = placement.Top = null;
                Fit();
                changed();
                e.Handled = true;
                return;
            }
            dragStart = e.GetPosition(canvas);
            double fromLeft = Canvas.GetLeft(this);
            double fromTop = Canvas.GetTop(this);
            dragFrom = new Point(double.IsFinite(fromLeft) ? fromLeft : 0, double.IsFinite(fromTop) ? fromTop : 0);
            bar.CaptureMouse();
            e.Handled = true;
        }

        private void BarMove(object sender, MouseEventArgs e)
        {
            if (dragStart is not Point start || Parent is not Canvas canvas) return;
            Vector moved = e.GetPosition(canvas) - start;
            placement.Left = dragFrom.X + moved.X;
            placement.Top = dragFrom.Y + moved.Y;
            Fit();
        }

        private void BarUp(object sender, MouseButtonEventArgs e)
        {
            if (dragStart == null) return;
            dragStart = null;
            bar.ReleaseMouseCapture();
            changed();
            e.Handled = true;
        }

        /// <summary>The panel inside the view: its place (the first one when it has none), the room for its content, its size.</summary>
        public void Fit()
        {
            if (Parent is not Canvas canvas || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double barHeight = Math.Max(24, bar.ActualHeight);
            if (sized != null) Width = Math.Min(PlacedWidth, Math.Max(MinWidthResizable, width - 8));
            // No place of its own yet: the first place, found again each time (the mini-PAR stays at the bottom right
            // when the window changes) until the panel is moved.
            if (placement.Left is not double placedLeft || placement.Top is not double placedTop || !double.IsFinite(placedLeft) || !double.IsFinite(placedTop))
            {
                Point first = firstPlace(new Size(width, height), new Size(Width, sized != null ? PlacedHeight + barHeight : barHeight));
                placedLeft = first.X;
                placedTop = first.Y;
            }
            // Always the whole bar inside the view (and a little of the content of an open panel).
            double left = Math.Clamp(placedLeft, 0, Math.Max(0, width - Width));
            double top = Math.Clamp(placedTop, 0, Math.Max(0, height - barHeight - (placement.Open ? 60 : 0)));
            Canvas.SetLeft(this, left);
            Canvas.SetTop(this, top);
            // The room under the bar: a taller content scrolls (a resizable one becomes lower).
            double room = Math.Max(60, height - top - barHeight - 4);
            if (sized != null)
            {
                sized.Width = Math.Max(0, Width - 2);
                sized.Height = Math.Min(PlacedHeight, room);
            }
            else
            {
                contentHost.MaxHeight = room;
            }
        }

        private static T Freeze<T>(T item) where T : Freezable
        {
            item.Freeze();
            return item;
        }
    }
}
