using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer, Instructor+: the performance classes. "Aircraft performance…" (SIMULATION SETTINGS) and "…"
    /// beside a Class box open the table (<see cref="PerformanceWindow"/>); "Slow dwn to final" gives the selected
    /// aircraft the final speed of its class.
    /// </summary>
    internal sealed partial class InstructorWindow
    {
        /// <summary>The small "…" beside a Class box: the Aircraft performance window.</summary>
        private Button PerformanceButton()
        {
            Button button = new() { Content = "…", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(2, 0, 6, 0), Focusable = false, ToolTip = "Aircraft performance: the classes and their values." };
            button.Click += (s, e) => OpenPerformanceWindow();
            return button;
        }

        private void OpenPerformanceWindow()
        {
            PerformanceWindow window = new(bridge.Engine.Classes) { Owner = this };
            if (window.ShowDialog() != true || window.Result == null) return;
            string? problem = bridge.SavePerformance(window.Result);
            RefreshClassLists();
            if (problem != null) Warn(problem);
        }

        /// <summary>The Class boxes after the classes changed: the same choice when it is still there, else the Jet; plans of a class gone get the Jet.</summary>
        private void RefreshClassLists()
        {
            List<string> names = bridge.Engine.Classes.Select(c => c.Name).ToList();
            string? quick = classBox.SelectedItem as string;
            classBox.ItemsSource = names;
            classBox.SelectedItem = names.FirstOrDefault(n => string.Equals(n, quick, StringComparison.OrdinalIgnoreCase)) ?? ProposedClass();
            // The initial speed of the class (it may have changed).
            if (bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, classBox.SelectedItem as string, StringComparison.OrdinalIgnoreCase)) is PerformanceClass kind)
            {
                newSpeedBox.Text = kind.ApproachSpeedKt.ToString("0", CultureInfo.InvariantCulture);
            }
            foreach (FlightPlan plan in flightPlans)
            {
                if (!names.Contains(plan.Class, StringComparer.OrdinalIgnoreCase)) plan.Class = ProposedClass();
            }
            fplSync = true;
            try
            {
                fplClassBox.ItemsSource = names;
                fplClassBox.SelectedItem = SelectedFpl?.Class ?? ProposedClass();
            }
            finally
            {
                fplSync = false;
            }
            fplShownKey = trafficShownKey = "";
            Refresh();
        }

        /// <summary>The final speed of the class of an aircraft (null: its class is not known any more).</summary>
        private double? FinalSpeed(AircraftState state) =>
            bridge.Engine.Classes.FirstOrDefault(c => string.Equals(c.Name, state.PerformanceClass, StringComparison.OrdinalIgnoreCase))?.FinalSpeedKt;

        /// <summary>Slow dwn to final: the selected aircraft to the final speed of its class (as a speed given, after the pilot reaction).</summary>
        private void SlowToFinal()
        {
            if (Selected() is not AircraftState state || FinalSpeed(state) is not double speed)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            speedBox.Text = Math.Round(speed).ToString("0", CultureInfo.InvariantCulture);
            GiveSpeed();
        }
    }
}
