using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATCRoom.Sim;

namespace AuroraPAR
{
    /// <summary>
    /// A flight plan of the Instructor+: an aircraft prepared before it flies (callsign, class, route). Activated, the
    /// aircraft starts on the first point (its altitude and speed) and flies the others. The points are in the plane of
    /// the engine around <see cref="OriginLatitude"/>/<see cref="OriginLongitude"/> (the threshold they were made with):
    /// if the reference runway changes they are placed again from their latitude and longitude.
    /// </summary>
    internal sealed class FlightPlan
    {
        /// <summary>Empty: the next free callsign of the prefix when activated.</summary>
        public string Callsign = "";
        public string Class = "";
        public List<RoutePoint> Points = [];
        public double OriginLatitude = double.NaN;
        public double OriginLongitude = double.NaN;
        public double OriginHeading;
        /// <summary>Callsign of the aircraft flying it now, or null (waiting).</summary>
        public string? Active;
        /// <summary>In the TRAFFIC LIST, waiting to start (▶ or Start all); false: waiting in Traffic generation, or flying.</summary>
        public bool Queued;
        /// <summary>
        /// Made by Quick (Move to TFC list), not a flight plan of Traffic generation: one point (the start) and the
        /// heading to start with (MANUAL). Not saved in scenarios; gone once its aircraft is gone or it leaves the list.
        /// </summary>
        public bool Quick;
        public double StartHeading;
    }

    /// <summary>A point of a saved scenario: latitude and longitude, altitude and speed (null: kept as they are).</summary>
    internal sealed class ScenarioPoint
    {
        public string Name { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? AltitudeFt { get; set; }
        public double? SpeedKt { get; set; }
    }

    internal sealed class ScenarioPlan
    {
        public string Callsign { get; set; } = "";
        public string Class { get; set; } = "";
        public List<ScenarioPoint> Points { get; set; } = [];
    }

    /// <summary>A scenario of the Instructor+ (file in the Scenarios folder of the Trainer): the flight plans, for an airport.</summary>
    internal sealed class ScenarioFile
    {
        public string Description { get; set; } = "Scenario of AuroraPARv2 Trainer (Instructor+): flight plans with their routes. Altitudes ft above mean sea level, speeds kt; null = kept as they are.";
        public string Airport { get; set; } = "";
        public string Runway { get; set; } = "";
        public List<ScenarioPlan> FlightPlans { get; set; } = [];

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        /// <summary>Folder of the scenarios, beside the settings of the Trainer.</summary>
        public static string Folder => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsStore.FilePath) ?? AppContext.BaseDirectory, "Scenarios");

        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

        public static ScenarioFile Load(string path) =>
            JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The file is empty.");
    }
}
