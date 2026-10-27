using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraPAR v2 Trainer: the lesson between two PCs (<see cref="LessonLink"/>). The instructor sends its simulated
    /// aircraft (with its QNH, the wind and the aircraft removed); the student adds them to its traffic, with its own IVAO
    /// traffic from its Aurora, and sends back its runway, its PAR beam, the aircraft its PAR sees and its IVAO traffic:
    /// the Instructor+ follows the student's runway and shows what the student sees.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The lesson running (read also by the radar timer thread), or null.</summary>
        private volatile LessonLink? lesson;
        private LessonWindow? lessonWindow;
        private Button? lessonButton;
        private readonly object lessonLock = new();
        private static readonly JsonSerializerOptions LessonJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        private DateTime lessonLastSend = DateTime.MinValue;

        // Student: what the instructor sent (under lessonLock).
        private List<Aircraft> lessonAircraft = [];
        private DateTime lessonAircraftAt = DateTime.MinValue;
        private int lessonQnh;
        private (double From, double Knots)? lessonWind;
        private readonly List<LessonForget> lessonForgetIn = [];
        /// <summary>The last "forget" carried out (the instructor sends each one a few times), and the run of the instructor's link it belongs to.</summary>
        private int lessonForgetDone;
        private string? lessonForgetSession;

        // Instructor: what the student sent (under lessonLock).
        private LessonRunway? studentRunwayData;
        private Runway? studentRunway;
        private List<(double Latitude, double Longitude)> studentBeam = [];
        private List<(double Latitude, double Longitude)> studentScan = [];
        private HashSet<string> studentSeen = new(StringComparer.OrdinalIgnoreCase);
        private List<Aircraft> studentTraffic = [];
        private DateTime studentAt = DateTime.MinValue;
        /// <summary>Aircraft removed or started again, still to be sent (each a few times: a message can be lost).</summary>
        private readonly List<(LessonForget Forget, int SendsLeft)> lessonForgetOut = [];
        private int lessonForgetNumber;

        private bool LessonStudent => lesson?.Role == LessonRole.Student;
        private bool LessonInstructor => lesson?.Role == LessonRole.Instructor;
        private static bool LessonFresh(DateTime at) => (DateTime.UtcNow - at).TotalSeconds < LessonLink.PresentSeconds;

        /// <summary>The key "Lesson" beside the Coordination key.</summary>
        private void BuildLessonButton()
        {
            lessonButton = new Button { Content = "Lesson...", Focusable = false };
            lessonButton.Click += (s, e) => OpenLessonWindow();
            if (CoordinationButton.Parent is Panel keys) keys.Children.Insert(keys.Children.IndexOf(CoordinationButton) + 1, lessonButton);
        }

        private void StyleLessonButton(bool analog)
        {
            if (lessonButton == null) return;
            lessonButton.ToolTip = "Lesson between two PCs: the Instructor+ on one PC, the student's PAR and RDR on another, linked through the internet.";
            if (analog)
            {
                StyleAsKey(lessonButton, "LESSON");
                lessonButton.Content = "LESSON";
            }
            else
            {
                lessonButton.ClearValue(StyleProperty);
                lessonButton.ClearValue(FontSizeProperty);
                lessonButton.Content = "Lesson...";
                lessonButton.Width = 98;
                lessonButton.Height = 26;
                lessonButton.Margin = new Thickness(0, 4, 0, 0);
            }
            UpdateLessonButton();
        }

        /// <summary>Lit while a lesson runs (modern: blue while linked, orange while waiting or lost).</summary>
        private void UpdateLessonButton()
        {
            if (lessonButton == null) return;
            if (viewOptions.Analog)
            {
                string tag = lesson != null ? "Lit" : "Unlit";
                if (lessonButton.Tag as string != tag) lessonButton.Tag = tag;
                return;
            }
            lessonButton.Tag = null;
            if (lesson == null)
            {
                lessonButton.ClearValue(BackgroundProperty);
                lessonButton.ClearValue(FontWeightProperty);
            }
            else
            {
                Color colour = lesson.OtherPresent ? Color.FromRgb(0xB8, 0xD6, 0xEE) : Color.FromRgb(0xFF, 0xD8, 0x9C);
                if (lessonButton.Background is not SolidColorBrush brush || brush.Color != colour) lessonButton.Background = new SolidColorBrush(colour);
                lessonButton.FontWeight = FontWeights.Bold;
            }
        }

        private void OpenLessonWindow()
        {
            if (lessonWindow != null)
            {
                lessonWindow.Activate();
                return;
            }
            LessonRole role = settings.Trainer.LessonRole == nameof(LessonRole.Instructor) ? LessonRole.Instructor : LessonRole.Student;
            LessonWindow window = new(role, settings.Trainer.LessonCode, StartLesson, StopLesson, () => lesson) { Owner = this };
            window.Closed += (s, e) => { if (lessonWindow == window) lessonWindow = null; };
            lessonWindow = window;
            window.Show();
        }

        /// <summary>Starts the lesson with a role and a code: an error to show, or null.</summary>
        private string? StartLesson(LessonRole role, string code)
        {
            if (role == LessonRole.Student && (instructorWindow != null || testTrafficWindow != null))
            {
                return "Close the Instructor windows of this PC first: in a lesson the aircraft are flown on the instructor's PC.";
            }
            StopLesson();
            settings.Trainer.LessonRole = role.ToString();
            settings.Trainer.LessonCode = code;
            SettingsStore.Save(settings);
            LessonLink link = new(role, code);
            link.Received += payload => LessonReceived(link, payload);
            lesson = link;
            lessonLastSend = DateTime.MinValue;
            UpdateLessonButton();
            FeedInstructorView();
            return null;
        }

        private void StopLesson()
        {
            LessonLink? link = lesson;
            if (link == null) return;
            lesson = null;
            link.Dispose();
            List<string> gone;
            lock (lessonLock)
            {
                gone = lessonAircraft.Select(a => a.Callsign).ToList();
                lessonAircraft = [];
                lessonAircraftAt = DateTime.MinValue;
                lessonQnh = 0;
                lessonWind = null;
                lessonForgetIn.Clear();
                lessonForgetDone = 0;
                lessonForgetSession = null;
                studentRunwayData = null;
                studentRunway = null;
                studentBeam = [];
                studentScan = [];
                studentSeen = new(StringComparer.OrdinalIgnoreCase);
                studentTraffic = [];
                studentAt = DateTime.MinValue;
                lessonForgetOut.Clear();
            }
            // The student: the aircraft of the lesson go, with their tracks.
            foreach (string callsign in gone) ForgetTracks(callsign);
            UpdateLessonButton();
            FeedInstructorView();
            Redraw();
        }

        /// <summary>A message of the other side (background thread).</summary>
        private void LessonReceived(LessonLink link, string payload)
        {
            if (lesson != link || payload.Length > 300_000) return;
            try
            {
                if (link.Role == LessonRole.Student)
                {
                    InstructorLessonMessage? message = JsonSerializer.Deserialize<InstructorLessonMessage>(payload, LessonJson);
                    if (message == null || message.Version != 1) return;
                    List<Aircraft> aircraft = (message.Aircraft ?? []).Where(a => a != null && a.IsValid).Take(200).Select(a => a.ToAircraft(simulated: true))
                        .DistinctBy(a => a.Callsign, StringComparer.OrdinalIgnoreCase).ToList();
                    lock (lessonLock)
                    {
                        lessonAircraft = aircraft;
                        lessonAircraftAt = DateTime.UtcNow;
                        lessonQnh = message.Qnh is >= 850 and <= 1090 ? message.Qnh : 0;
                        // The instructor started its link again: its numbers start again.
                        if (message.Session != lessonForgetSession)
                        {
                            lessonForgetSession = message.Session;
                            lessonForgetDone = 0;
                            lessonForgetIn.Clear();
                        }
                        lessonWind = message.WindFrom is double windFrom && message.WindKnots is double windKnots
                            && double.IsFinite(windFrom) && double.IsFinite(windKnots) && windKnots >= 0 && windKnots < 200
                            ? (windFrom, windKnots) : null;
                        foreach (LessonForget forget in (message.Forget ?? []).Take(50))
                        {
                            if (forget != null && forget.Number > lessonForgetDone && !string.IsNullOrWhiteSpace(forget.Callsign) && forget.Callsign.Length <= 16
                                && !lessonForgetIn.Any(f => f.Number == forget.Number))
                            {
                                lessonForgetIn.Add(forget);
                            }
                        }
                    }
                }
                else
                {
                    StudentLessonMessage? message = JsonSerializer.Deserialize<StudentLessonMessage>(payload, LessonJson);
                    if (message == null || message.Version != 1) return;
                    List<Aircraft> traffic = (message.Traffic ?? []).Where(a => a != null && a.IsValid).Take(300).Select(a => a.ToAircraft(simulated: false))
                        .DistinctBy(a => a.Callsign, StringComparer.OrdinalIgnoreCase).ToList();
                    HashSet<string> seen = new((message.Seen ?? []).Where(c => !string.IsNullOrWhiteSpace(c) && c.Length <= 16).Take(200), StringComparer.OrdinalIgnoreCase);
                    List<(double, double)> beam = StudentLessonMessage.Points(message.Beam);
                    List<(double, double)> scan = StudentLessonMessage.Points(message.Scan);
                    lock (lessonLock)
                    {
                        if (message.Runway is LessonRunway data && data.IsValid && data != studentRunwayData)
                        {
                            studentRunwayData = data;
                            studentRunway = data.ToRunway();
                        }
                        studentBeam = beam;
                        studentScan = scan;
                        studentSeen = seen;
                        studentTraffic = traffic;
                        studentAt = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception)
            {
                // A malformed message is ignored.
            }
        }

        /// <summary>Radar timer (background thread): the aircraft of the lesson added to the traffic.</summary>
        partial void TrainerTraffic(List<Aircraft> aircrafts)
        {
            LessonLink? link = lesson;
            if (link == null) return;
            List<Aircraft> added;
            lock (lessonLock)
            {
                if (link.Role == LessonRole.Student) added = LessonFresh(lessonAircraftAt) ? lessonAircraft : [];
                // The instructor without Aurora sees the student's IVAO traffic (with Aurora it has its own).
                else added = !aurora.Connected && LessonFresh(studentAt) ? studentTraffic : [];
            }
            if (added.Count == 0) return;
            HashSet<string> present = new(aircrafts.Select(a => a.Callsign), StringComparer.OrdinalIgnoreCase);
            // Copies: the display writes into the aircraft of each refresh.
            foreach (Aircraft aircraft in added) if (present.Add(aircraft.Callsign)) aircrafts.Add(aircraft.Copy());
        }

        /// <summary>Simulated aircraft received from the instructor and flying now (student).</summary>
        private int LessonSimCount
        {
            get
            {
                if (!LessonStudent) return 0;
                lock (lessonLock) return LessonFresh(lessonAircraftAt) ? lessonAircraft.Count : 0;
            }
        }

        /// <summary>The wind of the instructor's simulation (student, while its aircraft fly), or null.</summary>
        private (double From, double Knots)? LessonWind
        {
            get
            {
                if (!LessonStudent) return null;
                lock (lessonLock) return LessonFresh(lessonAircraftAt) && lessonAircraft.Count > 0 ? lessonWind : null;
            }
        }

        /// <summary>Instructor: the runway of the student while linked (the Instructor+ follows it), else the one of this PC.</summary>
        private Runway InstructorRunway
        {
            get
            {
                if (LessonInstructor)
                {
                    lock (lessonLock)
                    {
                        if (studentRunway != null) return studentRunway;
                    }
                }
                return runway;
            }
        }

        /// <summary>Instructor: whether the student's PAR sees an aircraft (null: no student linked, this PC decides).</summary>
        private bool? LessonIsSeen(Aircraft aircraft)
        {
            if (!LessonInstructor) return null;
            lock (lessonLock) return LessonFresh(studentAt) ? studentSeen.Contains(aircraft.Callsign) : null;
        }

        /// <summary>Instructor: the callsigns of the student's IVAO traffic (a new aircraft must not take one of them).</summary>
        private IEnumerable<string> LessonStudentCallsigns()
        {
            if (!LessonInstructor) return [];
            lock (lessonLock) return LessonFresh(studentAt) ? studentTraffic.Select(a => a.Callsign).ToList() : [];
        }

        /// <summary>
        /// Instructor: an aircraft removed or started again, to be forgotten on the student's PC too ("all" comes as one
        /// call for each aircraft).
        /// </summary>
        private void LessonForgetAircraft(string? callsign)
        {
            if (!LessonInstructor || string.IsNullOrWhiteSpace(callsign)) return;
            lock (lessonLock)
            {
                lessonForgetNumber++;
                lessonForgetOut.Add((new LessonForget { Number = lessonForgetNumber, Callsign = callsign }, 4));
            }
        }

        /// <summary>The sign of the student's main window while the lesson is not linked (null: nothing to say).</summary>
        private string? LessonSignText
        {
            get
            {
                LessonLink? link = lesson;
                if (link == null || link.Role != LessonRole.Student) return null;
                if (!link.Connected) return "LESSON: CONNECTING...";
                if (!link.EverHeard) return "LESSON: WAITING FOR THE INSTRUCTOR";
                return link.OtherPresent ? null : "LESSON: LINK LOST";
            }
        }

        /// <summary>Instructor: the state of the lesson for the plan view of the Instructor+ ("" without a lesson).</summary>
        private string LessonInfoText
        {
            get
            {
                LessonLink? link = lesson;
                if (link == null || link.Role != LessonRole.Instructor) return "";
                return $"   LESSON {link.Code}: {(link.OtherPresent ? "STUDENT LINKED" : link.EverHeard ? "STUDENT LINK LOST" : "WAITING FOR THE STUDENT")}";
            }
        }

        /// <summary>Student: the Instructor windows are not opened on its PC (true: blocked, the user was told).</summary>
        private bool LessonBlocksInstructor()
        {
            if (!LessonStudent) return false;
            MessageBox.Show(this, "This PC is the student of a lesson: the aircraft are flown on the instructor's PC. Disconnect the lesson (key Lesson) to use the Instructor here.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        /// <summary>At every refresh (user interface thread): what arrived is used, and this PC sends its message.</summary>
        private void LessonTick(List<Aircraft> aircrafts)
        {
            UpdateLessonButton();
            lessonWindow?.Refresh();
            LessonLink? link = lesson;
            if (link == null) return;
            DateTime now = DateTime.UtcNow;
            if (link.Role == LessonRole.Student)
            {
                List<LessonForget> forget = [];
                int receivedQnh;
                lock (lessonLock)
                {
                    foreach (LessonForget item in lessonForgetIn.OrderBy(f => f.Number))
                    {
                        if (item.Number <= lessonForgetDone) continue;
                        lessonForgetDone = item.Number;
                        forget.Add(item);
                    }
                    lessonForgetIn.Clear();
                    receivedQnh = LessonFresh(lessonAircraftAt) ? lessonQnh : 0;
                }
                foreach (LessonForget item in forget) ForgetTracks(item.Callsign);
                // Without Aurora the QNH is the instructor's (for this lesson only: the QNH typed by hand stays saved).
                if (receivedQnh > 0 && !aurora.Connected && receivedQnh != qnh)
                {
                    qnh = receivedQnh;
                    ShowManualQnh();
                }
                if ((now - lessonLastSend).TotalSeconds >= 1)
                {
                    lessonLastSend = now;
                    StudentLessonMessage message = new()
                    {
                        Runway = LessonRunway.From(runway),
                        Beam = StudentLessonMessage.Flatten(PlanSector(radar.AzimuthLeftEdge, radar.AzimuthRightEdge)),
                        Scan = StudentLessonMessage.Flatten(PlanSector(radar.ScanLeftEdge, radar.ScanRightEdge)),
                        Seen = aircrafts.Where(a => a.IsSim && radar.IsSeen(a, runway)).Select(a => a.Callsign).ToList(),
                        Traffic = aurora.Connected ? aircrafts.Where(a => !a.IsTest).Take(300).Select(LessonAircraft.From).ToList() : null
                    };
                    link.Send(JsonSerializer.Serialize(message, LessonJson));
                }
            }
            else if ((now - lessonLastSend).TotalSeconds >= 0.5)
            {
                lessonLastSend = now;
                (double windFrom, double windKnots, _) = instructor.Engine.Wind;
                bool flying = instructor.Count > 0;
                List<LessonForget> forget;
                lock (lessonLock)
                {
                    forget = lessonForgetOut.Select(f => f.Forget).ToList();
                    for (int i = lessonForgetOut.Count - 1; i >= 0; i--)
                    {
                        (LessonForget item, int left) = lessonForgetOut[i];
                        if (left <= 1) lessonForgetOut.RemoveAt(i);
                        else lessonForgetOut[i] = (item, left - 1);
                    }
                }
                InstructorLessonMessage message = new()
                {
                    Session = link.Session,
                    Aircraft = aircrafts.Where(a => a.IsSim).Select(LessonAircraft.From).ToList(),
                    Qnh = qnh,
                    WindFrom = flying && windKnots > 0 ? Math.Round(windFrom) : null,
                    WindKnots = flying && windKnots > 0 ? Math.Round(windKnots, 1) : null,
                    Forget = forget.Count > 0 ? forget : null
                };
                link.Send(JsonSerializer.Serialize(message, LessonJson));
            }
        }
    }
}
