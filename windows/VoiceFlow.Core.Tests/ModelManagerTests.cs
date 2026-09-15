using System.Net;
using System.Security.Cryptography;
using System.Text;
using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class ModelManagerTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "voiceflow-model-manager-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }

    // A stream that blocks until cancelled, so DownloadAsync's cancellation
    // path can be exercised without any real network I/O or sleeps.
    private sealed class BlockingStream(CancellationToken blockUntil) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, blockUntil);
            var tcs = new TaskCompletionSource<int>();
            await using var registration = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token));
            return await tcs.Task;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Sha256OfFileComputesKnownHash()
    {
        var dir = CreateTempDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("voiceflow-test-fixture\n");
            var filePath = Path.Combine(dir, "fixture.bin");
            File.WriteAllBytes(filePath, bytes);
            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var actual = ModelManager.Sha256OfFile(filePath);

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsModelPresentAndValidIsFalseWhenFileMissing()
    {
        var dir = CreateTempDirectory();
        try
        {
            var manager = new ModelManager(dir);
            var model = new ModelInfo("base", "missing.bin", new Uri("https://example.com/missing.bin"), "does-not-matter");

            Assert.False(manager.IsModelPresentAndValid(model));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsModelPresentAndValidIsFalseWhenChecksumMismatches()
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "corrupt.bin");
            File.WriteAllText(filePath, "not the real model");
            var manager = new ModelManager(dir);
            var model = new ModelInfo("base", "corrupt.bin", new Uri("https://example.com/corrupt.bin"), new string('0', 64));

            Assert.False(manager.IsModelPresentAndValid(model));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsModelPresentAndValidIsTrueWhenChecksumMatches()
    {
        var dir = CreateTempDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("voiceflow-test-fixture\n");
            var filePath = Path.Combine(dir, "fixture.bin");
            File.WriteAllBytes(filePath, bytes);
            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var manager = new ModelManager(dir);
            var model = new ModelInfo("fixture", "fixture.bin", new Uri("https://example.com/fixture.bin"), expected);

            Assert.True(manager.IsModelPresentAndValid(model));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsyncWritesFileAndReportsProgressToOne()
    {
        var dir = CreateTempDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(new string('a', 10_000));
            var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var handler = new FakeHttpMessageHandler((request, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                };
                response.Content.Headers.ContentLength = bytes.Length;
                return Task.FromResult(response);
            });
            var manager = new ModelManager(dir, handler);
            var model = new ModelInfo("fixture", "fixture.bin", new Uri("https://example.com/fixture.bin"), expectedHash);
            var reported = new List<double>();
            var progress = new Progress<double>(value => reported.Add(value));

            await manager.DownloadAsync(model, progress, CancellationToken.None);
            // Progress is reported via the IProgress<T> callback which may be
            // deferred to the SynchronizationContext; give it a chance to drain.
            await Task.Delay(50);

            var destination = manager.LocalPath(model);
            Assert.True(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));
            Assert.Equal(expectedHash, ModelManager.Sha256OfFile(destination));
            Assert.Contains(1.0, reported);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsyncThrowsAndLeavesNoFilesOnChecksumMismatch()
    {
        var dir = CreateTempDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("some bytes that will not match the checksum");
            var handler = new FakeHttpMessageHandler((request, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                };
                response.Content.Headers.ContentLength = bytes.Length;
                return Task.FromResult(response);
            });
            var manager = new ModelManager(dir, handler);
            var model = new ModelInfo("fixture", "fixture.bin", new Uri("https://example.com/fixture.bin"), new string('0', 64));

            await Assert.ThrowsAsync<ChecksumMismatchException>(
                () => manager.DownloadAsync(model, new Progress<double>(), CancellationToken.None));

            var destination = manager.LocalPath(model);
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsyncLeavesNoPartFileOnCancellation()
    {
        var dir = CreateTempDirectory();
        try
        {
            using var cts = new CancellationTokenSource();
            var handler = new FakeHttpMessageHandler((request, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new BlockingStream(cts.Token)),
                };
                response.Content.Headers.ContentLength = 10_000;
                return Task.FromResult(response);
            });
            var manager = new ModelManager(dir, handler);
            var model = new ModelInfo("fixture", "fixture.bin", new Uri("https://example.com/fixture.bin"), new string('0', 64));

            var downloadTask = manager.DownloadAsync(model, new Progress<double>(), cts.Token);
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask);

            var destination = manager.LocalPath(model);
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KnownModelsHasThreeEntriesWithValidChecksums()
    {
        Assert.Equal(3, ModelCatalogue.KnownModels.Count);
        foreach (var model in ModelCatalogue.KnownModels)
        {
            Assert.Equal(64, model.Sha256.Length);
            Assert.True(model.Sha256.All(Uri.IsHexDigit));
        }
    }

    [Theory]
    [InlineData(WhisperModelName.Base)]
    [InlineData(WhisperModelName.Small)]
    [InlineData(WhisperModelName.Medium)]
    public void ForReturnsTheCatalogueEntryForEveryModelName(WhisperModelName name)
    {
        var model = ModelCatalogue.For(name);

        Assert.NotNull(model);
        Assert.NotEmpty(model.FileName);
    }
}
