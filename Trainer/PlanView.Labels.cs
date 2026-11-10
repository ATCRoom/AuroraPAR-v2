using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>A piece of a label line: its text, the field it gives (null: none), whether it is a value given by the instructor (blue), or dim.</summary>
    internal sealed record LabelPart(string Text, string? Field = null, bool Given = false, bool Dim = false, LabelLook Look = LabelLook.Text, double MinWidth = 0);

    /// <summary>
    /// How a part of the larger label is drawn (the user, 10 Nov: a label to command the aircraft by itself, as TWR
    /// Trainer's): text; the small grey name of a field (with a width, so the values line up); a small grey note; a key
    /// (dark), a key lit (blue), the side of a continuous turn (yellow), STOP TURN (orange); a thin line between groups.
    /// </summary>
    internal enum LabelLook { Text, Name, Note, Key, KeyOn, KeyTurning, KeyStop, Divider }

    /// <summary>
    /// The labels of the plan view, as those of the PAR of AuroraPAR: they can be dragged with the left button (a leader
    /// line joins them to the track), a double-click puts one back. On the student's RDR a right click hides a label and a
    /// right click on its track shows it again; on the Instructor+ (<see cref="LabelCommands"/>) a right click asks for
    /// the menu of the aircraft, the selected aircraft has a larger label (<see cref="ExtendedLabel"/>) whose fields can
    /// be clicked (<see cref="LabelFieldClicked"/>) or changed with the wheel (<see cref="LabelFieldWheel"/>).
    /// </summary>
    internal sealed partial class PlanView
    {
        private static readonly Brush GivenBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0xD1, 0xFF)));
        private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
        /// <summary>Labels of the simulated aircraft on the instructor's views (<see cref="MarkSimulated"/>): orange, as SIM TRAFFIC.</summary>
        internal static readonly Brush SimLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x30)));
        private static readonly Brush LabelBack = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x14, 0x18, 0x10)));
        private static readonly Pen LabelBorder = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00))), 1));
        private static readonly Brush KeyBack = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x28, 0x33)));
        private static readonly Brush KeyText = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xDE)));
        private static readonly Brush KeyOnBack = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xAF)));
        private static readonly Brush KeyTurningBack = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A)));
        private static readonly Brush KeyTurningText = Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2A)));
        private static readonly Brush KeyStopBack = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x74, 0x2A)));
        private static readonly Pen KeyBorder = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x58, 0x66))), 1));
        private static readonly Pen DividerPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x46, 0x52))), 1));
        private const double KeyPadding = 4, KeyGap = 3;

        private static bool IsKey(LabelLook look) => look is LabelLook.Key or LabelLook.KeyOn or LabelLook.KeyTurning or LabelLook.KeyStop;

        /// <summary>How wide a part takes on its line (a key: its text, its padding and the gap after it).</summary>
        private static double PartWidth(FormattedText text, LabelPart part) =>
            IsKey(part.Look) ? text.Width + 2 * KeyPadding + KeyGap
            : part.Look == LabelLook.Name ? Math.Max(part.MinWidth, text.Width + 4)
            : text.WidthIncludingTrailingWhitespace;

        /// <summary>Instructor+: labels give commands (fields, menu), not hidden by a right click.</summary>
        public bool LabelCommands { get; set; }
        /// <summary>Instructor+ only: the labels of the simulated aircraft in another colour, not to mix them up with real
        /// (IVAO) traffic with a similar callsign. Never on the student's RDR.</summary>
        public bool MarkSimulated { get; set; }
        /// <summary>Instructor+: the lines of the larger label of an aircraft (null: the usual label).</summary>
        public Func<string, IReadOnlyList<IReadOnlyList<LabelPart>>?>? ExtendedLabel { get; set; }
        /// <summary>A field of a label was clicked: callsign, field, where (in the view).</summary>
        public event Action<string, string, Point>? LabelFieldClicked;
        /// <summary>The wheel over a field of a label: callsign, field, +1 / −1.</summary>
        public event Action<string, string, int>? LabelFieldWheel;
        /// <summary>A right click on a label or a track (Instructor+): callsign, where.</summary>
        public event Action<string, Point>? LabelMenuRequested;

        private readonly Dictionary<string, Vector> labelOffsets = new(StringComparer.Ordinal);
        private readonly HashSet<string> hiddenLabels = new(StringComparer.Ordinal);

        /// <summary>A label as drawn the last time: its box, the track, the callsign and the boxes of its fields.</summary>
        private sealed record DrawnLabel(Rect Box, Point Track, string Callsign, List<(Rect Box, string Field)> Fields);
        private readonly List<DrawnLabel> labelBoxes = [];

        /// <summary>A label pressed with the left button: which, where, its offset then, the field under the mouse, moved yet.</summary>
        private sealed class LabelDrag
        {
            public string Callsign = "";
            public Point Start;
            public Vector StartOffset;
            public string? Field;
            public bool Moved;
        }
        private LabelDrag? labelDrag;
        /// <summary>A right button pressed on a label or a track (Instructor+): its menu opens when the button is let go.</summary>
        private (string Callsign, Point At)? pendingMenu;

        public bool IsLabelHidden(string callsign) => hiddenLabels.Contains(callsign);

        public void SetLabelHidden(string callsign, bool hidden)
        {
            if (hidden) hiddenLabels.Add(callsign);
            else hiddenLabels.Remove(callsign);
            InvalidateVisual();
        }

        private void DrawLabel(DrawingContext dc, Aircraft aircraft, Aircraft shown, Point position, double pixelsPerDip)
        {
            string callsign = aircraft.Callsign;
            if (hiddenLabels.Contains(callsign)) return;
            IReadOnlyList<IReadOnlyList<LabelPart>>? extended = ExtendedLabel?.Invoke(callsign);
            IReadOnlyList<IReadOnlyList<LabelPart>> lines = extended ?? new List<IReadOnlyList<LabelPart>>
            {
                new List<LabelPart> { new(callsign) },
                new List<LabelPart> { new($"{Math.Max(0, Math.Round(shown.Altitude / 100)).ToString("000", CultureInfo.InvariantCulture)}  {Math.Max(0, Math.Round(shown.Speed)).ToString("0", CultureInfo.InvariantCulture)}") }
            };
            double size = extended != null ? 13 : 11;
            double padding = extended != null ? 4 : 1;
            // Measure: each part on its line, one after the other.
            List<List<(FormattedText Text, LabelPart Part)>> measured = [];
            double width = 0;
            double height = 0;
            foreach (IReadOnlyList<LabelPart> line in lines)
            {
                List<(FormattedText Text, LabelPart Part)> row = [];
                double lineWidth = 0;
                double lineHeight = 0;
                bool keys = false;
                foreach (LabelPart part in line)
                {
                    Brush normal = MarkSimulated && aircraft.IsSim ? SimLabelBrush : LabelBrush;
                    Brush brush = part.Look switch
                    {
                        LabelLook.Name or LabelLook.Note => DimBrush,
                        LabelLook.Key => KeyText,
                        LabelLook.KeyOn or LabelLook.KeyStop => Brushes.White,
                        LabelLook.KeyTurning => KeyTurningText,
                        _ => part.Given ? (part.Dim ? DimBrush : GivenBrush) : part.Dim ? DimBrush : normal
                    };
                    double partSize = part.Look is LabelLook.Name or LabelLook.Note ? size - 3 : IsKey(part.Look) ? size - 1 : size;
                    FormattedText text = Text(part.Text, partSize, brush, pixelsPerDip);
                    row.Add((text, part));
                    keys |= IsKey(part.Look);
                    lineWidth += PartWidth(text, part);
                    lineHeight = Math.Max(lineHeight, text.Height);
                }
                measured.Add(row);
                width = Math.Max(width, lineWidth);
                height += line.Count == 1 && line[0].Look == LabelLook.Divider ? 5 : Math.Max(lineHeight, size + 2) + (keys ? 4 : 0);
            }
            Size box = new(width + 2 * padding, height + 2 * padding);
            Vector offset = labelOffsets.TryGetValue(callsign, out Vector moved) ? moved : new Vector(14, -14 - box.Height);
            Rect rect = new(position + offset, box);

            // Leader line from the track to the nearest point of the label.
            Point nearest = new(Math.Clamp(position.X, rect.Left, rect.Right), Math.Clamp(position.Y, rect.Top, rect.Bottom));
            Vector towards = nearest - position;
            if (towards.Length > 8)
            {
                Vector unit = towards / towards.Length;
                dc.DrawLine(LeaderPen, position + unit * 6, nearest);
            }
            if (extended != null) dc.DrawRectangle(LabelBack, LabelBorder, rect);

            List<(Rect Box, string Field)> fields = [];
            double y = rect.Top + padding;
            foreach (List<(FormattedText Text, LabelPart Part)> row in measured)
            {
                double x = rect.Left + padding;
                if (row.Count == 1 && row[0].Part.Look == LabelLook.Divider)
                {
                    dc.DrawLine(DividerPen, new Point(rect.Left + padding, y + 2.5), new Point(rect.Right - padding, y + 2.5));
                    y += 5;
                    continue;
                }
                bool keys = row.Any(r => IsKey(r.Part.Look));
                double lineHeight = Math.Max(row.Count == 0 ? 0 : row.Max(r => r.Text.Height), size + 2) + (keys ? 4 : 0);
                foreach ((FormattedText text, LabelPart part) in row)
                {
                    double top = y + (lineHeight - text.Height) / 2;
                    if (IsKey(part.Look))
                    {
                        Rect key = new(x, y + 1.5, text.Width + 2 * KeyPadding, lineHeight - 3);
                        Brush back = part.Look switch
                        {
                            LabelLook.KeyOn => KeyOnBack,
                            LabelLook.KeyTurning => KeyTurningBack,
                            LabelLook.KeyStop => KeyStopBack,
                            _ => KeyBack
                        };
                        dc.DrawRoundedRectangle(back, KeyBorder, key, 2, 2);
                        dc.DrawText(text, new Point(x + KeyPadding, top));
                        if (part.Field != null) fields.Add((key, part.Field));
                    }
                    else
                    {
                        dc.DrawText(text, new Point(x, top));
                        if (part.Field != null) fields.Add((new Rect(x - 1, y, text.Width + 2, lineHeight), part.Field));
                    }
                    x += PartWidth(text, part);
                }
                y += lineHeight;
            }
            labelBoxes.Add(new DrawnLabel(rect, position, callsign, fields));
        }

        /// <summary>The label under a point (the last drawn on top), or null.</summary>
        private DrawnLabel? LabelAt(Point at)
        {
            for (int i = labelBoxes.Count - 1; i >= 0; i--)
            {
                if (labelBoxes[i].Box.Contains(at)) return labelBoxes[i];
            }
            return null;
        }

        /// <summary>Left button on a label: a double-click puts it back; else the start of a drag (or of a click). True: taken.</summary>
        private bool LabelPress(Point at, int clicks)
        {
            // Not while the view is being dragged with the right button.
            if (dragFrom != null || LabelAt(at) is not DrawnLabel label) return false;
            if (clicks >= 2)
            {
                labelOffsets.Remove(label.Callsign);
                labelDrag = null;
                InvalidateVisual();
                return true;
            }
            labelDrag = new LabelDrag
            {
                Callsign = label.Callsign,
                Start = at,
                StartOffset = label.Box.TopLeft - label.Track,
                Field = label.Fields.FirstOrDefault(f => f.Box.Contains(at)).Field
            };
            CaptureMouse();
            return true;
        }

        /// <summary>The mouse moving with a label pressed: a few pixels make it a drag. True: taken.</summary>
        private bool LabelMove(Point at)
        {
            if (labelDrag == null) return false;
            Vector moved = at - labelDrag.Start;
            if (!labelDrag.Moved && moved.Length < 3) return true;
            labelDrag.Moved = true;
            labelOffsets[labelDrag.Callsign] = labelDrag.StartOffset + moved;
            InvalidateVisual();
            return true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            // FREE: the end of a drag of the view.
            if (EndLeftPan())
            {
                e.Handled = true;
                return;
            }
            if (RouteRelease(e.GetPosition(this)))
            {
                e.Handled = true;
                return;
            }
            if (labelDrag is not LabelDrag drag) return;
            labelDrag = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
            if (drag.Moved) return;
            // A click (no drag): a field gives its command; else the aircraft is selected.
            if (LabelCommands && drag.Field != null) LabelFieldClicked?.Invoke(drag.Callsign, drag.Field, e.GetPosition(this));
            else AircraftClicked?.Invoke(drag.Callsign);
        }

        /// <summary>Right button: the menu (Instructor+), or hide a label / show it again from its track (RDR). True: taken.</summary>
        private bool LabelRightClick(Point at)
        {
            DrawnLabel? label = LabelAt(at);
            (Point At, string Callsign)? track = drawn.Count == 0 ? null : drawn.OrderBy(d => (d.At - at).Length).First();
            string? onTrack = track != null && (track.Value.At - at).Length <= 12 ? track.Value.Callsign : null;
            if (LabelCommands)
            {
                string? callsign = label?.Callsign ?? onTrack;
                if (callsign == null) return false;
                pendingMenu = (callsign, at);
                return true;
            }
            if (label != null)
            {
                SetLabelHidden(label.Callsign, true);
                return true;
            }
            if (onTrack != null && hiddenLabels.Contains(onTrack))
            {
                SetLabelHidden(onTrack, false);
                return true;
            }
            return false;
        }

        /// <summary>The wheel over a field of a label (Instructor+). True: taken.</summary>
        private bool LabelWheel(Point at, int delta)
        {
            if (!LabelCommands || LabelAt(at) is not DrawnLabel label) return false;
            string? field = label.Fields.FirstOrDefault(f => f.Box.Contains(at)).Field;
            if (field == null) return false;
            LabelFieldWheel?.Invoke(label.Callsign, field, delta > 0 ? 1 : -1);
            return true;
        }
    }
}
