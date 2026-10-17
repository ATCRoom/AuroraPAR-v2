using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuroraPAR
{
    /// <summary>
    /// What the program knows of a bundled profile: the version last taken and the profile as it was then (JSON), to
    /// see later whether the user changed it. Empty snapshot: the user's own profile with that name (never replaced).
    /// </summary>
    internal sealed class BundledProfileRecord
    {
        public int Version { get; set; }
        public string Snapshot { get; set; } = "";
    }

    /// <summary>Profiles added or updated at the start by <see cref="BundledProfiles.Sync"/>, to tell the user.</summary>
    internal sealed class BundledSyncResult
    {
        public List<string> Added { get; } = [];
        public List<string> Updated { get; } = [];
        /// <summary>(the user's profile, the copy with the new version)</summary>
        public List<(string Kept, string Copy)> Copies { get; } = [];
        public bool Changed { get; set; }
        public bool HasNews => Added.Count + Updated.Count + Copies.Count > 0;
    }

    /// <summary>
    /// Keeps the profiles that come with the program (Profiles folder) up to date without losing the user's changes.
    /// Each bundled profile has a <see cref="Profile.BundledVersion"/>, raised when the profile is changed in a new
    /// package. At every start: a bundled profile the user never had is added; a newer version replaces the user's
    /// profile only if the user never changed it (same hash as when it was taken), otherwise the user's profile is
    /// kept and the new version is added beside it as "Name (updated)". A bundled profile the user deleted or renamed
    /// is not brought back. What the keys change while working (brightness, A, C, X, O) is not counted as a change and is
    /// kept on an update.
    /// </summary>
    internal static class BundledProfiles
    {
        public static BundledSyncResult Sync(AppSettings settings, bool firstStart)
        {
            BundledSyncResult result = new();
            foreach (Profile bundled in SettingsStore.ReadBundledProfiles())
            {
                string key = bundled.Name.ToLowerInvariant();
                int index = settings.Profiles.FindIndex(p => string.Equals(p.Name, bundled.Name, StringComparison.OrdinalIgnoreCase));
                settings.BundledProfiles.TryGetValue(key, out BundledProfileRecord? record);
                if (record == null)
                {
                    if (index < 0)
                    {
                        // A bundled profile never seen: added (on a first start they are all there already).
                        settings.Profiles.Add(bundled.Clone());
                        if (!firstStart) result.Added.Add(bundled.Name);
                        settings.BundledProfiles[key] = Record(bundled);
                    }
                    else
                    {
                        // Taken before this check existed (or the user's own profile with the same name): followed from
                        // now on only if it is exactly the bundled one; otherwise it is the user's and never replaced.
                        settings.BundledProfiles[key] = Hash(settings.Profiles[index]) == Hash(bundled)
                            ? Record(bundled)
                            : new() { Version = bundled.BundledVersion, Snapshot = "" };
                    }
                    result.Changed = true;
                    continue;
                }
                if (bundled.BundledVersion <= record.Version) continue;
                result.Changed = true;
                if (index < 0)
                {
                    // Deleted or renamed by the user: not brought back.
                    record.Version = bundled.BundledVersion;
                    continue;
                }
                Profile mine = settings.Profiles[index];
                if (Unchanged(mine, record))
                {
                    // Never changed by the user: replaced by the new version (what the keys change stays as the user left it).
                    Profile updated = bundled.Clone();
                    updated.Name = mine.Name;
                    KeepUserToggles(mine, updated);
                    settings.Profiles[index] = updated;
                    settings.BundledProfiles[key] = Record(bundled);
                    result.Updated.Add(mine.Name);
                }
                else
                {
                    // Changed by the user: kept; the new version beside it.
                    Profile copy = bundled.Clone();
                    copy.Name = settings.UniqueName(bundled.Name + " (updated)");
                    settings.Profiles.Add(copy);
                    record.Version = bundled.BundledVersion;
                    result.Copies.Add((mine.Name, copy.Name));
                }
            }
            return result;
        }

        /// <summary>After "Reload bundled": the profiles are the bundled ones again.</summary>
        public static void Reloaded(AppSettings settings, IEnumerable<Profile> bundled)
        {
            foreach (Profile profile in bundled)
            {
                settings.BundledProfiles[profile.Name.ToLowerInvariant()] = Record(profile);
            }
        }

        private static BundledProfileRecord Record(Profile bundled) =>
            new() { Version = bundled.BundledVersion, Snapshot = JsonSerializer.Serialize(bundled, SettingsStore.JsonOptions) };

        /// <summary>
        /// The user's profile is still the one taken (snapshot read again with the code of today, so a new version of
        /// the program that adds options does not make every profile look changed).
        /// </summary>
        private static bool Unchanged(Profile mine, BundledProfileRecord record)
        {
            if (string.IsNullOrEmpty(record.Snapshot)) return false;
            try
            {
                Profile? taken = JsonSerializer.Deserialize<Profile>(record.Snapshot, SettingsStore.JsonOptions);
                return taken != null && Hash(taken) == Hash(mine);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>What the keys and knobs change while working (brightness, A, C, X, O): kept on an update.</summary>
        private static void KeepUserToggles(Profile from, Profile to)
        {
            to.BrightnessModern = from.BrightnessModern;
            to.BrightnessAnalog = from.BrightnessAnalog;
            to.DisplayMode = from.DisplayMode;
            to.CompressedScale = from.CompressedScale;
            to.DeviationCrosshair = from.DeviationCrosshair;
            to.TerrainDisplay = from.TerrainDisplay;
            to.ApplyScaleLock();
        }

        /// <summary>
        /// Fingerprint of a profile's options, without its name, its bundled version and what the keys and knobs change
        /// while working (brightness, display mode, LIN / LOG, crosshair, terrain). Taken on a normalized copy read
        /// back from JSON, so a profile saved and loaded again gives the same value.
        /// </summary>
        public static string Hash(Profile profile)
        {
            Profile copy = profile.Clone();
            copy.Normalize();
            copy.Name = "";
            copy.BundledVersion = 0;
            copy.BrightnessModern = 100;
            copy.BrightnessAnalog = 100;
            copy.DisplayMode = DisplayMode.Modern;
            copy.CompressedScale = false;
            copy.DeviationCrosshair = false;
            copy.TerrainDisplay = TerrainDisplay.Off;
            string json = JsonSerializer.Serialize(copy, SettingsStore.JsonOptions);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }

        /// <summary>The text for the user, or null when there is nothing to say.</summary>
        public static string? Message(BundledSyncResult result)
        {
            if (!result.HasNews) return null;
            StringBuilder text = new("The profiles that come with AuroraPAR have changed in this version.\n\n");
            if (result.Added.Count > 0) text.Append("New profiles added:\n  ").Append(string.Join("\n  ", result.Added)).Append("\n\n");
            if (result.Updated.Count > 0) text.Append("Updated (you had not changed them):\n  ").Append(string.Join("\n  ", result.Updated)).Append("\n\n");
            if (result.Copies.Count > 0)
            {
                text.Append("You had changed these profiles: they are kept as they are, and the new version is added beside them:\n  ")
                    .Append(string.Join("\n  ", result.Copies.Select(c => $"{c.Kept}  →  new version: {c.Copy}"))).Append("\n\n")
                    .Append("Use the one you prefer; you can delete the other in Settings.\n\n");
            }
            text.Append("Change profile with P, or in Settings.");
            return text.ToString();
        }
    }
}
