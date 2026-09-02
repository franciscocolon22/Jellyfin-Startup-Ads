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
    /// Resolves, validates and enumerates the configured ads directory. All file access
    /// performed by the plugin goes through this service so that path traversal is impossible.
    /// </summary>
    public class MediaFileService
    {
        private static readonly HashSet<string> _imageExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif"
        };

        private static readonly HashSet<string> _videoExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".m4v", ".mov", ".ogv", ".ogg"
        };

        // Directories that must never be used as an ad source, even if readable.
        private static readonly string[] _forbidden = OperatingSystem.IsWindows()
            ? new[] { @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)" }
            : new[] { "/etc", "/proc", "/sys", "/dev", "/boot", "/root", "/bin", "/sbin", "/usr/bin", "/usr/sbin", "/var/log" };

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

            string full;
            try
            {
                full = Path.GetFullPath(path.Trim());
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

            foreach (var forbidden in _forbidden)
            {
                if (full.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(forbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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
                var files = Directory.EnumerateFiles(full).Take(2000).ToList();
                result.Readable = true;
                result.CompatibleFileCount = files.Count(f => IsExtensionAllowed(f));
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
        /// Lists compatible media files inside the configured directory.
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

            foreach (var file in Directory.EnumerateFiles(root))
            {
                var name = Path.GetFileName(file);
                var type = TypeFor(name);
                if (type is null)
                {
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
        /// inside the configured directory. Returns null on any traversal / mismatch / bad extension.
        /// </summary>
        public string? ResolveFile(string? configuredDirectory, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory) || string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            // Reject anything that is not a bare file name.
            if (fileName.Contains('/', StringComparison.Ordinal)
                || fileName.Contains('\\', StringComparison.Ordinal)
                || fileName.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(fileName))
            {
                _logger.LogWarning("Rejected ad file name with path characters: {File}", fileName);
                return null;
            }

            if (!IsExtensionAllowed(fileName))
            {
                return null;
            }

            var root = Path.GetFullPath(configuredDirectory.Trim());
            var candidate = Path.GetFullPath(Path.Combine(root, fileName));

            var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSep, StringComparison.Ordinal))
            {
                _logger.LogWarning("Rejected ad file outside configured directory: {File}", fileName);
                return null;
            }

            return File.Exists(candidate) ? candidate : null;
        }
    }
}
