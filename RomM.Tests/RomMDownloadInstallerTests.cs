using Playnite.SDK.Plugins;
using RomM.Downloads;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RomM.Tests
{
    public class RomMDownloadInstallerTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "romm-download-test-" + Guid.NewGuid());

        public RomMDownloadInstallerTests() { Directory.CreateDirectory(root); }
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        private DownloadRequest Request() => new DownloadRequest
        {
            GameId = Guid.NewGuid(), GameName = "Test game", DownloadUrl = "https://romm.example/game",
            InstallDir = root, GamePath = Path.Combine(root, "game.rom"), AutoExtract = false
        };

        private DownloadQueueItem Item(CancellationTokenSource cts = null) => new DownloadQueueItem
        {
            Cts = cts ?? new CancellationTokenSource()
        };

        [Fact]
        public async Task SuccessfulDownloadInstallsOnlyAfterAllBytesArrive()
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }))
            {
                var request = Request();
                GameInstalledEventArgs installed = null;
                request.OnInstalled = args => installed = args;
                await new RomMDownloadInstaller(client).DownloadAndInstall(Item(), request);
                Assert.Equal(payload, File.ReadAllBytes(request.GamePath));
                Assert.NotNull(installed);
                Assert.Equal(root, installed.InstalledInfo.InstallDirectory);
                Assert.False(File.Exists(request.GamePath + ".part"));
            }
        }

        [Fact]
        public async Task PauseKeepsPartialFileAndRetryRestartsWithoutAppending()
        {
            var payload = new byte[1024 * 1024];
            new Random(42).NextBytes(payload);
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }))
            using (var cts = new CancellationTokenSource())
            {
                var request = Request();
                var item = Item(cts);
                var installed = false;
                request.OnInstalled = _ => installed = true;
                item.PropertyChanged += (_, __) => { if (item.ProgressValue > 0) cts.Cancel(); };
                var installer = new RomMDownloadInstaller(client);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.DownloadAndInstall(item, request));
                Assert.False(installed);
                Assert.False(File.Exists(request.GamePath));
                Assert.True(new FileInfo(request.GamePath + ".part").Length > 0);

                await installer.DownloadAndInstall(Item(), request);
                Assert.True(installed);
                Assert.Equal(payload, File.ReadAllBytes(request.GamePath));
            }
        }

        [Fact]
        public async Task CancellationWhileBuildingRomListDoesNotSignalInstalled()
        {
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) }))
            using (var cts = new CancellationTokenSource())
            {
                var request = Request();
                var installed = false;
                request.BuildRoms = () => { cts.Cancel(); return new List<Playnite.SDK.Models.GameRom>(); };
                request.OnInstalled = _ => installed = true;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RomMDownloadInstaller(client).DownloadAndInstall(Item(cts), request));
                Assert.False(installed);
            }
        }

        [Fact]
        public async Task FailedHttpRequestDoesNotSignalInstalled()
        {
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)))
            {
                var request = Request();
                var installed = false;
                request.OnInstalled = _ => installed = true;
                await Assert.ThrowsAsync<HttpRequestException>(() => new RomMDownloadInstaller(client).DownloadAndInstall(Item(), request));
                Assert.False(installed);
                Assert.False(File.Exists(request.GamePath));
            }
        }

        [Fact]
        public async Task TruncatedResponseIsNotMarkedInstalled()
        {
            using (var client = Client(() =>
            {
                var content = new ByteArrayContent(new byte[] { 1 });
                content.Headers.ContentLength = 10;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }))
            {
                var request = Request();
                await Assert.ThrowsAsync<IOException>(() => new RomMDownloadInstaller(client).DownloadAndInstall(Item(), request));
                Assert.False(File.Exists(request.GamePath));
            }
        }

        [Fact]
        public async Task FolderArchiveExtractsBeforeInstalledCallback()
        {
            var zip = Archive("disc.rom");
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }))
            {
                var request = Request();
                request.GamePath = Path.Combine(root, "game.zip");
                request.DownloadAsArchive = true;
                request.BuildRoms = () =>
                {
                    Assert.True(File.Exists(Path.Combine(root, "disc.rom")));
                    return new List<Playnite.SDK.Models.GameRom>();
                };
                await new RomMDownloadInstaller(client).DownloadAndInstall(Item(), request);
                Assert.False(File.Exists(request.GamePath));
            }
        }

        [Fact]
        public async Task ArchiveTraversalIsRejected()
        {
            var zip = Archive("../outside.rom");
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }))
            {
                var request = Request();
                request.DownloadAsArchive = true;
                await Assert.ThrowsAnyAsync<ArgumentException>(() => new RomMDownloadInstaller(client).DownloadAndInstall(Item(), request));
                Assert.False(File.Exists(Path.Combine(root, "..", "outside.rom")));
            }
        }

        [Fact]
        public void CancelFlatInstallPreservesOtherGames()
        {
            var request = Request();
            request.InstallFlat = true;
            var otherGame = Path.Combine(root, "other.rom");
            File.WriteAllText(otherGame, "keep");
            File.WriteAllText(request.GamePath + ".part", "partial");
            using (var client = Client(() => new HttpResponseMessage(HttpStatusCode.OK)))
                new RomMDownloadInstaller(client).TryCleanupPartialInstall(request);
            Assert.True(File.Exists(otherGame));
            Assert.False(File.Exists(request.GamePath + ".part"));
        }

        private static byte[] Archive(string entry)
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("rom");
                return stream.ToArray();
            }
        }

        private static HttpClient Client(Func<HttpResponseMessage> response) => new HttpClient(new Handler(response));
        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> response;
            internal Handler(Func<HttpResponseMessage> response) { this.response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(response());
        }
    }
}
