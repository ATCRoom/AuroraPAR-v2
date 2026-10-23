using System.Windows;
using System.Windows.Controls;

namespace AuroraPAR
{
    /// <summary>AuroraPAR v2 Trainer: its own options in the Settings window (the bar at the bottom).</summary>
    public partial class SettingsWindow
    {
        partial void TrainerOptions()
        {
            if (JoystickButton.Parent is not Panel bar) return;
            TextBlock caption = new() { Text = "Instructor buttons", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) };
            ComboBox choice = new()
            {
                Width = 130,
                Height = 26,
                ItemsSource = new[] { "Instructor+ only", "Instructor only", "Both" },
                ToolTip = "Which Instructor buttons the main window shows. A button that is hidden also switches off its key (I, Shift+I) and its analog key."
            };
            choice.SelectedIndex = settings.Trainer.InstructorButtons switch { "Basic" => 1, "Both" => 2, _ => 0 };
            choice.SelectionChanged += (s, e) =>
            {
                settings.Trainer.InstructorButtons = choice.SelectedIndex switch { 1 => "Basic", 2 => "Both", _ => "Plus" };
                SettingsStore.Save(settings);
            };
            int at = bar.Children.IndexOf(JoystickButton) + 1;
            bar.Children.Insert(at, caption);
            bar.Children.Insert(at + 1, choice);
        }
    }
}
