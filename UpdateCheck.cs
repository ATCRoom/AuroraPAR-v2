using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace AuroraPAR
{
    /// <summary>
    /// New version notice: at the start, at most once a day, asks GitHub for the published build (Releases) and, when
    /// it is newer than this one, offers to open its page (Releases). Nothing is downloaded or installed: the user
    /// replaces the files as usual. Silent when there is no internet, no answer, or a build made outside GitHub.
    /// </summary>
    internal static class UpdateCheck
    {
        /// <summary>The most recent release (versions and pre-releases), newest first.</summary>
        private const string ReleaseApi = "https://api.github.com/repos/ATCRoom/AuroraPAR-v2/releases?per_page=1";

        private sealed record Release(string Name, string Commit, DateTime Published, string Page);

        public static async Task RunAsync(Window owner, AppSettings settings)
        {
#if TRAINER
            // The Trainer has no releases yet: its own check comes with its first release (the newest release of the
            // repository may be one of AuroraPAR v2).
            await Task.CompletedTask;
            return;
#else
            if (!settings.CheckForUpdates) return;
            string commit = AppInfo.FullCommit;
            if (commit.Length == 0 || AppInfo.BuildTime == null) return; // not a GitHub build: nothing to compare
            DateTime now = DateTime.UtcNow;
            if (settings.LastUpdateCheck is DateTime last && now - last < TimeSpan.FromHours(20)) return;
            Release? release = await FetchAsync();
            if (release == null) return;
            settings.LastUpdateCheck = now;
            SettingsStore.Save(settings);
            // Newer: another commit, published after this build (a test build made later than the release is not older).
            bool newer = !string.Equals(release.Commit, commit, StringComparison.OrdinalIgnoreCase)
                && release.Published > AppInfo.BuildTime.Value.AddMinutes(1);
            if (!newer || string.Equals(settings.SkippedRelease, release.Commit, StringComparison.OrdinalIgnoreCase)) return;
            MessageBoxResult answer = MessageBox.Show(owner,
                $"A new version of AuroraPAR v2 is available:\n{release.Name}\n\n" +
                $"You have {AppInfo.FullVersion}.\n\n" +
                "Open the download page now?\n\n" +
                "Yes: open the page (download the zip and replace the files; your settings and profiles are kept).\n" +
                "No: remind me tomorrow.\n" +
                "Cancel: do not tell me again about this version.",
                "AuroraPAR v2 - new version", MessageBoxButton.YesNoCancel, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true }); } catch (Exception) { }
            }
            else if (answer == MessageBoxResult.Cancel)
            {
                settings.SkippedRelease = release.Commit;
                SettingsStore.Save(settings);
            }
#endif
        }

        private static async Task<Release?> FetchAsync()
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AuroraPAR/" + AppInfo.Version.Replace(' ', '-'));
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using JsonDocument json = JsonDocument.Parse(await http.GetStringAsync(ReleaseApi));
                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) return null;
                JsonElement root = json.RootElement[0];
                string name = root.GetProperty("name").GetString() ?? "";
                string commit = root.GetProperty("target_commitish").GetString() ?? "";
                string page = root.GetProperty("html_url").GetString() ?? "";
                DateTime published = root.GetProperty("published_at").GetDateTime().ToUniversalTime();
                if (commit.Length == 0 || !page.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return null;
                return new Release(name, commit, published, page);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
