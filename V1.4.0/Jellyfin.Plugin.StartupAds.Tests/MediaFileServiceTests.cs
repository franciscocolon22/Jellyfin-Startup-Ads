using System;
using System.IO;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.StartupAds.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    public class MediaFileServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _outside;
        private readonly MediaFileService _svc;

        public MediaFileServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sa-tests-" + Guid.NewGuid().ToString("N"));
            _outside = Path.Combine(Path.GetTempPath(), "sa-outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Directory.CreateDirectory(_outside);

            TestFiles.WriteJpeg(Path.Combine(_dir, "ok.jpg"));
            TestFiles.WriteMp4(Path.Combine(_dir, "promo.mp4"));
            TestFiles.WriteGarbage(Path.Combine(_dir, "notes.txt"));
            TestFiles.WriteGarbage(Path.Combine(_dir, "fake.png")); // extension ok, content not
            TestFiles.WritePng(Path.Combine(_outside, "secret.png"));

            _svc = new MediaFileService(NullLogger<MediaFileService>.Instance);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* ignore */ }
            try { Directory.Delete(_outside, true); } catch { /* ignore */ }
        }

        [Fact]
        public void ListFiles_ReturnsOnlyCompatibleMediaWithValidSignature()
        {
            var files = _svc.ListFiles(_dir);
            Assert.Equal(2, files.Count);                        // ok.jpg + promo.mp4
            Assert.DoesNotContain(files, f => f.FileName == "notes.txt");
            Assert.DoesNotContain(files, f => f.FileName == "fake.png");
        }

        [Theory]
        [InlineData("../secret.png")]
        [InlineData("..\\secret.png")]
        [InlineData("/etc/passwd")]
        [InlineData("sub/dir/x.jpg")]
        [InlineData("ok.txt")]
        [InlineData("C:\\Windows\\win.ini")]
        [InlineData("\\\\server\\share\\x.jpg")]
        [InlineData("")]
        [InlineData(null)]
        public void IsValidFileName_RejectsBadInput(string? name)
        {
            Assert.False(_svc.IsValidFileName(name));
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
        public void ResolveFile_RejectsExtensionOnlyMatch()
        {
            Assert.Null(_svc.ResolveFile(_dir, "fake.png"));
        }

        [Fact]
        public void ResolveFile_RejectsSymlinkEscapingTheDirectory()
        {
            var link = Path.Combine(_dir, "escape.png");
            try
            {
                File.CreateSymbolicLink(link, Path.Combine(_outside, "secret.png"));
            }
            catch (Exception)
            {
                // Creating symlinks may require privileges (Windows without dev mode). Skip.
                return;
            }

            Assert.Null(_svc.ResolveFile(_dir, "escape.png"));
        }

        [Fact]
        public void ValidateDirectory_RejectsSystemPaths()
        {
            var sys = OperatingSystem.IsWindows() ? @"C:\Windows" : "/etc";
            Assert.False(_svc.ValidateDirectory(sys).Ok);
        }

        [Fact]
        public void ValidateDirectory_RejectsUncPaths()
        {
            Assert.False(_svc.ValidateDirectory(@"\\server\share\ads").Ok);
        }

        [Fact]
        public void ValidateDirectory_RejectsRelativePaths()
        {
            var result = _svc.ValidateDirectory("relative/ads");
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
            Assert.True(result.Exists);
            Assert.True(result.Readable);
        }

        [Fact]
        public void ResolveFile_ReturnsNullForMissingFile()
        {
            Assert.Null(_svc.ResolveFile(_dir, "missing.jpg"));
        }
    }
}
