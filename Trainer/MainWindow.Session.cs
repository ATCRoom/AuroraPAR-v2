using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: the session and its log (the user, 9 Nov: as TWR Trainer). START SESSION / END SESSION and PHOTO
    /// on a bar at the top left of the Instructor+ plan view; the log (<see cref="ApproachLog"/>) fed once a second; the
    /// callsign and VID of the instructor (Aurora and IVAO's list when connected, else typed in the Lesson window) and of the
    /// student (sent on the lesson link).
    /// </summary>
    public partial class MainWindow
    {
        private readonly ApproachLog approachLog = new();
        private DateTime sessionStepAt = DateTime.MinValue;
        private Button? sessionButton;
        private readonly TextBlock photoText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xDE)), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private DispatcherTimer? photoTextTimer;
        private StackPanel? sessionBar;
        private string photoSession = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        private int photoCount;
        // The callsign on IVAO (Aurora), asked every 30 s while connected, and its VID (IVAO's list).
        private string ivaoCallsign = "", ivaoVid = "", ivaoVidFor = "";
        private DateTime ivaoAskedAt = DateTime.MinValue;
        private bool ivaoAsking;
        /// <summary>IVAO's list did not give the VID: asked again after this (its list is refreshed every 15 s).</summary>
        private DateTime ivaoVidRetryAt = DateTime.MinValue;
        private string loggedStudent = "";

        private static string PhotosFolder => Path.Combine(Path.GetDirectoryName(SettingsStore.FilePath) ?? AppContext.BaseDirectory, "Photos");
        private string PhotoFolder => Path.Combine(PhotosFolder, photoSession);

        /// <summary>My callsign: Aurora's on IVAO, else the one typed (Lesson window).</summary>
        private string MyCallsign => ivaoCallsign.Length > 0 ? ivaoCallsign : settings.Trainer.MyCallsign;
        /// <summary>My VID: IVAO's list's while connected, else the one typed.</summary>
        private string MyVid => ivaoCallsign.Length > 0 && ivaoVid.Length > 0 ? ivaoVid : settings.Trainer.MyVid;

        private static readonly Brush BarOff = new SolidColorBrush(Color.FromArgb(0xE0, 0x2A, 0x3A, 0x4A));
        private static readonly Brush BarBorder = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xD8));
        private static readonly Brush SessionOn = new SolidColorBrush(Color.FromRgb(0x3B, 0x8A, 0x22));

        private static Button BarButton(string text, string tip, Action click)
        {
            Button button = new()
            {
                Content = text,
                ToolTip = tip,
                Focusable = false,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = BarOff,
                BorderBrush = BarBorder,
                BorderThickness = new Thickness(1.5)
            };
            button.Click += (s, e) => click();
            return button;
        }

        /// <summary>The bar of the session on the Instructor+ plan view (top left, under the line of the radar).</summary>
        private UIElement SessionBar()
        {
            if (sessionBar != null)
            {
                (sessionBar.Parent as Panel)?.Children.Remove(sessionBar);
                return sessionBar;
            }
            sessionBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 26, 0, 0) };
            // PAUSE / RUN first and IVAO last, as on TWR Trainer (the user, 10 Nov).
            runButton = BarButton("PAUSE", "", () => instructorWindow?.TogglePause());
            runButton.MinWidth = 64;
            sessionBar.Children.Add(runButton);
            sessionButton = BarButton("START SESSION", "", ToggleSession);
            sessionBar.Children.Add(sessionButton);
            // (The same order as TWR Trainer: PAUSE / RUN, START SESSION, LESSON, PHOTO, 📁, LOGS, IVAO.)
            barLessonButton = BarButton("LESSON", "", OpenLessonWindow);
            sessionBar.Children.Add(barLessonButton);
            sessionBar.Children.Add(BarButton("PHOTO", "A picture of the Instructor+ (the simulation goes on), in the Photos folder of this session; a mark PHOTO n in the log.", TakePhoto));
            sessionBar.Children.Add(BarButton("📁", "Opens the folder of the photos of this session (or the Photos folder).", OpenPhotos));
            sessionBar.Children.Add(BarButton("LOGS", "Opens the Logs folder: a CSV per session (Excel), and a folder per session with the pictures of the traces of the approaches.", OpenLogs));
            ivaoButton = BarButton("IVAO", "", ToggleInstructorIvao);
            sessionBar.Children.Add(ivaoButton);
            sessionBar.Children.Add(photoText);
            ShowSession();
            return sessionBar;
        }

        /// <summary>
        /// START SESSION / END SESSION (as TWR Trainer): the training or the exam in the log (a new file, the time from 0, a new
        /// folder of photos); nothing starts flying. Ended: the summary written, the simulation paused (asked first).
        /// </summary>
        private void ToggleSession()
        {
            if (!approachLog.InSession)
            {
                // (The user, 10 Nov: a lesson between two PCs started but the student not linked yet: asked first.)
                if (LessonInstructor && lesson is { OtherPresent: false }
                    && MessageBox.Show(instructorWindow ?? (Window)this, "The student is not linked yet. Start the session anyway?", AppInfo.ProductName,
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                // (The speed of the simulation back to x1, and x1 until END SESSION.)
                instructorWindow?.LockSpeed(true);
                photoSession = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                photoCount = 0;
                Runway reference = InstructorRunway;
                approachLog.Airport = reference.ICAO;
                approachLog.RunwayName = reference.Designator;
                approachLog.Network = aurora.Connected && ivaoCallsign.Length > 0;
                approachLog.InstructorCallsign = MyCallsign;
                approachLog.InstructorVid = MyVid;
                approachLog.StudentCallsign = studentCallsign;
                approachLog.StudentVid = studentVid;
                loggedStudent = $"{studentCallsign}|{studentVid}";
                approachLog.StartSession(instructor.Engine.Time, instructor.Engine.Snapshot());
                PhotoSaid(approachLog.Error ?? $"Session started: {Path.GetFileName(approachLog.FileName)}");
            }
            else
            {
                if (MessageBox.Show(instructorWindow ?? (Window)this, "End the session? Its log is closed with the summary, and the simulation paused.", AppInfo.ProductName,
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                string file = Path.GetFileName(approachLog.FileName);
                approachLog.EndSession(instructor.Engine.Time);
                photoSession = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                photoCount = 0;
                instructor.Engine.Paused = true;
                instructorWindow?.LockSpeed(false);
                PhotoSaid($"Session ended: {file}");
            }
            ShowSession();
        }

        private Button? runButton, ivaoButton, barLessonButton;

        /// <summary>The Network section of the Settings panel of the Instructor+ (as TWR Trainer): My callsign, My VID.</summary>
        private UIElement? networkSection;
        private UIElement NetworkSection()
        {
            if (networkSection != null)
            {
                ((networkSection as FrameworkElement)?.Parent as Panel)?.Children.Remove(networkSection);
                return networkSection;
            }
            StackPanel section = new();
            section.Children.Add(new TextBlock { Text = "Network", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
            TextBox callsignBox = new() { Width = 100, MaxLength = 10, CharacterCasing = CharacterCasing.Upper, Text = settings.Trainer.MyCallsign, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
            TextBox vidBox = new() { Width = 90, MaxLength = 10, Text = settings.Trainer.MyVid, VerticalContentAlignment = VerticalAlignment.Center };
            StackPanel row = new() { Orientation = Orientation.Horizontal, ToolTip = "For the log of the session: used when Aurora is not connected to IVAO (connected: the callsign from Aurora, the VID from IVAO's list of who is connected)." };
            row.Children.Add(new TextBlock { Text = "My callsign", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(callsignBox);
            row.Children.Add(new TextBlock { Text = "My VID", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(vidBox);
            section.Children.Add(row);
            void SaveMe()
            {
                string callsign = callsignBox.Text.Trim().ToUpperInvariant(), vid = new(vidBox.Text.Where(char.IsAsciiDigit).ToArray());
                vidBox.Text = vid;
                if (callsign == settings.Trainer.MyCallsign && vid == settings.Trainer.MyVid) return;
                settings.Trainer.MyCallsign = callsign;
                settings.Trainer.MyVid = vid;
                SettingsStore.Save(settings);
            }
            callsignBox.LostFocus += (s, e) => SaveMe();
            vidBox.LostFocus += (s, e) => SaveMe();
            // (Changed in the Lesson window: shown here when the panel gets the keyboard again.)
            section.GotKeyboardFocus += (s, e) =>
            {
                if (!callsignBox.IsKeyboardFocused && callsignBox.Text != settings.Trainer.MyCallsign) callsignBox.Text = settings.Trainer.MyCallsign;
                if (!vidBox.IsKeyboardFocused && vidBox.Text != settings.Trainer.MyVid) vidBox.Text = settings.Trainer.MyVid;
            };
            networkSection = section;
            return section;
        }
        private static readonly Brush RunBack = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A));
        private static readonly Brush DarkText = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2A));
        private static readonly Brush IvaoOn = new SolidColorBrush(Color.FromRgb(0x63, 0x99, 0x22));
        private static readonly Brush KeyLinked = new SolidColorBrush(Color.FromRgb(0x85, 0xB7, 0xEB));
        private static readonly Brush IvaoSearching = new SolidColorBrush(Color.FromRgb(0xEF, 0x9F, 0x27));

        /// <summary>IVAO on the bar: the IVAO traffic on the Instructor+ shown or not (Aurora stays connected for the PAR and RDR).</summary>
        private void ToggleInstructorIvao()
        {
            settings.Trainer.InstructorIvao = !settings.Trainer.InstructorIvao;
            SettingsStore.Save(settings);
            ShowBar();
        }

        /// <summary>PAUSE / RUN (yellow RUN while paused) and IVAO (grey off, orange: Aurora not on IVAO, green: on IVAO), as TWR Trainer.</summary>
        private void ShowBar()
        {
            if (runButton != null)
            {
                bool paused = instructor.Engine.Paused;
                string text = paused ? "RUN" : "PAUSE";
                if (!Equals(runButton.Content, text))
                {
                    runButton.Content = text;
                    runButton.Background = paused ? RunBack : BarOff;
                    runButton.Foreground = paused ? DarkText : Brushes.White;
                    runButton.ToolTip = paused ? "The simulation is paused (all the aircraft). Click: RUN (as RESUME ALL)." : "The simulation runs. Click: PAUSE all the aircraft (as PAUSE ALL).";
                }
            }
            if (barLessonButton != null)
            {
                // LESSON lit with its code while a lesson is on (blue: linked; orange: waiting or lost), as TWR Trainer.
                string text = lesson != null ? $"LESSON {lesson.Code}" : "LESSON";
                Brush back = lesson == null ? BarOff : lesson.OtherPresent ? KeyLinked : IvaoSearching;
                if (!Equals(barLessonButton.Content, text) || !ReferenceEquals(barLessonButton.Background, back))
                {
                    barLessonButton.Content = text;
                    barLessonButton.Background = back;
                    barLessonButton.Foreground = lesson == null ? Brushes.White : DarkText;
                    barLessonButton.ToolTip = lesson == null ? "A lesson between two PCs: start one (a code for the student), or join one as the student."
                        : $"A lesson is on: code {lesson.Code}{(lesson.OtherPresent ? ", linked" : ", waiting for the other PC")}. Click: its window.";
                }
            }
            if (ivaoButton != null)
            {
                bool on = settings.Trainer.InstructorIvao, linked = aurora.Connected, network = linked && ivaoCallsign.Length > 0;
                string text = !on ? "IVAO" : network ? "● IVAO" : "IVAO ?";
                Brush back = !on ? BarOff : network ? IvaoOn : IvaoSearching;
                if (!Equals(ivaoButton.Content, text) || !ReferenceEquals(ivaoButton.Background, back))
                {
                    ivaoButton.Content = text;
                    ivaoButton.Background = back;
                    ivaoButton.Foreground = on ? DarkText : Brushes.White;
                }
                ivaoButton.ToolTip = !on ? "The IVAO traffic on the Instructor+: hidden (only the simulated aircraft). Aurora stays connected: the PAR and RDR of this PC keep it. Click: shown."
                    : network ? $"Aurora on IVAO as {ivaoCallsign}: its traffic on the Instructor+. Click: hide it (Aurora stays connected)."
                    : linked ? "Aurora found, but not connected to IVAO: no IVAO traffic yet. Click: hide it."
                    : "Aurora not found on this PC (STS SIM: the simulated aircraft only). Click: hide the IVAO traffic.";
            }
        }

        private void ShowSession()
        {
            if (sessionButton == null) return;
            bool on = approachLog.InSession;
            int seconds = on ? (int)Math.Max(0, instructor.Engine.Time - approachLog.SessionStart) : -1;
            sessionButton.Content = on ? string.Create(CultureInfo.InvariantCulture, $"END SESSION {seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}") : "START SESSION";
            sessionButton.Background = on ? SessionOn : BarOff;
            sessionButton.ToolTip = on ? "The session of the training or the exam: written in the log (Logs), a picture of the trace of each approach. Click: end it (asked first)."
                : "Start the session of the training or the exam: a new log (CSV, Logs folder), the time from 0:00:00, the traces of the approaches, a new folder of photos. Nothing starts flying.";
        }

        /// <summary>
        /// At every refresh: once a second the log fed (the simulated aircraft, the runway of the lesson, the limits of the
        /// profile), the button's time; the callsign on IVAO asked every 30 s; the student's callsign and VID in the log.
        /// </summary>
        private void SessionTick()
        {
            DateTime now = DateTime.UtcNow;
            ShowBar();
            if (aurora.Connected && !ivaoAsking && now - ivaoAskedAt > TimeSpan.FromSeconds(30)) _ = AskIvao();
            else if (!aurora.Connected && ivaoCallsign.Length > 0) ivaoCallsign = ivaoVid = ivaoVidFor = "";
            if (!approachLog.InSession || now - sessionStepAt < TimeSpan.FromSeconds(1)) return;
            sessionStepAt = now;
            double time = instructor.Engine.Time;
            string student = $"{studentCallsign}|{studentVid}";
            if (student != loggedStudent)
            {
                loggedStudent = student;
                approachLog.StudentCallsign = studentCallsign;
                approachLog.StudentVid = studentVid;
                if (studentCallsign.Length > 0 || studentVid.Length > 0)
                    approachLog.Event(time, "STUDENT", detail: $"{studentCallsign}{(studentVid.Length > 0 ? $" VID {studentVid}" : "")}", source: "STUDENT");
            }
            approachLog.Network = aurora.Connected && ivaoCallsign.Length > 0;
            // (The runway of the rows follows the one in use: the student may change it; the review of 9 Nov.)
            approachLog.RunwayName = InstructorRunway.Designator;
            double variation = instructor.Reference?.MagneticVariation ?? settings.Active.MagneticVariation;
            approachLog.Step(time, instructor.Engine.Paused, instructor.Engine.Snapshot(), InstructorRunway, radar, viewOptions.ApproachLimitsTo,
                viewOptions.AzimuthApproachLimits, variation, instructor.ToGeo);
            ShowSession();
        }

        /// <summary>The callsign on IVAO from Aurora, and its VID from IVAO's list when it changed.</summary>
        private async Task AskIvao()
        {
            ivaoAsking = true;
            ivaoAskedAt = DateTime.UtcNow;
            try
            {
                string callsign = (await aurora.GetConnectedCallsign() ?? "").Trim().ToUpperInvariant();
                ivaoCallsign = callsign;
                if (callsign.Length == 0)
                {
                    ivaoVid = ivaoVidFor = "";
                    return;
                }
                if (callsign != ivaoVidFor && DateTime.UtcNow >= ivaoVidRetryAt)
                {
                    ivaoVidFor = callsign;
                    ivaoVid = await IvaoDirectory.FindVidAsync(callsign, AppInfo.Version) ?? "";
                    // (Not in IVAO's list yet, just connected: asked again in 2 minutes; the review of 9 Nov.)
                    if (ivaoVid.Length == 0)
                    {
                        ivaoVidFor = "";
                        ivaoVidRetryAt = DateTime.UtcNow.AddMinutes(2);
                    }
                }
            }
            catch (Exception)
            {
                // (Asked again in 30 s.)
            }
            finally
            {
                ivaoAsking = false;
            }
        }

        /// <summary>
        /// A picture of the Instructor+ (as TWR Trainer), at the moment of the click; the simulation does not stop. In
        /// Photos\&lt;session&gt;\ as Photo-03_T+12m40s.png; a mark PHOTO n in the log.
        /// </summary>
        private void TakePhoto()
        {
            if (instructorWindow?.Content is not FrameworkElement view || view.ActualWidth < 20 || view.ActualHeight < 20) return;
            RenderTargetBitmap bitmap;
            try
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(view);
                Rect area = new(0, 0, view.ActualWidth, view.ActualHeight);
                DrawingVisual picture = new();
                using (DrawingContext dc = picture.RenderOpen()) dc.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, area);
                bitmap = new RenderTargetBitmap((int)Math.Ceiling(area.Width * dpi.DpiScaleX), (int)Math.Ceiling(area.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(picture);
                bitmap.Freeze();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OutOfMemoryException)
            {
                PhotoSaid("Photo not taken (the view could not be drawn).");
                return;
            }
            string folder = PhotoFolder;
            int number = ++photoCount;
            double time = Math.Max(0, instructor.Engine.Time);
            double shown = approachLog.InSession ? Math.Max(0, time - approachLog.SessionStart) : time;
            string name = string.Create(CultureInfo.InvariantCulture, $"Photo-{number:00}_T+{(int)(shown / 60)}m{(int)(shown % 60):00}s.png");
            approachLog.Event(time, $"PHOTO {number}", detail: name, source: "INSTRUCTOR");
            _ = Task.Run(() =>
            {
                string said;
                try
                {
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, name);
                    if (File.Exists(path)) path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + "_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + ".png");
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using FileStream file = File.Create(path);
                    encoder.Save(file);
                    said = $"Photo {number}: {Path.GetFileName(path)}";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
                {
                    // (No path in the message: the Windows user name.)
                    said = $"Photo {number} not saved: the Photos folder cannot be written (read-only, or the disk is full).";
                }
                Dispatcher.InvokeAsync(() => PhotoSaid(said));
            });
        }

        /// <summary>What happened (a photo, the session), beside the buttons for a few seconds.</summary>
        private void PhotoSaid(string text)
        {
            photoText.Text = text;
            photoTextTimer?.Stop();
            photoTextTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            photoTextTimer.Tick += (s, e) =>
            {
                photoText.Text = "";
                photoTextTimer?.Stop();
            };
            photoTextTimer.Start();
        }

        private void OpenPhotos() => OpenFolder(photoCount > 0 && Directory.Exists(PhotoFolder) ? PhotoFolder : PhotosFolder);

        private void OpenLogs() => OpenFolder(approachLog.InSession && Directory.Exists(approachLog.SessionFolder) ? approachLog.SessionFolder : ApproachLog.LogsFolder);

        private void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                PhotoSaid("The folder cannot be opened.");
            }
        }
    }
}
