using System.Reflection;

namespace AuroraPAR
{
    /// <summary>Version of the program (csproj Version) and the date and commit of the build (set by the build).</summary>
    internal static class AppInfo
    {
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

        /// <summary>Build date (UTC, yyyy-MM-dd), or empty.</summary>
        public static string BuildDate => Metadata("BuildDate") ?? "";

        /// <summary>Short commit of the build, or empty (local builds).</summary>
        public static string Commit
        {
            get
            {
                string? commit = Metadata("Commit");
                return string.IsNullOrEmpty(commit) ? "" : commit[..Math.Min(7, commit.Length)];
            }
        }

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
