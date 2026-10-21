using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.Updater.Services;

namespace WallpaperTurbo.Tests;

public class HttpDownloadManagerResumeTests
{
    private static readonly byte[] FullContent = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

    private static UpdateManifest MakeManifest(long fileSizeBytes)
    {
        return new UpdateManifest(
            Version: new SemanticVersion(2, 0, 0),
            Channel: ReleaseChannel.Stable,
            ReleaseNotes: "test fixture",
            DownloadUrl: "https://example.invalid/setup.exe",
            Sha256Hash: "unused-in-these-tests",
            FileSizeBytes: fileSizeBytes,
            MinSupportedVersion: new SemanticVersion(1, 0, 0),
            IsRollbackEligible: false,
            MinSignatureRequirement: SignatureRequirement.Sha256Only);
    }

    private static string CreateTempDestination()
    {
        var dir = Path.Combine(Path.GetTempPath(), "WallpaperTurboTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "update.exe");
    }

    [Fact]
    public async Task Download_WhenNoFileExists_DownloadsFullContent()
    {
        var destination = CreateTempDestination();
        using var handler = new RangeAwareHandler(FullContent, supportsRange: true);
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        var result = await manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination);

        Assert.Equal(destination, result);
        Assert.Equal(FullContent, File.ReadAllBytes(destination));
        Assert.Null(handler.LastRangeFrom);
    }

    [Fact]
    public async Task Download_WhenPartialFileExists_SendsRangeAndAppendsRemainder()
    {
        var destination = CreateTempDestination();
        var existingBytes = FullContent.Take(20).ToArray();
        File.WriteAllBytes(destination, existingBytes);

        using var handler = new RangeAwareHandler(FullContent, supportsRange: true);
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        var result = await manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination);

        Assert.Equal(destination, result);
        Assert.Equal(20, handler.LastRangeFrom);
        Assert.Equal(FullContent, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Download_WhenServerIgnoresRange_RestartsFromScratch()
    {
        var destination = CreateTempDestination();
        File.WriteAllBytes(destination, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        using var handler = new RangeAwareHandler(FullContent, supportsRange: false);
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        var result = await manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination);

        Assert.Equal(destination, result);
        Assert.Equal(FullContent, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Download_WhenServerRejectsRange_RestartsFromScratch()
    {
        var destination = CreateTempDestination();
        File.WriteAllBytes(destination, FullContent.Take(60).ToArray());

        using var handler = new RangeRejectingHandler(FullContent);
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        var result = await manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination);

        Assert.Equal(destination, result);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(FullContent, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Download_WhenFileAlreadyComplete_SkipsNetwork()
    {
        var destination = CreateTempDestination();
        File.WriteAllBytes(destination, FullContent);

        using var handler = new RangeAwareHandler(FullContent, supportsRange: true);
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        var result = await manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination);

        Assert.Equal(destination, result);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(FullContent, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Download_WhenCancelledMidTransfer_KeepsPartialFile()
    {
        var destination = CreateTempDestination();
        var partial = FullContent.Take(8).ToArray();

        using var handler = new StreamHandler(() => new PartialThenThrowStream(partial, new TaskCanceledException("cancelled")));
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination));

        Assert.True(File.Exists(destination), "Cancelled download must keep the partially downloaded file so it can resume.");
        Assert.Equal(partial, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Download_WhenAllRetriesFail_KeepsExistingPartialFile()
    {
        var destination = CreateTempDestination();
        var partial = FullContent.Take(10).ToArray();
        File.WriteAllBytes(destination, partial);

        using var handler = new FailingHandler();
        using var client = new HttpClient(handler);
        using var manager = new HttpDownloadManager(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.DownloadUpdateAsync(MakeManifest(FullContent.Length), destination));

        Assert.True(File.Exists(destination), "Failed download must keep the partially downloaded file so it can resume.");
        Assert.Equal(partial, File.ReadAllBytes(destination));
    }

    // -----------------------------------------------------------------------
    // Test fakes
    // -----------------------------------------------------------------------
    private sealed class RangeAwareHandler : HttpMessageHandler
    {
        private readonly byte[] _fullContent;
        private readonly bool _supportsRange;

        public int RequestCount { get; private set; }
        public long? LastRangeFrom { get; private set; }

        public RangeAwareHandler(byte[] fullContent, bool supportsRange)
        {
            _fullContent = fullContent;
            _supportsRange = supportsRange;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            LastRangeFrom = range?.From;

            long from = _supportsRange ? range?.From ?? 0 : 0;
            var slice = _fullContent.Skip((int)from).ToArray();
            var statusCode = from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK;

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(slice)
            };
            response.Content.Headers.ContentLength = slice.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class RangeRejectingHandler : HttpMessageHandler
    {
        private readonly byte[] _fullContent;

        public int RequestCount { get; private set; }

        public RangeRejectingHandler(byte[] fullContent) => _fullContent = fullContent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            // The stored asset is shorter than the stale partial on disk:
            // any ranged request is past the end of the content.
            if (request.Headers.Range != null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_fullContent)
            };
            response.Content.Headers.ContentLength = _fullContent.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class StreamHandler : HttpMessageHandler
    {
        private readonly Func<Stream> _streamFactory;

        public StreamHandler(Func<Stream> streamFactory) => _streamFactory = streamFactory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(_streamFactory())
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("simulated network failure");
        }
    }

    private sealed class PartialThenThrowStream : Stream
    {
        private readonly byte[] _payload;
        private readonly Exception _exceptionToThrow;
        private bool _payloadDelivered;

        public PartialThenThrowStream(byte[] payload, Exception exceptionToThrow)
        {
            _payload = payload;
            _exceptionToThrow = exceptionToThrow;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_payloadDelivered)
            {
                _payloadDelivered = true;
                var toCopy = Math.Min(_payload.Length, count);
                Array.Copy(_payload, 0, buffer, offset, toCopy);
                return toCopy;
            }
            throw _exceptionToThrow;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
