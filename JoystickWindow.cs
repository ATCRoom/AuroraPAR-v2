using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// Joystick settings with a live test: device, what the stick moves, dead zone, and the command of each button
    /// (chosen in a list or by pressing the button). Changes are saved at once.
    /// </summary>
    internal sealed class JoystickWindow : Window
    {
        private const double BarWidth = 160;

        private static readonly (JoystickCommand Command, string Text)[] Commands =
        [
            (JoystickCommand.FinalCourse, "Final CRS"),
            (JoystickCommand.NormalRate, "Normal rate of descent (=)"),
            (JoystickCommand.ReduceRate, "Reduce rate of descent (−)"),
            (JoystickCommand.IncreaseRate, "Increase rate of descent (+)"),
            (JoystickCommand.OnPath, "On GP / CL"),
            (JoystickCommand.NextAircraft, "Next aircraft"),
            (JoystickCommand.Pause, "Pause / resume the Instructor's aircraft"),
            (JoystickCommand.TurnRate, "Next turn rate (1.5°/s, 3°/s, Free)"),
            (JoystickCommand.TiltUp, "Antenna tilt up"),
            (JoystickCommand.TiltDown, "Antenna tilt down"),
            (JoystickCommand.TiltLeft, "Antenna tilt left"),
            (JoystickCommand.TiltRight, "Antenna tilt right"),
            (JoystickCommand.TiltNeutral, "Antenna neutral"),
            (JoystickCommand.SwitchRole, "Switch stick: aircraft / antenna")
        ];

        private readonly AppSettings settings;
        private JoystickSettings Options => settings.Joystick;
        private readonly CheckBox enabledCheck = new() { Content = "Use a joystick or gamepad (USB, no driver needed)", FontWeight = FontWeights.SemiBold };
        private readonly ComboBox deviceBox = new() { Width = 260, Height = 24 };
        private readonly TextBlock status = new() { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private readonly Rectangle xBar = new() { Height = 10, Fill = Brushes.SteelBlue };
        private readonly Rectangle yBar = new() { Height = 10, Fill = Brushes.SteelBlue };
        private readonly Canvas xCanvas = new() { Width = BarWidth, Height = 10, Background = Brushes.Gainsboro };
        private readonly Canvas yCanvas = new() { Width = BarWidth, Height = 10, Background = Brushes.Gainsboro };
        private readonly TextBlock otherAxes = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock pressed = new() { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly RadioButton automaticRadio = new() { Content = "Automatic: the selected aircraft while an Instructor window is open, else the antenna", GroupName = "JoystickRole" };
        private readonly RadioButton antennaRadio = new() { Content = "Always the antenna tilt", GroupName = "JoystickRole" };
        private readonly RadioButton aircraftRadio = new() { Content = "Always the Instructor's aircraft", GroupName = "JoystickRole" };
        private readonly ComboBox deadZoneBox = new() { Width = 70 };
        private readonly CheckBox pullCheck = new() { Content = "Aircraft: pull back to climb / reduce the descent (as in an aircraft)", Margin = new Thickness(0, 6, 0, 0) };
        private readonly Dictionary<JoystickCommand, ComboBox> inputBoxes = [];
        private readonly Dictionary<JoystickCommand, Button> detectButtons = [];
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private JoystickCommand? detecting;
        private DateTime detectEnd;
        private HashSet<int> detectIgnore = [];
        private bool filling;

        internal JoystickWindow(AppSettings settings)
        {
            this.settings = settings;
            Title = "Aurora PAR - Joystick";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel root = new() { Margin = new Thickness(10), Width = 520 };
            root.Children.Add(enabledCheck);

            // Device and live test.
            StackPanel devicePanel = new();
            StackPanel deviceRow = new() { Orientation = Orientation.Horizontal };
            deviceRow.Children.Add(deviceBox);
            Button rescan = new() { Content = "Search again", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            rescan.Click += (s, e) => Joystick.Rescan(force: true);
            deviceRow.Children.Add(rescan);
            devicePanel.Children.Add(deviceRow);
            devicePanel.Children.Add(status);
            Grid bars = new() { Margin = new Thickness(0, 6, 0, 0) };
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bars.RowDefinitions.Add(new RowDefinition());
            bars.RowDefinitions.Add(new RowDefinition());
            AddBar(bars, 0, "X (left / right)", xCanvas, xBar);
            AddBar(bars, 1, "Y (back / forward)", yCanvas, yBar);
            devicePanel.Children.Add(bars);
            devicePanel.Children.Add(otherAxes);
            devicePanel.Children.Add(pressed);
            root.Children.Add(Group("Device (live test)", devicePanel));

            // Stick.
            StackPanel stick = new();
            stick.Children.Add(automaticRadio);
            stick.Children.Add(antennaRadio);
            stick.Children.Add(aircraftRadio);
            StackPanel deadRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            deadRow.Children.Add(new TextBlock { Text = "Dead zone around the centre:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            deadRow.Children.Add(deadZoneBox);
            stick.Children.Add(deadRow);
            stick.Children.Add(pullCheck);
            stick.Children.Add(new TextBlock
            {
                Text = "Antenna: forward / back tilts up / down, left / right tilts left / right (one step, repeated while held).",
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            root.Children.Add(Group("The stick moves", stick));

            // Buttons.
            Grid commands = new();
            commands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            List<string> inputNames = Enumerable.Range(0, Joystick.InputCount + 1).Select(Joystick.InputName).ToList();
            for (int i = 0; i < Commands.Length; i++)
            {
                JoystickCommand command = Commands[i].Command;
                commands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = new() { Text = Commands[i].Text, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, i);
                commands.Children.Add(label);
                ComboBox box = new() { ItemsSource = inputNames, Width = 90, Margin = new Thickness(8, 2, 0, 2) };
                box.SelectionChanged += (s, e) =>
                {
                    if (filling) return;
                    Options.Assign(command, Math.Max(0, box.SelectedIndex));
                    Save();
                    FillInputs();
                };
                Grid.SetRow(box, i);
                Grid.SetColumn(box, 1);
                commands.Children.Add(box);
                inputBoxes[command] = box;
                Button detect = new() { Content = "Press...", Width = 110, Margin = new Thickness(6, 2, 0, 2), ToolTip = "Then press the joystick button (or the hat direction) for this command within 5 s." };
                detect.Click += (s, e) => StartDetect(command);
                Grid.SetRow(detect, i);
                Grid.SetColumn(detect, 2);
                commands.Children.Add(detect);
                detectButtons[command] = detect;
            }
            StackPanel buttonsPanel = new();
            buttonsPanel.Children.Add(commands);
            Button defaults = new() { Content = "Default buttons", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            defaults.Click += (s, e) =>
            {
                Options.Buttons = JoystickSettings.DefaultButtons();
                Save();
                FillInputs();
            };
            buttonsPanel.Children.Add(defaults);
            root.Children.Add(Group("Buttons and hat", buttonsPanel));

            Button close = new() { Content = "Close", Width = 80, Height = 26, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
            close.Click += (s, e) => Close();
            root.Children.Add(close);
            Content = root;

            // Values.
            foreach (int percent in new[] { 0, 5, 10, 15, 20, 25, 30 }) deadZoneBox.Items.Add($"{percent} %");
            filling = true;
            enabledCheck.IsChecked = Options.Enabled;
            (Options.Role switch
            {
                JoystickRole.AntennaTilt => antennaRadio,
                JoystickRole.TestAircraft => aircraftRadio,
                _ => automaticRadio
            }).IsChecked = true;
            deadZoneBox.SelectedIndex = Math.Clamp((int)Math.Round(Options.DeadZone * 20), 0, deadZoneBox.Items.Count - 1);
            pullCheck.IsChecked = Options.PullToClimb;
            filling = false;
            FillInputs();
            FillDevices();

            enabledCheck.Click += (s, e) =>
            {
                Options.Enabled = enabledCheck.IsChecked == true;
                Save();
            };
            automaticRadio.Checked += (s, e) => SetRole(JoystickRole.Automatic);
            antennaRadio.Checked += (s, e) => SetRole(JoystickRole.AntennaTilt);
            aircraftRadio.Checked += (s, e) => SetRole(JoystickRole.TestAircraft);
            deadZoneBox.SelectionChanged += (s, e) =>
            {
                if (filling || deadZoneBox.SelectedIndex < 0) return;
                Options.DeadZone = deadZoneBox.SelectedIndex * 0.05;
                Save();
            };
            pullCheck.Click += (s, e) =>
            {
                Options.PullToClimb = pullCheck.IsChecked == true;
                Save();
            };
            deviceBox.SelectionChanged += (s, e) =>
            {
                if (filling || deviceBox.SelectedItem is not string name) return;
                Options.Device = deviceBox.SelectedIndex == 0 ? null : name;
                Save();
            };

            timer.Tick += (s, e) => Live();
            timer.Start();
            Closed += (s, e) =>
            {
                timer.Stop();
                JoystickController.Suspended = false;
            };
            Joystick.Rescan();
        }

        private static GroupBox Group(string header, UIElement content) => new()
        {
            Header = header,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            Content = content
        };

        private static void AddBar(Grid grid, int row, string text, Canvas canvas, Rectangle bar)
        {
            TextBlock label = new() { Text = text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            canvas.Children.Add(new Line { X1 = BarWidth / 2, X2 = BarWidth / 2, Y1 = 0, Y2 = 10, Stroke = Brushes.Gray });
            canvas.Children.Add(bar);
            canvas.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(canvas, row);
            Grid.SetColumn(canvas, 1);
            grid.Children.Add(canvas);
        }

        private void SetRole(JoystickRole role)
        {
            if (filling) return;
            Options.Role = role;
            Save();
        }

        private void Save() => SettingsStore.Save(settings);

        private void FillInputs()
        {
            filling = true;
            foreach ((JoystickCommand command, ComboBox box) in inputBoxes) box.SelectedIndex = Options.InputOf(command);
            filling = false;
        }

        /// <summary>The list of devices, kept up to date as they are connected or removed.</summary>
        private void FillDevices()
        {
            List<string> names = ["First found"];
            names.AddRange(Joystick.Devices.Select(d => d.Name));
            if (Options.Device != null && !names.Contains(Options.Device)) names.Add(Options.Device);
            if (deviceBox.ItemsSource is List<string> current && current.SequenceEqual(names)) return;
            filling = true;
            deviceBox.ItemsSource = names;
            deviceBox.SelectedItem = Options.Device ?? names[0];
            filling = false;
        }

        private void StartDetect(JoystickCommand command)
        {
            StopDetect();
            Joystick.Device? device = Joystick.Find(Options.Device);
            Joystick.State? state = device != null ? Joystick.Read(device) : null;
            if (state == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            // Inputs already held do not count: the next one pressed is taken.
            detectIgnore = state.Pressed().ToHashSet();
            detecting = command;
            detectEnd = DateTime.UtcNow.AddSeconds(5);
            detectButtons[command].Content = "Press a button...";
            detectButtons[command].FontWeight = FontWeights.SemiBold;
            JoystickController.Suspended = true;
        }

        private void StopDetect()
        {
            if (detecting is JoystickCommand command)
            {
                detectButtons[command].Content = "Press...";
                detectButtons[command].FontWeight = FontWeights.Normal;
            }
            detecting = null;
            JoystickController.Suspended = false;
        }

        private void Live()
        {
            FillDevices();
            Joystick.Device? device = Joystick.Find(Options.Device);
            Joystick.State? state = device != null ? Joystick.Read(device) : null;
            if (device == null || state == null)
            {
                status.Text = "No joystick found: connect one and wait a few seconds (or press Search again).";
                ShowBar(xBar, 0);
                ShowBar(yBar, 0);
                otherAxes.Text = "";
                pressed.Text = "";
                if (detecting != null && DateTime.UtcNow > detectEnd) StopDetect();
                return;
            }
            status.Text = $"In use: {device.Name} — {device.Buttons} buttons{(device.HasPov ? ", hat" : "")}.";
            ShowBar(xBar, state.X);
            ShowBar(yBar, state.Y);
            string[] axisNames = ["X", "Y", "Z", "R", "U", "V"];
            List<string> others = [];
            for (int i = 2; i < 6; i++)
            {
                if (device.HasAxis[i]) others.Add($"{axisNames[i]} {state.Axes[i]:+0.00;−0.00;0.00}");
            }
            otherAxes.Text = others.Count > 0 ? "Other axes (not used): " + string.Join("   ", others) : "";
            List<int> down = state.Pressed().ToList();
            pressed.Text = down.Count == 0 ? "Pressed: none" : "Pressed: " + string.Join(", ", down.Select(Joystick.InputName));
            if (detecting is JoystickCommand command)
            {
                int input = down.FirstOrDefault(i => !detectIgnore.Contains(i));
                detectIgnore.IntersectWith(down);
                if (input > 0)
                {
                    Options.Assign(command, input);
                    Save();
                    FillInputs();
                    StopDetect();
                }
                else if (DateTime.UtcNow > detectEnd)
                {
                    StopDetect();
                }
            }
        }

        private static void ShowBar(Rectangle bar, double value)
        {
            double half = BarWidth / 2;
            double width = Math.Abs(value) * half;
            bar.Width = width;
            Canvas.SetLeft(bar, value >= 0 ? half : half - width);
        }
    }
}
