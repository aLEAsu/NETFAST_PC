using System.Net;
using System.Net.Sockets;
using NetPulse.Domain.Network;
using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Exercises the real HTTP request/response logic against a local, throwaway HttpListener -
/// deliberately never the real speed.cloudflare.com, per the project's constraint that no
/// automated session may generate load against the real internet. The real endpoint's
/// request/response shape was instead verified once, manually, outside this codebase (a
/// single ~1-2 KB request each way via curl, not a load test) before this class was
/// written - see its own remarks and 06-continuation-brief.md's Module E notes.
/// </summary>
public sealed class CloudflareLoadGenerationServiceTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _downloadUrl;
    private readonly string _uploadUrl;
    private int _downloadRequestCount;
    private int _uploadRequestCount;
    private long _lastUploadBytes;

    public CloudflareLoadGenerationServiceTests()
    {
        int port = GetFreeLoopbackPort();
        var baseUrl = $"http://127.0.0.1:{port}/";
        _downloadUrl = baseUrl + "down";
        _uploadUrl = baseUrl + "up";

        _listener = new HttpListener();
        _listener.Prefixes.Add(baseUrl);
        _listener.Start();
        _ = Task.Run(ServeRequestsAsync);
    }

    private static int GetFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeRequestsAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return; // Stopped/disposed between loop iterations - normal shutdown.
            }

            if (context.Request.Url!.AbsolutePath.EndsWith("down", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _downloadRequestCount);
                var payload = new byte[64 * 1024];
                context.Response.ContentType = "application/octet-stream";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
            }
            else
            {
                Interlocked.Increment(ref _uploadRequestCount);
                using var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);
                Interlocked.Exchange(ref _lastUploadBytes, buffer.Length);
            }

            context.Response.Close();
        }
    }

    [Fact]
    public async Task GenerateLoadAsync_Download_MakesAtLeastOneRequestWithinTheDuration()
    {
        var service = new CloudflareLoadGenerationService(downloadUrl: _downloadUrl, uploadUrl: _uploadUrl);

        await service.GenerateLoadAsync(LoadDirection.Download, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.True(_downloadRequestCount > 0, "Expected at least one download request against the local test server.");
    }

    [Fact]
    public async Task GenerateLoadAsync_Upload_SendsARealBodyToTheConfiguredUrl()
    {
        var service = new CloudflareLoadGenerationService(downloadUrl: _downloadUrl, uploadUrl: _uploadUrl);

        await service.GenerateLoadAsync(LoadDirection.Upload, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.True(_uploadRequestCount > 0, "Expected at least one upload request against the local test server.");
        Assert.True(_lastUploadBytes > 0, "Expected the uploaded body to contain real bytes.");
    }

    [Fact]
    public async Task GenerateLoadAsync_NeverRunsNoticeablyLongerThanTheRequestedDuration()
    {
        var service = new CloudflareLoadGenerationService(downloadUrl: _downloadUrl, uploadUrl: _uploadUrl);
        var requested = TimeSpan.FromMilliseconds(300);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await service.GenerateLoadAsync(LoadDirection.Download, requested, CancellationToken.None);

        stopwatch.Stop();
        // Generous tolerance - this asserts "bounded", not "exact to the millisecond": a
        // single in-flight chunk can finish slightly after the duration cap fires.
        Assert.True(stopwatch.Elapsed < requested + TimeSpan.FromSeconds(5),
            $"Expected roughly {requested.TotalMilliseconds} ms, took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task GenerateLoadAsync_ZeroOrNegativeDuration_Throws()
    {
        var service = new CloudflareLoadGenerationService(downloadUrl: _downloadUrl, uploadUrl: _uploadUrl);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GenerateLoadAsync(LoadDirection.Download, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateLoadAsync_CallerAlreadyCancelled_ThrowsOperationCanceled()
    {
        var service = new CloudflareLoadGenerationService(downloadUrl: _downloadUrl, uploadUrl: _uploadUrl);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GenerateLoadAsync(LoadDirection.Download, TimeSpan.FromSeconds(5), cts.Token));
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
