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
        private readonly Button startButton = new() { Padding = new Thickness(14, 4, 14, 4), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button copyButton = new() { Content = "COPY CODE", Padding = new Thickness(10, 4, 10, 4) };
        private readonly TextBlock codeText = new() { FontSize = 26, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox codeBox = new() { Width = 120, FontSize = 16, FontFamily = new FontFamily("Consolas"), CharacterCasing = CharacterCasing.Upper, MaxLength = CoordinationSettings.MaxCodeLength, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button joinButton = new() { Padding = new Thickness(16, 4, 16, 4), FontWeight = FontWeights.SemiBold };
        private readonly TextBlock stateText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold };

        /// <param name="role">Role of the last lesson (not used: the two sides are two parts of the window).</param>
        /// <param name="code">The code of the last lesson joined (the student's box).</param>
        /// <param name="connect">Starts the lesson: an error to show, or null.</param>
        /// <param name="disconnect">Stops the lesson.</param>
        /// <param name="link">The lesson running, or null.</param>
        public LessonWindow(LessonRole role, string? code, Func<LessonRole, string, string?> connect, Action disconnect, Func<LessonLink?> link,
            TrainerSettings trainer, Action save)
        {
            this.connect = connect;
            this.disconnect = disconnect;
            this.link = link;
            // (The user, 10 Nov: the same window and words as TWR Trainer: START LESSON makes the code, COPY CODE, JOIN.)
            Title = "Lesson between two PCs";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel root = new() { Margin = new Thickness(14) };
            TextBlock Title2(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
            TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(Title2("Instructor (this PC flies the aircraft)"));
            root.Children.Add(Note("Start the lesson and give its code to the student. The maps of the airport go to him at the start (kept in memory only, never saved on his PC), then the simulated aircraft. Nothing to install, no ports: public relays on the internet, every message encrypted with the code. Talk to each other as usual (Discord, TeamSpeak, phone...)."));
            StackPanel instructorRow = new() { Orientation = Orientation.Horizontal };
            instructorRow.Children.Add(startButton);
            instructorRow.Children.Add(copyButton);
            root.Children.Add(instructorRow);
            root.Children.Add(codeText);

            root.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 12) });
            root.Children.Add(Title2("Student (this PC shows them on its PAR and RDR)"));
            root.Children.Add(Note("Write the code of the instructor and JOIN. IVAO's traffic: from your own Aurora. Both PCs run the same version of AuroraPAR v2 Trainer."));
            StackPanel studentRow = new() { Orientation = Orientation.Horizontal };
            codeBox.Text = role == LessonRole.Student ? code ?? "" : "";
            studentRow.Children.Add(codeBox);
            studentRow.Children.Add(joinButton);
            root.Children.Add(studentRow);
            root.Children.Add(stateText);

            // My callsign and VID for the log (the user, 9 and 10 Nov): the same as in Settings › Network of the Instructor+
            // (here too: the student does not open the Instructor+).
            root.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 8) });
            TextBox callsignBox = new() { Width = 100, MaxLength = 10, CharacterCasing = CharacterCasing.Upper, Text = trainer.MyCallsign, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
            TextBox vidBox = new() { Width = 90, MaxLength = 10, Text = trainer.MyVid, VerticalContentAlignment = VerticalAlignment.Center };
            StackPanel meRow = new() { Orientation = Orientation.Horizontal };
            meRow.Children.Add(new TextBlock { Text = "My callsign", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            meRow.Children.Add(callsignBox);
            meRow.Children.Add(new TextBlock { Text = "My VID", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            meRow.Children.Add(vidBox);
            root.Children.Add(meRow);
            root.Children.Add(new TextBlock
            {
                Text = "For the log of the session (also in Settings › Network of the Instructor+). Connected to IVAO, the callsign comes from Aurora and the VID from IVAO's list; these are used when not connected.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 4, 0, 0)
            });
            void SaveMe()
            {
                string callsign = callsignBox.Text.Trim().ToUpperInvariant(), vid = new(vidBox.Text.Where(char.IsAsciiDigit).ToArray());
                if (callsign == trainer.MyCallsign && vid == trainer.MyVid) return;
                trainer.MyCallsign = callsign;
                trainer.MyVid = vid;
                save();
            }
            callsignBox.LostFocus += (s, e) => SaveMe();
            vidBox.LostFocus += (s, e) => SaveMe();
            Closed += (s, e) => SaveMe();
            Content = root;

            startButton.Click += (s, e) =>
            {
                if (link()?.Role == LessonRole.Instructor) disconnect();
                else if (link() == null) Start(LessonRole.Instructor, CoordinationSettings.GenerateCode());
                Refresh();
            };
            copyButton.Click += (s, e) =>
            {
                try
                {
                    if (link() is { Role: LessonRole.Instructor } running) Clipboard.SetText(running.Code);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // (The clipboard busy: the code is on the screen anyway.)
                }
            };
            joinButton.Click += (s, e) =>
            {
                if (link()?.Role == LessonRole.Student)
                {
                    disconnect();
                    Refresh();
                    return;
                }
                string? typed = CoordinationSettings.NormalizeCode(codeBox.Text);
                if (typed == null || typed.Length < 4)
                {
                    MessageBox.Show(this, "Write the code of the lesson (6 letters and digits, from the instructor).", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                codeBox.Text = typed;
                Start(LessonRole.Student, typed);
                Refresh();
            };
            Refresh();
        }

        private void Start(LessonRole role, string code)
        {
            string? error = connect(role, code);
            if (error != null) MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Shows the state (called by the main window at every refresh).</summary>
        public void Refresh()
        {
            LessonLink? current = link();
            bool instructor = current?.Role == LessonRole.Instructor, student = current?.Role == LessonRole.Student;
            string start = instructor ? "STOP LESSON" : "START LESSON";
            if (startButton.Content as string != start) startButton.Content = start;
            startButton.IsEnabled = !student;
            copyButton.IsEnabled = instructor;
            string code = instructor ? $"Code: {current!.Code}" : "";
            if (codeText.Text != code) codeText.Text = code;
            string join = student ? "LEAVE" : "JOIN";
            if (joinButton.Content as string != join) joinButton.Content = join;
            joinButton.IsEnabled = !instructor;
            codeBox.IsEnabled = current == null;
            string other = instructor ? "student" : "instructor";
            (string text, Color colour) = current == null
                ? ("", Colors.DimGray)
                : !current.Connected
                    ? ("Connecting to the relay...", Color.FromRgb(0xC0, 0x70, 0x00))
                    : current.OtherPresent
                        ? ($"● Linked with the {other}.", Color.FromRgb(0x10, 0x80, 0x20))
                        : current.EverHeard
                            ? ($"LINK LOST: the {other} is not heard (trying again by itself).", Color.FromRgb(0xC0, 0x20, 0x20))
                            : ($"Waiting for the {other}...", Color.FromRgb(0xC0, 0x70, 0x00));
            if (stateText.Text != text)
            {
                stateText.Text = text;
                stateText.Foreground = new SolidColorBrush(colour);
            }
        }
    }
}
