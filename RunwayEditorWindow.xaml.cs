using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Graphical editor of the runway file. Values are edited as text (so a half-typed value is never lost),
    /// checked while typing, and written to runways.par only with Save.
    /// </summary>
    public partial class RunwayEditorWindow : Window
    {
        /// <summary>
        /// A runway being edited: the text of each field.
        /// </summary>
        private sealed class Draft
        {
            private readonly Dictionary<string, string> values = [];
            /// <summary>Line in the file this runway comes from (null for a new runway).</summary>
            public int? SourceLine;
            /// <summary>Original line text, written back unchanged if the runway was not modified.</summary>
            public string? SourceText;
            /// <summary>For a new runway: file line after which it is saved.</summary>
            public int? InsertAfterLine;
            public bool Modified;

            public string this[string key]
            {
                get => values.TryGetValue(key, out string? v) ? v : "";
                set => values[key] = value;
            }

            public Draft Copy()
            {
                Draft copy = new();
                foreach (KeyValuePair<string, string> pair in values)
                {
                    copy.values[pair.Key] = pair.Value;
                }
                return copy;
            }

            public override string ToString()
            {
                // Same text as Runway.ToString() of the saved runway (ICAO in upper case, designator as typed).
                string name = $"{this["icao"].Trim().ToUpperInvariant()} {this["designator"].Trim()}".Trim();
                return name.Length > 0 ? name : "(new runway)";
            }
        }

        private sealed record FieldDefinition(string Key, string Label, string Hint);

        private static readonly FieldDefinition[] Fields =
        [
            new("icao", "Airport ICAO", "e.g. LIRF"),
            new("designator", "Runway / approach", "free text, e.g. 16L or 14 3.0"),
            new("heading", "Runway heading (° true)", "true heading, not magnetic"),
            new("elevation", "Threshold elevation (ft)", ""),
            new("latitude", "Threshold latitude", "any format: 41°48'10.5\"N, 414810.5N, 41.80292..."),
            new("longitude", "Threshold longitude", "any format: 012°15'03\"E, 0121503E, 12.25083..."),
            new("length", "Runway length (m)", ""),
            new("width", "Runway width (m)", ""),
            new("glideslope", "Glide slope (°)", "usually 3.0"),
            new("tch", "Threshold crossing height (ft)", "usually 50"),
            new("dh", "Decision height (ft)", "height above the threshold"),
            new("range", "Default range (NM)", "1, 2.5, 5, 10, 15 or 20 (the closest is used)"),
            new("touchdown", "Touchdown from threshold (m)", "optional: empty = automatic"),
            new("magvar", "Magnetic variation", "optional, e.g. 3E or 2W: empty = default in Settings"),
        ];

        private static readonly Brush ErrorBackground = new SolidColorBrush(Color.FromRgb(255, 215, 215));

        private readonly string path;
        private readonly ObservableCollection<Draft> drafts = [];
        private readonly Dictionary<string, TextBox> boxes = [];
        private readonly Dictionary<string, TextBlock> notes = [];
        /// <summary>
        /// Draft currently shown in the fields.
        /// </summary>
        private Draft? shown;
        /// <summary>
        /// True while the fields are being filled by the program, so their change events are ignored.
        /// </summary>
        private bool loading;
        private bool dirty;

        /// <summary>
        /// True if the runway file was saved at least once.
        /// </summary>
        internal bool Saved { get; private set; }

        private readonly ReminderEditor reminderEditor;

        /// <param name="settings">Settings holding the reminders of each runway.</param>
        /// <param name="settingsChanged">Saves the settings and redraws (reminders are saved at once, not with Save).</param>
        internal RunwayEditorWindow(string path, IEnumerable<Runway> runways, string? selectedName, AppSettings settings, Action settingsChanged)
        {
            InitializeComponent();
            this.path = path;
            reminderEditor = new ReminderEditor(() => settings.RemindersOf(ReminderKey(shown)), settingsChanged);
            RemindersHost.Content = reminderEditor;
            BuildForm();
            foreach (Runway runway in runways)
            {
                drafts.Add(FromRunway(runway));
            }
            RunwayList.ItemsSource = drafts;
            RunwayList.SelectionChanged += (s, e) => ShowSelected();
            NewButton.Click += (s, e) => AddDraft(NewDraft());
            DuplicateButton.Click += (s, e) => DuplicateSelected();
            DeleteButton.Click += (s, e) => DeleteSelected();
            SaveButton.Click += (s, e) => Save();
            CloseButton.Click += (s, e) => Close();
            Closing += RunwayEditorWindow_Closing;
            StatusText.Text = $"File: {path}";
            Draft? selected = drafts.FirstOrDefault(d => d.ToString() == selectedName) ?? drafts.FirstOrDefault();
            RunwayList.SelectedItem = selected;
            ShowSelected();
        }

        private void BuildForm()
        {
            int row = 0;
            foreach (FieldDefinition field in Fields)
            {
                FieldGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = new()
                {
                    Text = field.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4)
                };
                TextBox box = new()
                {
                    Height = 24,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                TextBlock note = new()
                {
                    Text = field.Hint,
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 4, 0, 4)
                };
                Grid.SetRow(label, row);
                Grid.SetRow(box, row);
                Grid.SetRow(note, row);
                Grid.SetColumn(box, 1);
                Grid.SetColumn(note, 2);
                FieldGrid.Children.Add(label);
                FieldGrid.Children.Add(box);
                FieldGrid.Children.Add(note);
                boxes[field.Key] = box;
                notes[field.Key] = note;
                string key = field.Key;
                box.TextChanged += (s, e) => FieldChanged(key);
                row++;
            }
        }

        private Draft? Selected => RunwayList.SelectedItem as Draft;

        private void ShowSelected()
        {
            Draft? draft = Selected;
            if (draft == shown && draft != null) return;
            shown = draft;
            loading = true;
            try
            {
                foreach (FieldDefinition field in Fields)
                {
                    boxes[field.Key].Text = draft?[field.Key] ?? "";
                    boxes[field.Key].IsEnabled = draft != null;
                }
            }
            finally
            {
                loading = false;
            }
            DuplicateButton.IsEnabled = draft != null;
            DeleteButton.IsEnabled = draft != null;
            UpdateNotes();
            UpdateReminders();
        }

        /// <summary>Reminders are kept by "ICAO DESIGNATOR", like the runway list.</summary>
        private static string ReminderKey(Draft? draft)
        {
            return draft == null ? "" : $"{draft["icao"].Trim().ToUpperInvariant()} {draft["designator"].Trim()}";
        }

        private void UpdateReminders()
        {
            RemindersGroup.IsEnabled = shown != null;
            RemindersInfo.Text = shown == null
                ? ""
                : $"Reminders of {ReminderKey(shown)} only, drawn in addition to those for all runways (Settings → Reminders). "
                  + "They are kept with the airport and runway name and saved at once (not with Save).";
            reminderEditor.Rebuild();
        }

        private void FieldChanged(string key)
        {
            if (loading || shown is not Draft draft) return;
            if (key == "icao" || key == "designator")
            {
                // The reminders belong to the airport and runway name.
                Dispatcher.BeginInvoke(new Action(UpdateReminders));
            }
            string text = boxes[key].Text;
            bool isCoordinate = key == "latitude" || key == "longitude";
            // A latitude and longitude pasted together in one field are split into both fields.
            if (isCoordinate && !CoordinateParser.TryParse(text, key == "latitude", out _)
                && CoordinateParser.TryParsePair(text, out double latitude, out double longitude))
            {
                draft["latitude"] = CoordinateParser.Format(latitude);
                draft["longitude"] = CoordinateParser.Format(longitude);
                loading = true;
                boxes["latitude"].Text = draft["latitude"];
                boxes["longitude"].Text = draft["longitude"];
                loading = false;
            }
            else
            {
                draft[key] = text;
            }
            draft.Modified = true;
            dirty = true;
            if (key == "icao" || key == "designator")
            {
                RunwayList.Items.Refresh();
            }
            UpdateNotes();
        }

        /// <summary>
        /// Shows, next to each field, its error (with a red field), the converted value or the hint.
        /// </summary>
        private void UpdateNotes()
        {
            foreach (FieldDefinition field in Fields)
            {
                TextBox box = boxes[field.Key];
                TextBlock note = notes[field.Key];
                if (shown == null)
                {
                    box.ClearValue(BackgroundProperty);
                    note.Text = field.Hint;
                    note.Foreground = Brushes.Gray;
                    continue;
                }
                string? error = Validate(shown, field.Key, out string? info);
                if (error != null)
                {
                    box.Background = ErrorBackground;
                    note.Text = error;
                    note.Foreground = Brushes.DarkRed;
                }
                else
                {
                    box.ClearValue(BackgroundProperty);
                    note.Text = info ?? field.Hint;
                    note.Foreground = info != null ? Brushes.DarkGreen : Brushes.Gray;
                }
            }
        }

        /// <summary>
        /// Checks one field. Returns the error, or null if valid; <paramref name="info"/> is an optional
        /// confirmation to show (e.g. the converted coordinate).
        /// </summary>
        private static string? Validate(Draft draft, string key, out string? info)
        {
            info = null;
            string text = draft[key].Trim();
            switch (key)
            {
                case "icao":
                    if (text.Length == 0) return "required";
                    if (text.Contains(';') || text.Contains(' ')) return "spaces and ; are not allowed";
                    return null;
                case "designator":
                    if (text.Length == 0) return "required";
                    if (text.Contains(';')) return "; is not allowed";
                    return null;
                case "heading": return CheckNumber(text, 0, 360);
                case "elevation": return CheckNumber(text, -1500, 15000);
                case "latitude":
                    if (!CoordinateParser.TryParse(text, true, out double latitude)) return "not a valid latitude";
                    info = $"= {CoordinateParser.Format(Math.Abs(latitude))}° {(latitude >= 0 ? "N" : "S")}";
                    return null;
                case "longitude":
                    if (!CoordinateParser.TryParse(text, false, out double longitude)) return "not a valid longitude";
                    info = $"= {CoordinateParser.Format(Math.Abs(longitude))}° {(longitude >= 0 ? "E" : "W")}";
                    return null;
                case "length": return CheckNumber(text, 100, 6000);
                case "width": return CheckNumber(text, 5, 150);
                case "glideslope": return CheckNumber(text, 1, 10);
                case "tch": return CheckNumber(text, 0, 200);
                case "dh": return CheckNumber(text, 0, 5000);
                case "range": return CheckNumber(text, 0.5, 100);
                case "touchdown":
                    if (text.Length == 0)
                    {
                        if (TryNumber(draft["tch"], out double tch) && TryNumber(draft["glideslope"], out double glideSlope) && glideSlope > 0)
                        {
                            info = $"automatic: {tch * 0.3048 / Math.Tan(glideSlope * Math.PI / 180):0} m";
                        }
                        return null;
                    }
                    return CheckNumber(text, 0, 3000);
                case "magvar":
                    if (text.Length == 0) return null;
                    if (!MagneticVariation.TryParse(text, out double variation)) return "e.g. 3E, 2.5W or -2";
                    if (TryNumber(draft["heading"], out double trueHeading))
                    {
                        info = $"final course {MagneticVariation.FinalCourse(trueHeading, variation):000}° magnetic";
                    }
                    return null;
                default:
                    return null;
            }
        }

        private static string? CheckNumber(string text, double min, double max)
        {
            if (!TryNumber(text, out double value)) return "not a number";
            if (value < min || value > max) return $"must be between {min.ToString(CultureInfo.CurrentCulture)} and {max.ToString(CultureInfo.CurrentCulture)}";
            return null;
        }

        /// <summary>
        /// Numbers can be typed with a dot or a comma as decimal separator.
        /// </summary>
        private static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double Number(Draft draft, string key)
        {
            TryNumber(draft[key], out double value);
            return value;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static Draft FromRunway(Runway runway)
        {
            Draft draft = new()
            {
                SourceLine = runway.SourceLine,
                SourceText = runway.SourceText
            };
            if (runway.SourceText != null)
            {
                // Show the values exactly as written in the file (e.g. 3.0 stays 3.0, 082.1 stays 082.1).
                string[] f = runway.SourceText.Split(';', StringSplitOptions.TrimEntries);
                string[] keys = ["icao", "designator", "heading", "elevation", "latitude", "longitude", "length", "width", "glideslope", "tch", "dh", "range", "touchdown", "magvar"];
                for (int i = 0; i < keys.Length; i++)
                {
                    draft[keys[i]] = i < f.Length ? f[i] : "";
                }
                return draft;
            }
            draft["icao"] = runway.ICAO;
            draft["designator"] = runway.Designator;
            draft["heading"] = FormatNumber(runway.Heading);
            draft["elevation"] = FormatNumber(runway.Elevation);
            draft["latitude"] = CoordinateParser.Format(runway.Latitude);
            draft["longitude"] = CoordinateParser.Format(runway.Longitude);
            draft["length"] = FormatNumber(runway.LengthM);
            draft["width"] = FormatNumber(runway.WidthM);
            draft["glideslope"] = FormatNumber(runway.GlideSlope);
            draft["tch"] = FormatNumber(runway.TCH);
            draft["dh"] = FormatNumber(runway.DefaultMDH);
            draft["range"] = FormatNumber(runway.DefaultDistance);
            draft["touchdown"] = runway.TouchdownOverrideM is double touchdown ? FormatNumber(touchdown) : "";
            draft["magvar"] = runway.MagneticVariation is double variation ? MagneticVariation.Format(variation) : "";
            return draft;
        }

        private static Runway ToRunway(Draft draft)
        {
            CoordinateParser.TryParse(draft["latitude"], true, out double latitude);
            CoordinateParser.TryParse(draft["longitude"], false, out double longitude);
            double dh = Number(draft, "dh");
            double range = Number(draft, "range");
            return new Runway
            {
                SourceLine = draft.SourceLine,
                SourceText = draft.Modified ? null : draft.SourceText,
                InsertAfterLine = draft.InsertAfterLine,
                ICAO = draft["icao"].Trim().ToUpperInvariant(),
                Designator = draft["designator"].Trim(),
                Heading = Number(draft, "heading"),
                Elevation = Number(draft, "elevation"),
                Latitude = latitude,
                Longitude = longitude,
                LengthM = Number(draft, "length"),
                WidthM = Number(draft, "width"),
                GlideSlope = Number(draft, "glideslope"),
                TCH = Number(draft, "tch"),
                MDH = dh,
                DefaultMDH = dh,
                Distance = range,
                DefaultDistance = range,
                TouchdownOverrideM = string.IsNullOrWhiteSpace(draft["touchdown"]) ? null : Number(draft, "touchdown"),
                MagneticVariation = MagneticVariation.TryParse(draft["magvar"], out double variation) ? variation : null
            };
        }

        private static Draft NewDraft()
        {
            Draft draft = new() { Modified = true };
            draft["width"] = "45";
            draft["glideslope"] = "3.0";
            draft["tch"] = "50";
            draft["dh"] = "200";
            draft["range"] = "10";
            return draft;
        }

        private void AddDraft(Draft draft, int index = -1)
        {
            if (index < 0 || index > drafts.Count) drafts.Add(draft);
            else drafts.Insert(index, draft);
            dirty = true;
            RunwayList.SelectedItem = draft;
            RunwayList.ScrollIntoView(draft);
            boxes["icao"].Focus();
        }

        /// <summary>
        /// Copy of the selected runway, e.g. to create the opposite end: the designator is left empty
        /// (then heading, coordinates and elevation of the other threshold need to be changed).
        /// </summary>
        private void DuplicateSelected()
        {
            if (Selected is not Draft draft) return;
            Draft copy = draft.Copy();
            copy["designator"] = "";
            copy.Modified = true;
            // Saved right after the runway it was copied from.
            copy.InsertAfterLine = draft.SourceLine ?? draft.InsertAfterLine;
            AddDraft(copy, drafts.IndexOf(draft) + 1);
            boxes["designator"].Focus();
        }

        private void DeleteSelected()
        {
            if (Selected is not Draft draft) return;
            if (MessageBox.Show(this, $"Delete the runway {draft}?", "Aurora PAR", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
            int index = drafts.IndexOf(draft);
            drafts.Remove(draft);
            dirty = true;
            if (drafts.Count > 0)
            {
                RunwayList.SelectedIndex = Math.Min(index, drafts.Count - 1);
            }
            ShowSelected();
        }

        /// <summary>
        /// Checks every runway and writes the file. Returns false if something is wrong or the file cannot be written.
        /// </summary>
        private bool Save()
        {
            foreach (Draft draft in drafts)
            {
                foreach (FieldDefinition field in Fields)
                {
                    string? error = Validate(draft, field.Key, out _);
                    if (error != null)
                    {
                        RunwayList.SelectedItem = draft;
                        ShowSelected();
                        boxes[field.Key].Focus();
                        MessageBox.Show(this, $"Runway {draft}: {field.Label}: {error}.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }
            IGrouping<string, Draft>? duplicate = drafts.GroupBy(d => d.ToString()).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                RunwayList.SelectedItem = duplicate.Last();
                ShowSelected();
                MessageBox.Show(this, $"The runway {duplicate.Key} is defined more than once.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                DataFile.SaveRunways(path, drafts.Select(ToRunway).ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot write the runway file {path}:\n{ex.Message}\n\nIf the program is in a protected folder (e.g. Program Files), move it to another folder.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            Saved = true;
            dirty = false;
            // Read the file again, so line numbers and texts match what was written (needed for a further save).
            string? selectedName = Selected?.ToString();
            try
            {
                Runway[] saved = DataFile.ReadRunways(path);
                shown = null;
                drafts.Clear();
                foreach (Runway runway in saved)
                {
                    drafts.Add(FromRunway(runway));
                }
                RunwayList.SelectedItem = drafts.FirstOrDefault(d => d.ToString() == selectedName) ?? drafts.FirstOrDefault();
                ShowSelected();
            }
            catch (Exception)
            {
                // The file was written; it will be read again by the main window.
            }
            StatusText.Text = $"Saved {drafts.Count} runways to {path} (previous version kept as runways.par.bak).";
            return true;
        }

        private void RunwayEditorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!dirty) return;
            MessageBoxResult answer = MessageBox.Show(this, "Save the changes to the runway file?", "Aurora PAR", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
            {
                e.Cancel = true;
            }
        }
    }
}
