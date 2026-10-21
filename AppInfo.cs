using System.Reflection;

namespace AuroraPAR
{
    /// <summary>Version of the program (csproj Version) and the date and commit of the build (set by the build).</summary>
    internal static class AppInfo
    {
#if TRAINER
        /// <summary>Name of the program, shown in the title bar and in the messages.</summary>
        public const string ProductName = "AuroraPAR v2 Trainer";
        /// <summary>Folder of the settings in the user's settings folder (%AppData% on Windows): apart from AuroraPAR v2.</summary>
        public const string SettingsFolderName = "AuroraPARTrainer";
        /// <summary>Name of the settings file (also the file that turns on the portable mode, next to the program).</summary>
        public const string SettingsFileName = "AuroraPARTrainer.settings.json";
#else
        /// <summary>Name of the program, shown in the title bar and in the messages.</summary>
        public const string ProductName = "AuroraPAR v2";
        /// <summary>Folder of the settings in the user's settings folder (%AppData% on Windows).</summary>
        public const string SettingsFolderName = "AuroraPAR";
        /// <summary>Name of the settings file (also the file that turns on the portable mode, next to the program).</summary>
        public const string SettingsFileName = "AuroraPAR.settings.json";
#endif

        private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

        /// <summary>e.g. "2.0.0-beta" (the Version of the csproj).</summary>
        public static string Version
        {
            get
            {
                string? text = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(text))
                {
                    Version? v = Assembly.GetName().Version;
                    return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
                }
                int plus = text.IndexOf('+');
                return plus >= 0 ? text[..plus] : text;
            }
        }

        private static string? Metadata(string key) =>
            Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

        /// <summary>Time of the build (UTC), or null.</summary>
        public static DateTime? BuildTime =>
            DateTime.TryParse(Metadata("BuildTime"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime time) ? time : null;

        /// <summary>Build date (UTC, yyyy-MM-dd), or empty.</summary>
        public static string BuildDate => BuildTime?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";

        /// <summary>Full commit of the build (GitHub builds), or empty (local builds).</summary>
        public static string FullCommit => Metadata("Commit") ?? "";

        /// <summary>Short commit of the build, or empty (local builds).</summary>
        public static string Commit => FullCommit[..Math.Min(7, FullCommit.Length)];

        /// <summary>e.g. "2.0.0-beta (build 2026-10-17, a1b2c3d)".</summary>
        public static string FullVersion
        {
            get
            {
                string details = string.Join(", ", new[] { BuildDate, Commit }.Where(s => s.Length > 0));
                return details.Length > 0 ? $"{Version} (build {details})" : Version;
            }
        }
    }
}
