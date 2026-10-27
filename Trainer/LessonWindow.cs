using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// The lesson between two PCs: this PC is the instructor (its Instructor+ flies the aircraft) or the student (its PAR and
    /// RDR show them). The instructor makes a code and tells it to the student, who types it. The link itself belongs to
    /// the main window: this window only starts and stops it and shows its state.
    /// </summary>
    internal sealed class LessonWindow : Window
    {
        private readonly Func<LessonRole, string, string?> connect;
        private readonly Action disconnect;
        private readonly Func<LessonLink?> link;
        private readonly RadioButton instructorRadio = new() { Content = "Instructor: this PC flies the simulated aircraft (Instructor+)", Margin = new Thickness(0, 2, 0, 2) };
        private readonly RadioButton studentRadio = new() { Content = "Student: this PC shows them on its PAR and RDR", Margin = new Thickness(0, 2, 0, 2) };
        private readonly TextBox codeBox = new() { Width = 120, FontSize = 16, FontFamily = new FontFamily("Consolas"), CharacterCasing = CharacterCasing.Upper, MaxLength = CoordinationSettings.MaxCodeLength, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button newCodeButton = new() { Content = "New code", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        private readonly Button connectButton = new() { Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock stateText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold };

        /// <param name="role">Role and code of the last lesson (settings).</param>
        /// <param name="connect">Starts the lesson: an error to show, or null.</param>
        /// <param name="disconnect">Stops the lesson.</param>
        /// <param name="link">The lesson running, or null.</param>
        public LessonWindow(LessonRole role, string? code, Func<LessonRole, string, string?> connect, Action disconnect, Func<LessonLink?> link)
        {
            this.connect = connect;
            this.disconnect = disconnect;
            this.link = link;
            Title = "Lesson between two PCs";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel root = new() { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "The Instructor+ of one PC and the PAR / RDR of the student on another PC, also far away, linked through the " +
                       "internet (the public relays of the coordination panel: nothing to install, no port to open). Both PCs run " +
                       "AuroraPAR v2 Trainer. Talk to each other as usual (Discord, TeamSpeak, phone...).",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            root.Children.Add(new TextBlock { Text = "This PC is the", FontWeight = FontWeights.SemiBold });
            instructorRadio.GroupName = studentRadio.GroupName = "LessonRole";
            root.Children.Add(instructorRadio);
            root.Children.Add(studentRadio);
            if (role == LessonRole.Instructor) instructorRadio.IsChecked = true;
            else studentRadio.IsChecked = true;

            root.Children.Add(new TextBlock { Text = "Lesson code", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
            StackPanel codeRow = new() { Orientation = Orientation.Horizontal };
            codeBox.Text = code ?? "";
            codeRow.Children.Add(codeBox);
            codeRow.Children.Add(newCodeButton);
            root.Children.Add(codeRow);
            root.Children.Add(new TextBlock
            {
                Text = "The instructor makes a new code and tells it to the student, who types the same code. Whoever has the code can see the lesson.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 4, 0, 0)
            });
            root.Children.Add(connectButton);
            root.Children.Add(stateText);
            Content = root;

            newCodeButton.Click += (s, e) => codeBox.Text = CoordinationSettings.GenerateCode();
            instructorRadio.Checked += (s, e) => Refresh();
            studentRadio.Checked += (s, e) => Refresh();
            connectButton.Click += (s, e) => ConnectClicked();
            Refresh();
        }

        private void ConnectClicked()
        {
            if (link() != null)
            {
                disconnect();
                Refresh();
                return;
            }
            string? code = CoordinationSettings.NormalizeCode(codeBox.Text);
            if (code == null || code.Length < 4)
            {
                MessageBox.Show(this, "Type the lesson code (at least 4 letters or digits), or make a new one on the instructor's PC.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            codeBox.Text = code;
            string? error = connect(instructorRadio.IsChecked == true ? LessonRole.Instructor : LessonRole.Student, code);
            if (error != null) MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }

        /// <summary>Shows the state (called by the main window at every refresh).</summary>
        public void Refresh()
        {
            LessonLink? current = link();
            bool running = current != null;
            instructorRadio.IsEnabled = studentRadio.IsEnabled = codeBox.IsEnabled = !running;
            newCodeButton.IsEnabled = !running && instructorRadio.IsChecked == true;
            string button = running ? "Disconnect" : "Connect";
            if (connectButton.Content as string != button) connectButton.Content = button;
            string other = current?.Role == LessonRole.Instructor ? "student" : "instructor";
            (string text, Color colour) = current == null
                ? ("Not connected.", Colors.DimGray)
                : !current.Connected
                    ? ("Connecting to the relay...", Color.FromRgb(0xC0, 0x70, 0x00))
                    : current.OtherPresent
                        ? ($"Linked with the {other} (relay {current.RelayName}).", Color.FromRgb(0x10, 0x80, 0x20))
                        : current.EverHeard
                            ? ($"LINK LOST: the {other} is not heard (trying again by itself).", Color.FromRgb(0xC0, 0x20, 0x20))
                            : ($"Connected to the relay {current.RelayName}: waiting for the {other} (same code on both PCs).", Color.FromRgb(0xC0, 0x70, 0x00));
            if (stateText.Text != text)
            {
                stateText.Text = text;
                stateText.Foreground = new SolidColorBrush(colour);
            }
        }
    }
}
