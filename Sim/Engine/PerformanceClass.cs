using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATCRoom.Sim
{
    /// <summary>
    /// What a kind of aircraft can do: speeds, climb and descent limits, accelerations, bank limit. Kept in the open
    /// data file performance.json (see docs/SIM-FORMAT.md), so the values can be changed without a new build.
    /// Speeds are true airspeeds (kt), vertical speeds ft/min, accelerations per second.
    /// </summary>
    public sealed class PerformanceClass
    {
        public string Name { get; set; } = "";
        public double MinSpeedKt { get; set; }
        public double MaxSpeedKt { get; set; }
        /// <summary>Usual speed on the final (the default speed of a new aircraft of this class).</summary>
        public double ApproachSpeedKt { get; set; }
        public double MaxClimbFpm { get; set; }
        /// <summary>Largest rate of descent (ft/min, positive).</summary>
        public double MaxDescentFpm { get; set; }
        public double AccelerationKtPerSecond { get; set; }
        public double DecelerationKtPerSecond { get; set; }
        /// <summary>How fast the vertical speed changes (ft/min per second): a change from −700 to −1000 takes about a second at 300.</summary>
        public double VerticalAccelerationFpmPerSecond { get; set; }
        /// <summary>Bank angle limit for the turns (degrees): the turn rate is lower at high speed.</summary>
        public double MaxBankDegrees { get; set; }

        /// <summary>Fields of a newer version of the file, kept as they are.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        public PerformanceClass Copy()
        {
            PerformanceClass copy = (PerformanceClass)MemberwiseClone();
            copy.Extra = Extra == null ? null : new Dictionary<string, JsonElement>(Extra);
            return copy;
        }

        /// <summary>Repairs values written by hand: inside sensible limits, minimum ≤ approach ≤ maximum.</summary>
        public void Normalize()
        {
            Name = (Name ?? "").Trim();
            MinSpeedKt = Clamp(MinSpeedKt, 30, 400, 100);
            MaxSpeedKt = Clamp(MaxSpeedKt, MinSpeedKt + 10, SimEngine.MaxSpeed, Math.Max(MinSpeedKt + 10, 250));
            ApproachSpeedKt = Clamp(ApproachSpeedKt, MinSpeedKt, MaxSpeedKt, (MinSpeedKt + MaxSpeedKt) / 2);
            MaxClimbFpm = Clamp(MaxClimbFpm, 200, 8000, 2000);
            MaxDescentFpm = Clamp(MaxDescentFpm, 200, 8000, 3000);
            AccelerationKtPerSecond = Clamp(AccelerationKtPerSecond, 0.2, 10, 1.5);
            DecelerationKtPerSecond = Clamp(DecelerationKtPerSecond, 0.2, 10, 1);
            VerticalAccelerationFpmPerSecond = Clamp(VerticalAccelerationFpmPerSecond, 50, 2000, 300);
            MaxBankDegrees = Clamp(MaxBankDegrees, 10, 60, 30);
        }

        private static double Clamp(double value, double min, double max, double fallback) =>
            double.IsFinite(value) && value > 0 ? Math.Clamp(value, min, max) : Math.Clamp(fallback, min, max);

        /// <summary>The classes of a first start: a light aircraft, a turboprop and a jet.</summary>
        public static List<PerformanceClass> Defaults() =>
        [
            new() { Name = "Light", MinSpeedKt = 60, MaxSpeedKt = 160, ApproachSpeedKt = 90, MaxClimbFpm = 800, MaxDescentFpm = 1500,
                AccelerationKtPerSecond = 2, DecelerationKtPerSecond = 1.5, VerticalAccelerationFpmPerSecond = 400, MaxBankDegrees = 30 },
            new() { Name = "Turboprop", MinSpeedKt = 90, MaxSpeedKt = 280, ApproachSpeedKt = 120, MaxClimbFpm = 2000, MaxDescentFpm = 3000,
                AccelerationKtPerSecond = 1.5, DecelerationKtPerSecond = 1, VerticalAccelerationFpmPerSecond = 400, MaxBankDegrees = 30 },
            new() { Name = "Jet", MinSpeedKt = 120, MaxSpeedKt = 350, ApproachSpeedKt = 140, MaxClimbFpm = 3000, MaxDescentFpm = 4000,
                AccelerationKtPerSecond = 1.5, DecelerationKtPerSecond = 1, VerticalAccelerationFpmPerSecond = 300, MaxBankDegrees = 30 }
        ];

        /// <summary>The class a new aircraft gets when none is chosen.</summary>
        public const string DefaultName = "Turboprop";
    }

    /// <summary>
    /// The file performance.json: the performance classes. Read at start; written only when it is missing (then with
    /// the defaults). A file made by a newer version of the program is used but never overwritten; fields this
    /// version does not know are kept.
    /// </summary>
    public sealed class PerformanceFile
    {
        public const int CurrentFormatVersion = 1;
        public const string FileName = "performance.json";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // Forgiving with hand edits: "minSpeedKt" or "MinSpeedKt", 120 or "120".
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public List<PerformanceClass> Classes { get; set; } = PerformanceClass.Defaults();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>The classes, repaired: names unique and not empty, at least one class.</summary>
        public List<PerformanceClass> NormalizedClasses()
        {
            List<PerformanceClass> result = [];
            foreach (PerformanceClass item in Classes ?? [])
            {
                if (item == null) continue;
                PerformanceClass copy = item.Copy();
                copy.Normalize();
                if (copy.Name.Length == 0 || result.Any(c => string.Equals(c.Name, copy.Name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(copy);
            }
            return result.Count > 0 ? result : PerformanceClass.Defaults();
        }

        /// <summary>
        /// Reads the file. Missing or empty: the defaults, and the file is written. Damaged: kept as ".bad", the
        /// defaults written. Newer version: used as far as understood, <c>ReadOnly</c> (never written).
        /// <c>Message</c> says what happened when it was not a plain read (null otherwise).
        /// </summary>
        public static (List<PerformanceClass> Classes, bool ReadOnly, string? Message) Load(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    PerformanceFile created = new();
                    Write(path, created);
                    return (created.NormalizedClasses(), false, null);
                }
                PerformanceFile? file = JsonSerializer.Deserialize<PerformanceFile>(File.ReadAllText(path), JsonOptions);
                if (file == null) throw new JsonException("Empty document.");
                if (file.FormatVersion > CurrentFormatVersion)
                {
                    return (file.NormalizedClasses(), true, $"{FileName} was made by a newer version of AuroraPAR: it is used as far as this version understands it, and not changed.");
                }
                return (file.NormalizedClasses(), false, null);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (Exception) { }
                PerformanceFile created = new();
                try { Write(path, created); } catch (Exception) { }
                return (created.NormalizedClasses(), false, $"{FileName} could not be read: it was kept as {FileName}.bad and the default classes were written.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (PerformanceClass.Defaults(), true, $"{FileName} could not be read or written ({ex.Message}): the default classes are used.");
            }
        }

        public static void Write(string path, PerformanceFile file)
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
        }
    }
}
