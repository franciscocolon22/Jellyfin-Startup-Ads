using System;
using System.IO;
using Jellyfin.Plugin.StartupAds.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    public class MediaFileServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly MediaFileService _svc;

        public MediaFileServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sa-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "ok.jpg"), "x");
            File.WriteAllText(Path.Combine(_dir, "promo.mp4"), "x");
            File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
            _svc = new MediaFileService(NullLogger<MediaFileService>.Instance);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* ignore */ }
        }

        [Fact]
        public void ListFiles_ReturnsOnlyCompatibleMedia()
        {
            var files = _svc.ListFiles(_dir);
            Assert.Equal(2, files.Count);
            Assert.DoesNotContain(files, f => f.FileName == "notes.txt");
        }

        [Theory]
        [InlineData("../secret.jpg")]
        [InlineData("..\\secret.jpg")]
        [InlineData("/etc/passwd")]
        [InlineData("sub/dir/x.jpg")]
        [InlineData("ok.txt")]
        public void ResolveFile_RejectsTraversalAndBadExtensions(string name)
        {
            Assert.Null(_svc.ResolveFile(_dir, name));
        }

        [Fact]
        public void ResolveFile_AcceptsPlainFileNameInsideDir()
        {
            var resolved = _svc.ResolveFile(_dir, "ok.jpg");
            Assert.NotNull(resolved);
            Assert.StartsWith(_dir, resolved!);
        }

        [Fact]
        public void ValidateDirectory_RejectsSystemPaths()
        {
            var sys = OperatingSystem.IsWindows() ? @"C:\Windows" : "/etc";
            var result = _svc.ValidateDirectory(sys);
            Assert.False(result.Ok);
        }

        [Fact]
        public void ValidateDirectory_ReportsMissingPath()
        {
            var result = _svc.ValidateDirectory(Path.Combine(_dir, "does-not-exist"));
            Assert.False(result.Ok);
            Assert.False(result.Exists);
        }

        [Fact]
        public void ValidateDirectory_OkForRealFolderWithMedia()
        {
            var result = _svc.ValidateDirectory(_dir);
            Assert.True(result.Ok);
            Assert.Equal(2, result.CompatibleFileCount);
        }
    }
}
