using System.IO;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    /// <summary>Helpers that write files with valid magic-number headers.</summary>
    internal static class TestFiles
    {
        public static void WritePng(string path)
        {
            var bytes = new byte[32];
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            sig.CopyTo(bytes, 0);
            File.WriteAllBytes(path, bytes);
        }

        public static void WriteJpeg(string path)
        {
            var bytes = new byte[32];
            byte[] sig = { 0xFF, 0xD8, 0xFF, 0xE0 };
            sig.CopyTo(bytes, 0);
            File.WriteAllBytes(path, bytes);
        }

        public static void WriteMp4(string path)
        {
            // 4 bytes size, then "ftyp" atom.
            var bytes = new byte[32];
            bytes[3] = 0x18;
            bytes[4] = (byte)'f';
            bytes[5] = (byte)'t';
            bytes[6] = (byte)'y';
            bytes[7] = (byte)'p';
            File.WriteAllBytes(path, bytes);
        }

        public static void WriteGarbage(string path) => File.WriteAllText(path, "not a real media file at all");
    }
}
