using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuroraPAR
{
    /// <summary>What the program knows of a bundled profile: the version last taken and how it was then (hash).</summary>
    internal sealed class BundledProfileRecord
    {
        public int Version { get; set; }
        public string Hash { get; set; } = "";
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
    /// is not brought back. Brightness is not counted as a change (it is moved with the knob every day).
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
                        settings.BundledProfiles[key] = new() { Version = bundled.BundledVersion, Hash = Hash(bundled) };
                    }
                    else
                    {
                        // Taken before this check existed: whether it was changed is not known, so it is kept as it is;
                        // from now on its changes are followed.
                        settings.BundledProfiles[key] = new() { Version = bundled.BundledVersion, Hash = Hash(settings.Profiles[index]) };
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
                if (Hash(mine) == record.Hash)
                {
                    // Never changed by the user: replaced by the new version (the brightness stays).
                    Profile updated = bundled.Clone();
                    updated.Name = mine.Name;
                    updated.BrightnessModern = mine.BrightnessModern;
                    updated.BrightnessAnalog = mine.BrightnessAnalog;
                    settings.Profiles[index] = updated;
                    settings.BundledProfiles[key] = new() { Version = bundled.BundledVersion, Hash = Hash(updated) };
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
                settings.BundledProfiles[profile.Name.ToLowerInvariant()] = new() { Version = profile.BundledVersion, Hash = Hash(profile) };
            }
        }

        /// <summary>
        /// Fingerprint of a profile's options, without its name, its bundled version and the brightness. Taken on a
        /// normalized copy read back from JSON, so a profile saved and loaded again gives the same value.
        /// </summary>
        public static string Hash(Profile profile)
        {
            Profile copy = profile.Clone();
            copy.Normalize();
            copy.Name = "";
            copy.BundledVersion = 0;
            copy.BrightnessModern = 100;
            copy.BrightnessAnalog = 100;
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
