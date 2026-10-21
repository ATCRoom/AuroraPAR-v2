using System.IO;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>A map of the Maps folder, with the name of its file (the key of its settings).</summary>
    internal sealed record LoadedMap(string FileName, MapFile Map);

    /// <summary>
    /// The maps of the RDR: the files of the Maps folder beside the settings of the Trainer (%AppData%\AuroraPARTrainer\Maps,
    /// or beside the program in portable mode), in the open ATCRoom map format made by ATCRoom Map Creator.
    /// </summary>
    internal static class MapLibrary
    {
        public static string Folder => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsStore.FilePath) ?? AppContext.BaseDirectory, "Maps");

        /// <summary>ATCRoom Map Creator, when it is beside the Trainer.</summary>
        public static string MapCreatorPath => System.IO.Path.Combine(AppContext.BaseDirectory, "ATCRoomMapCreator.exe");

        /// <summary>Reads all the maps; a file that is not a map is listed in the errors (by name) and skipped.</summary>
        public static (List<LoadedMap> Maps, List<string> Errors) Load()
        {
            List<LoadedMap> maps = [];
            List<string> errors = [];
            try
            {
                if (!Directory.Exists(Folder)) return (maps, errors);
                foreach (string file in Directory.GetFiles(Folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                {
                    string name = System.IO.Path.GetFileName(file);
                    try
                    {
                        maps.Add(new LoadedMap(name, MapFile.Load(file)));
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Maps folder: {ex.Message}");
            }
            return (maps, errors);
        }
    }
}
