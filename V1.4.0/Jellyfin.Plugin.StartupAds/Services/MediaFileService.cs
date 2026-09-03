using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.StartupAds.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.Services
{
    public class MediaFileInfo
    {
        public string FileName { get; set; } = string.Empty;

        public string FullPath { get; set; } = string.Empty;

        public AdvertisementType Type { get; set; }

        public long SizeBytes { get; set; }

        public string ContentType { get; set; } = "application/octet-stream";
    }

    public class PathValidationResult
    {
        public bool Ok { get; set; }

        public string Message { get; set; } = string.Empty;

        public bool Exists { get; set; }

        public bool Readable { get; set; }

        public int CompatibleFileCount { get; set; }
    }

    /// <summary>
    /// Resolves, validates and enumerates the configured ads directory. Every file access the
    /// plugin performs goes through this service. Protections:
    /// <list type="bullet">
    ///   <item>only bare file names are accepted (no <c>/</c>, <c>\</c>, <c>..</c>, no rooted paths, no UNC);</item>
    ///   <item>the resolved candidate must be a direct child of the configured directory;</item>
    ///   <item>symlinks are canonicalised (final target) and the real target must still live inside
    ///         the real configured directory — a symlink escaping the folder is rejected;</item>
    ///   <item>the configured directory itself cannot be a system directory or a UNC share;</item>
    ///   <item>the file extension must be allow-listed AND the file content signature must match.</item>
    /// </list>
    /// Works on both Linux and Windows.
    /// </summary>
    public class MediaFileService
    {
        /// <summary>Hard cap on how many entries are enumerated in one directory scan.</summary>
        public const int MaxScanEntries = 5000;

        private static readonly HashSet<string> _imageExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif"
        };

        private static readonly HashSet<string> _videoExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".m4v", ".mov", ".ogv", ".ogg"
        };

        private static readonly string[] _forbidden = OperatingSystem.IsWindows()
            ? new[] { @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData\Jellyfin" }
            : new[]
            {
                "/etc", "/proc", "/sys", "/dev", "/boot", "/root", "/bin", "/sbin",
                "/usr/bin", "/usr/sbin", "/lib", "/lib64", "/var/log", "/run"
            };

        private readonly ILogger<MediaFileService> _logger;

        public MediaFileService(ILogger<MediaFileService> logger)
        {
            _logger = logger;
        }

        public static string ContentTypeFor(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".avif" => "image/avif",
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                ".ogv" or ".ogg" => "video/ogg",
                _ => "application/octet-stream"
            };
        }

        public static AdvertisementType? TypeFor(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            if (_imageExt.Contains(ext))
            {
                return AdvertisementType.Image;
            }

            if (_videoExt.Contains(ext))
            {
                return AdvertisementType.Video;
            }

            return null;
        }

        public bool IsExtensionAllowed(string fileName) => TypeFor(fileName) is not null;

        /// <summary>
        /// True if <paramref name="fileName"/> is a safe bare file name (no directory parts,
        /// no traversal, no rooted/UNC path) with an allow-listed extension.
        /// Use this to <b>reject</b> bad input explicitly rather than silently rewriting it.
        /// </summary>
        public bool IsValidFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (fileName.Length > 255)
            {
                return false;
            }

            if (fileName.Contains('/', StringComparison.Ordinal)
                || fileName.Contains('\\', StringComparison.Ordinal)
                || fileName.Contains("..", StringComparison.Ordinal)
                || fileName.Contains(':', StringComparison.Ordinal)
                || Path.IsPathRooted(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            // Reject names that normalise to something other than themselves.
            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            {
                return false;
            }

            return IsExtensionAllowed(fileName);
        }

        /// <summary>
        /// Validates a candidate directory path without persisting it.
        /// </summary>
        public PathValidationResult ValidateDirectory(string? path)
        {
            var result = new PathValidationResult();

            if (string.IsNullOrWhiteSpace(path))
            {
                result.Message = "La ruta está vacía.";
                return result;
            }

            var trimmed = path.Trim();

            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                result.Message = "Las rutas de red (UNC) no están permitidas.";
                return result;
            }

            string full;
            try
            {
                full = Path.GetFullPath(trimmed);
            }
            catch (Exception ex)
            {
                result.Message = "La ruta no es válida: " + ex.Message;
                return result;
            }

            if (!Path.IsPathRooted(full))
            {
                result.Message = "La ruta debe ser absoluta.";
                return result;
            }

            var real = TryGetRealPath(full) ?? full;

            foreach (var forbidden in _forbidden)
            {
                if (IsInside(real, forbidden) || IsInside(full, forbidden))
                {
                    result.Message = "La ruta apunta a un directorio del sistema y no está permitida.";
                    return result;
                }
            }

            if (!Directory.Exists(full))
            {
                result.Message = "La ruta configurada no existe.";
                return result;
            }

            result.Exists = true;

            try
            {
                var count = 0;
                var compatible = 0;
                foreach (var file in Directory.EnumerateFiles(full))
                {
                    if (++count > MaxScanEntries)
                    {
                        _logger.LogWarning(
                            "[StartupAds] Ads directory has more than {Max} files; only the first {Max} are considered.",
                            MaxScanEntries,
                            MaxScanEntries);
                        break;
                    }

                    if (IsExtensionAllowed(file))
                    {
                        compatible++;
                    }
                }

                result.Readable = true;
                result.CompatibleFileCount = compatible;
            }
            catch (UnauthorizedAccessException)
            {
                result.Message = "Jellyfin no tiene permisos de lectura sobre la ruta.";
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "No se pudo leer la ruta: " + ex.Message;
                return result;
            }

            result.Ok = true;
            result.Message = result.CompatibleFileCount > 0
                ? $"Ruta válida. {result.CompatibleFileCount} archivo(s) compatible(s)."
                : "Ruta válida, pero no contiene archivos de imagen o vídeo compatibles.";
            return result;
        }

        /// <summary>
        /// Lists compatible media files that pass both extension and content-signature checks.
        /// </summary>
        public IReadOnlyList<MediaFileInfo> ListFiles(string? configuredDirectory)
        {
            var validated = ValidateDirectory(configuredDirectory);
            if (!validated.Exists || !validated.Readable)
            {
                return Array.Empty<MediaFileInfo>();
            }

            var root = Path.GetFullPath(configuredDirectory!.Trim());
            var list = new List<MediaFileInfo>();
            var count = 0;

            foreach (var file in Directory.EnumerateFiles(root))
            {
                if (++count > MaxScanEntries)
                {
                    break;
                }

                var name = Path.GetFileName(file);
                var type = TypeFor(name);
                if (type is null)
                {
                    continue;
                }

                // Content-signature check: reject a file that merely has an allowed extension.
                if (!HasValidSignature(file, type.Value))
                {
                    _logger.LogWarning("[StartupAds] Skipping {File}: content does not match its extension.", name);
                    continue;
                }

                long size = 0;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    // ignore, keep 0
                }

                list.Add(new MediaFileInfo
                {
                    FileName = name,
                    FullPath = file,
                    Type = type.Value,
                    SizeBytes = size,
                    ContentType = ContentTypeFor(name)
                });
            }

            return list.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Safely resolves a single file name to an absolute path guaranteed to live directly
        /// inside the configured directory, with symlink targets canonicalised. Returns null on
        /// any invalid input, traversal, symlink escape, signature mismatch or missing file.
        /// </summary>
        public string? ResolveFile(string? configuredDirectory, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory))
            {
                return null;
            }

            if (!IsValidFileName(fileName))
            {
                _logger.LogWarning("[StartupAds] Rejected ad file name: {File}", fileName);
                return null;
            }

            var root = Path.GetFullPath(configuredDirectory.Trim());
            var candidate = Path.GetFullPath(Path.Combine(root, fileName!));

            if (!IsInside(candidate, root))
            {
                _logger.LogWarning("[StartupAds] Rejected ad file outside configured directory: {File}", fileName);
                return null;
            }

            if (!File.Exists(candidate))
            {
                return null;
            }

            // Canonicalise: follow symlinks and verify the *real* target is still inside the
            // *real* configured directory.
            var realRoot = TryGetRealPath(root) ?? root;
            var realFile = TryGetRealPath(candidate) ?? candidate;

            if (!IsInside(realFile, realRoot))
            {
                _logger.LogWarning(
                    "[StartupAds] Rejected symlinked ad file escaping the ads directory: {File}", fileName);
                return null;
            }

            var type = TypeFor(fileName!);
            if (type is null || !HasValidSignature(candidate, type.Value))
            {
                _logger.LogWarning("[StartupAds] Rejected ad file with mismatching content: {File}", fileName);
                return null;
            }

            return candidate;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// True when <paramref name="path"/> is the same folder as, or lives inside,
        /// <paramref name="ancestor"/>. Case-insensitive on Windows. Both are normalised first.
        /// </summary>
        public static bool PathIsInside(string? path, string? ancestor)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(ancestor))
            {
                return false;
            }

            try
            {
                return IsInside(Path.GetFullPath(path.Trim()), Path.GetFullPath(ancestor.Trim()));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>True when <paramref name="path"/> equals or is contained by <paramref name="ancestor"/>.</summary>
        private static bool IsInside(string path, string ancestor)
        {
            var cmp = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            ancestor = ancestor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (path.Equals(ancestor, cmp))
            {
                return true;
            }

            return path.StartsWith(ancestor + Path.DirectorySeparatorChar, cmp);
        }

        /// <summary>
        /// Returns the fully canonical path with every symlink component resolved to its final
        /// target, or null if the path does not exist / cannot be resolved.
        /// </summary>
        private static string? TryGetRealPath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    var target = di.ResolveLinkTarget(true);
                    return Path.GetFullPath(target?.FullName ?? di.FullName);
                }

                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    var target = fi.ResolveLinkTarget(true);
                    if (target is null)
                    {
                        // Not a link itself, but a parent directory might be.
                        var dir = TryGetRealPath(fi.DirectoryName ?? string.Empty);
                        return dir is null ? Path.GetFullPath(fi.FullName) : Path.Combine(dir, fi.Name);
                    }

                    return Path.GetFullPath(target.FullName);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Cheap magic-number check so a renamed executable / script with a media extension is rejected.
        /// </summary>
        private static bool HasValidSignature(string path, AdvertisementType type)
        {
            byte[] head;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 12)
                {
                    return false;
                }

                head = new byte[16];
                _ = fs.Read(head, 0, head.Length);
            }
            catch
            {
                return false;
            }

            bool StartsWith(params byte[] sig)
            {
                if (head.Length < sig.Length)
                {
                    return false;
                }

                for (var i = 0; i < sig.Length; i++)
                {
                    if (head[i] != sig[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            bool HasAtomAt4(string atom)
            {
                var b = System.Text.Encoding.ASCII.GetBytes(atom);
                for (var i = 0; i < b.Length; i++)
                {
                    if (head[4 + i] != b[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (type == AdvertisementType.Image)
            {
                return ext switch
                {
                    ".jpg" or ".jpeg" => StartsWith(0xFF, 0xD8, 0xFF),
                    ".png" => StartsWith(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
                    ".gif" => StartsWith(0x47, 0x49, 0x46, 0x38),
                    ".bmp" => StartsWith(0x42, 0x4D),
                    ".webp" => StartsWith(0x52, 0x49, 0x46, 0x46)
                               && head[8] == (byte)'W' && head[9] == (byte)'E'
                               && head[10] == (byte)'B' && head[11] == (byte)'P',
                    ".avif" => HasAtomAt4("ftyp"),
                    _ => false
                };
            }

            // Video
            return ext switch
            {
                ".mp4" or ".m4v" or ".mov" => HasAtomAt4("ftyp") || HasAtomAt4("moov") || HasAtomAt4("mdat") || HasAtomAt4("free") || HasAtomAt4("wide"),
                ".webm" => StartsWith(0x1A, 0x45, 0xDF, 0xA3),
                ".ogv" or ".ogg" => StartsWith(0x4F, 0x67, 0x67, 0x53),
                _ => false
            };
        }
    }
}
