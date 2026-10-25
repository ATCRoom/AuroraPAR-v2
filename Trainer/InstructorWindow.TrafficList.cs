using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the TRAFFIC LIST panel. The aircraft flying (a click selects one for AIRCRAFT
    /// CONTROL; II / ▶ pause or unpause it, ↺ starts the aircraft of a plan again, ✕ removes it) and, under them, those
    /// WAITING to start (flight plans sent with TFC LIST, aircraft of Quick sent with Move to TFC list): ▶ starts one,
    /// ✕ takes it out of the list. Under the lists: Start all, Pause / Resume (the whole simulation), Remove all aircraft.
    /// The waiting ones are not given to the radar of the student; the Instructor+ shows them grey on its plan view.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        private readonly TextBlock aircraftTitle = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        private readonly TextBlock trafficSummary = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDA)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        private readonly ListBox waitingList = new() { MinHeight = 22, MaxHeight = 130, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock waitingCaption = new() { Text = "WAITING  (▶ starts it)", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, Margin = new Thickness(2, 6, 0, 2) };
        private readonly Button startAllButton = new() { Content = "Start all", FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Starts all the aircraft waiting in the list." };
        /// <summary>What the waiting list shows (to rebuild it only when it changes).</summary>
        private string trafficShownKey = "";
        /// <summary>The waiting list is being rebuilt (its selection set by the program, not a click).</summary>
        private bool trafficSync;

        private FrameworkElement BuildTrafficList()
        {
            StackPanel panel = new();
            panel.Children.Add(new TextBlock { Text = AircraftHeader, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 2) });
            Detach(list);
            // As high as its rows (up to about seven, then it scrolls).
            list.Height = double.NaN;
            list.MinHeight = 24;
            list.MaxHeight = 150;
            list.FontSize = 12;
            list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.ToolTip = "Click: the aircraft for AIRCRAFT CONTROL.";
            panel.Children.Add(list);

            panel.Children.Add(waitingCaption);
            ScrollViewer.SetHorizontalScrollBarVisibility(waitingList, ScrollBarVisibility.Disabled);
            waitingList.SelectionChanged += (s, e) =>
            {
                if (trafficSync) return;
                // A flight plan waiting: its route in Traffic generation (to look at it or change it).
                if ((waitingList.SelectedItem as ListBoxItem)?.Tag is FlightPlan plan && !plan.Quick && flightPlans.Contains(plan)) SelectPlan(plan);
            };
            panel.Children.Add(waitingList);

            WrapPanel buttons = new() { Margin = new Thickness(0, 8, 0, 0) };
            startAllButton.Click += (s, e) => StartAll();
            buttons.Children.Add(startAllButton);
            Detach(pauseButton);
            pauseButton.FontWeight = FontWeights.SemiBold;
            pauseButton.Padding = new Thickness(8, 1, 8, 1);
            pauseButton.Margin = new Thickness(0, 0, 6, 0);
            pauseButton.Focusable = false;
            pauseButton.ToolTip = "Pause / Resume: the whole simulation stops and goes on.";
            buttons.Children.Add(pauseButton);
            Button removeAll = new() { Content = "Remove all aircraft", Padding = new Thickness(8, 1, 8, 1), ToolTip = "Removes all the aircraft, flying and waiting (after a question): the flight plans wait again in Traffic generation; the aircraft of Quick are gone." };
            removeAll.Click += (s, e) => RemoveAllAircraft();
            buttons.Children.Add(removeAll);
            panel.Children.Add(buttons);
            return panel;
        }

        /// <summary>Remove all aircraft: flying and waiting, after a question; the flight plans wait again in Traffic generation.</summary>
        private void RemoveAllAircraft()
        {
            int flying = bridge.Count;
            int waiting = flightPlans.Count(p => p.Queued && p.Active == null);
            if (flying + waiting == 0) return;
            string what = flying > 0 && waiting > 0 ? $"{flying} flying and {waiting} waiting aircraft" : flying > 0 ? $"{flying} flying aircraft" : $"{waiting} waiting aircraft";
            if (MessageBox.Show(this, $"Remove {what}? Their flight plans wait again in Traffic generation.", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            // Their tracks and history dots go from every view (the plans may start again under the same callsigns).
            forgetTracks?.Invoke(null);
            bridge.Clear();
            foreach (FlightPlan plan in flightPlans) plan.Queued = false;
            flightPlans.RemoveAll(p => p.Quick);
            fplShownKey = trafficShownKey = routeShownKey = "";
            Refresh();
        }

        /// <summary>Start all: each aircraft waiting in the list, in order.</summary>
        private void StartAll()
        {
            foreach (FlightPlan plan in flightPlans.Where(p => p.Queued && p.Active == null).ToList()) Activate(plan);
            trafficShownKey = "";
            Refresh();
        }

        /// <summary>A waiting aircraft out of the list: a flight plan waits again in Traffic generation, one of Quick is gone.</summary>
        private void Unqueue(FlightPlan plan)
        {
            plan.Queued = false;
            if (plan.Quick) flightPlans.Remove(plan);
            fplShownKey = trafficShownKey = routeShownKey = "";
            Refresh();
        }

        /// <summary>The summaries on the bars, the list of the waiting aircraft (each refresh; rebuilt only when it changes).</summary>
        private void RefreshTrafficList(AircraftState? selected)
        {
            aircraftTitle.Text = selected != null ? $"· {selected.Callsign}" : "";
            List<FlightPlan> waiting = flightPlans.Where(p => p.Queued && p.Active == null).ToList();
            int flying = bridge.Count;
            trafficSummary.Text = waiting.Count > 0 ? $"{flying} flying · {waiting.Count} waiting" : $"{flying} flying";
            startAllButton.IsEnabled = waiting.Count > 0;
            Visibility shown = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            waitingCaption.Visibility = waitingList.Visibility = shown;

            string Row(FlightPlan plan)
            {
                string name = plan.Callsign.Length > 0 ? plan.Callsign : "(next)";
                string what = plan.Quick ? "quick start" : plan.Points.Count == 1 ? "1 point" : $"{plan.Points.Count} points";
                return string.Format(CultureInfo.InvariantCulture, "{0,-8} {1,-10} {2}", name.Length > 8 ? name[..8] : name, plan.Class.Length > 10 ? plan.Class[..10] : plan.Class, what);
            }
            string key = string.Join("|", waiting.Select(p => $"{p.GetHashCode()}:{Row(p)}"));
            if (key == trafficShownKey) return;
            trafficShownKey = key;
            object? keep = (waitingList.SelectedItem as ListBoxItem)?.Tag;
            trafficSync = true;
            try
            {
                FillWaiting(waiting, Row, keep);
            }
            finally
            {
                trafficSync = false;
            }
        }

        private void FillWaiting(List<FlightPlan> waiting, Func<FlightPlan, string> Row, object? keep)
        {
            waitingList.Items.Clear();
            foreach (FlightPlan plan in waiting)
            {
                DockPanel row = new() { LastChildFill = true };
                Button remove = RowButton("✕");
                remove.ToolTip = plan.Quick ? "Out of the list (this aircraft is not created)." : "Out of the list: the flight plan waits again in Traffic generation.";
                remove.Click += (s, e) => Unqueue(plan);
                Button start = RowButton("▶");
                start.FontWeight = FontWeights.SemiBold;
                start.ToolTip = "Start: the aircraft starts flying now.";
                start.Click += (s, e) => { Activate(plan); trafficShownKey = ""; Refresh(); };
                foreach (Button button in new[] { remove, start })
                {
                    DockPanel.SetDock(button, Dock.Right);
                    row.Children.Add(button);
                }
                row.Children.Add(new TextBlock { Text = Row(plan), FontFamily = new FontFamily("Consolas"), Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                ListBoxItem item = new() { Content = row, Tag = plan, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                waitingList.Items.Add(item);
                if (ReferenceEquals(plan, keep)) item.IsSelected = true;
            }
        }
    }
}
