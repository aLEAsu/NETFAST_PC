using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Generates real load against Cloudflare's public speed-test endpoints
/// (speed.cloudflare.com/__down and /__up) - chosen deliberately over a self-hosted endpoint
/// per the project's decision to use a well-known, purpose-built, no-auth-required public
/// service rather than ask the user to stand up their own server. Verified manually against
/// the real endpoint before writing this class (a single ~1-2 KB request each way, not a
/// load test) to confirm the request/response shape rather than guessing it - see
/// 06-continuation-brief.md's Module E notes.
///
/// The base URLs are constructor parameters specifically so tests can point this at a local
/// loopback listener instead of the real internet - per the project's constraint that no
/// automated session may generate load against the real internet, the test suite for this
/// class never contacts Cloudflare.
/// </summary>
public sealed class CloudflareLoadGenerationService : ILoadGenerationService
{
    private const int DownloadChunkBytes = 4 * 1024 * 1024; // 4 MB per request
    private const int UploadChunkBytes = 1 * 1024 * 1024; // 1 MB per request
    private const int StreamCopyBufferSize = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly string _downloadUrl;
    private readonly string _uploadUrl;

    public CloudflareLoadGenerationService(
        HttpClient? httpClient = null,
        string? downloadUrl = null,
        string? uploadUrl = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _downloadUrl = downloadUrl ?? $"https://speed.cloudflare.com/__down?bytes={DownloadChunkBytes}";
        _uploadUrl = uploadUrl ?? "https://speed.cloudflare.com/__up";
    }

    public async Task GenerateLoadAsync(LoadDirection direction, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        // Must be checked before the loop, not only inside it: if the caller's token is
        // already cancelled, the linked duration token starts out already cancelled too, so
        // "while (!durationCts.IsCancellationRequested)" would be false on its very first
        // check and the method would return normally without ever throwing - silently
        // reporting success for a run the caller asked to cancel before it even started.
        cancellationToken.ThrowIfCancellationRequested();

        using var durationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        durationCts.CancelAfter(duration);

        try
        {
            while (!durationCts.IsCancellationRequested)
            {
                if (direction == LoadDirection.Download)
                    await RunOneDownloadChunkAsync(durationCts.Token);
                else
                    await RunOneUploadChunkAsync(durationCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Expected: the duration elapsed - this run finishing on schedule, not a failure.
        }
    }

    // Reads the response body in fixed-size chunks (rather than one unattended
    // CopyToAsync/ReadAsByteArrayAsync) so a cancellation request takes effect within one
    // buffer's worth of data, not only once the whole multi-megabyte chunk has downloaded -
    // the brief's "honor cancellation within one buffer-write cycle" requirement.
    private async Task RunOneDownloadChunkAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[StreamCopyBufferSize];
        int bytesRead;
        do
        {
            bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        }
        while (bytesRead > 0);
    }

    private async Task RunOneUploadChunkAsync(CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(RandomPayload(UploadChunkBytes));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await _httpClient.PostAsync(_uploadUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static byte[] RandomPayload(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
