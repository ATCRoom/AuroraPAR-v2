using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATCRoom.Maps;

namespace AuroraPAR
{
    /// <summary>
    /// The maps of the student's airport sent by the instructor (the user, 9 Nov: as TWR Trainer): all its map files packed
    /// (JSON, compressed, as text) and cut in pieces, sent until the student has them all; read on the student's PC and kept
    /// in memory only, never saved there (the maps made with the Map Creator are not distributed).
    /// </summary>
    internal static class LessonMaps
    {
        /// <summary>The size of a piece (characters): small enough for the public relays.</summary>
        private const int PieceSize = 48_000;

        private sealed class Packed
        {
            public string File { get; set; } = "";
            public string Json { get; set; } = "";
        }

        /// <summary>The maps as pieces: their id (from their content) and the pieces.</summary>
        public static (string Id, List<string> Pieces) Pack(IEnumerable<LoadedMap> maps)
        {
            List<Packed> all = maps.Select(m => new Packed { File = Path.GetFileName(m.FileName), Json = m.Map.ToJson() }).ToList();
            byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(all));
            string id = Convert.ToHexString(SHA256.HashData(json))[..12].ToLowerInvariant();
            using MemoryStream packed = new();
            using (GZipStream zip = new(packed, CompressionLevel.SmallestSize, leaveOpen: true)) zip.Write(json);
            string text = Convert.ToBase64String(packed.ToArray());
            List<string> pieces = [];
            for (int at = 0; at < text.Length; at += PieceSize) pieces.Add(text.Substring(at, Math.Min(PieceSize, text.Length - at)));
            if (pieces.Count == 0) pieces.Add("");
            return (id, pieces);
        }

        /// <summary>The maps from all their pieces (null when they cannot be read).</summary>
        public static List<LoadedMap>? Unpack(IEnumerable<string> pieces)
        {
            try
            {
                byte[] packed = Convert.FromBase64String(string.Concat(pieces));
                using GZipStream zip = new(new MemoryStream(packed), CompressionMode.Decompress);
                using StreamReader reader = new(zip, Encoding.UTF8);
                List<Packed>? all = JsonSerializer.Deserialize<List<Packed>>(reader.ReadToEnd());
                if (all == null) return null;
                List<LoadedMap> maps = [];
                foreach (Packed item in all.Where(p => p != null && p.Json != null).Take(50))
                    maps.Add(new LoadedMap(Path.GetFileName(item.File ?? "map.json"), MapFile.Parse(item.Json)));
                return maps;
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or JsonException or ArgumentException)
            {
                return null;
            }
        }
    }
}
