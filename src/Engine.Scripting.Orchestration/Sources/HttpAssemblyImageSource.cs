using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Orchestration.Sources;

/// <summary>
/// <see cref="IScriptAssemblyImageSource"/> over HTTP(S): downloads a precompiled script
/// assembly (<c>.dll</c> + optional <c>.pdb</c>), verifies its SHA-256, keeps a local
/// offline-first cache, and detects new versions with conditional polling
/// (<c>If-None-Match</c>/ETag — unchanged versions cost a 304 with no body).
/// </summary>
/// <remarks>
/// <para>
/// This is the distribution piece of the "compile once, distribute the binary" flow for devices
/// and remote hosts (e.g. MAUI apps pulling business-rule scripts from a server): the publisher
/// uploads <c>scripts.dll</c> (+ <c>.pdb</c> and a checksum manifest) and every running host
/// hot-swaps on its next poll.
/// </para>
/// <para>
/// Failure semantics: transient network errors serve the last in-memory or on-disk copy (with a
/// warning) — a device that booted offline starts from cache. Integrity failures
/// (<see cref="ScriptImageIntegrityException"/>) are never masked by fallbacks and never touch
/// the running generation.
/// </para>
/// </remarks>
public sealed class HttpAssemblyImageSource : IScriptAssemblyImageSource
{
    private readonly HttpAssemblyImageSourceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<HttpAssemblyImageSource> _logger;
    private readonly string _assemblyName;
    private readonly Uri _symbolsUrl;
    private readonly Lock _gate = new();
    private ScriptAssemblyImage? _latest;
    private string? _etag;
    private CancellationTokenSource? _watchCts;
    private Task? _watchLoop;

    /// <summary>Creates the source.</summary>
    /// <param name="options">Source configuration.</param>
    /// <param name="httpClient">
    /// Optional client (e.g. from <c>IHttpClientFactory</c>); when omitted, the source creates
    /// and owns one.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <exception cref="ArgumentException">The options are inconsistent (see remarks on each option).</exception>
    public HttpAssemblyImageSource(
        HttpAssemblyImageSourceOptions options,
        HttpClient? httpClient = null,
        ILogger<HttpAssemblyImageSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        _options = options;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _logger = logger ?? NullLogger<HttpAssemblyImageSource>.Instance;

        var nameFromUrl = Path.GetFileNameWithoutExtension(options.ImageUrl.AbsolutePath);
        _assemblyName = string.IsNullOrEmpty(nameFromUrl) ? "RemoteScriptAssembly" : nameFromUrl;
        _symbolsUrl = options.SymbolsUrl ?? DeriveSymbolsUrl(options.ImageUrl);
    }

    private enum FetchStatus
    {
        Downloaded,
        NotModified,
        NotFound,
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    /// <remarks>
    /// Always revalidates against the server with a conditional request (a 304 costs no body),
    /// so a manual <c>ReloadAsync</c> observes the latest published version even with watching
    /// disabled. On transient network failure the last in-memory copy, then the on-disk cache,
    /// is served; with neither available the network error propagates (and the reload pipeline
    /// keeps the current generation).
    /// </remarks>
    public async Task<ScriptAssemblyImage?> LoadImageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await FetchAsync(cancellationToken).ConfigureAwait(false);
            if (status == FetchStatus.NotFound)
            {
                return null;
            }

            lock (_gate)
            {
                return _latest;
            }
        }
        catch (Exception exception) when (IsTransientNetworkError(exception, cancellationToken))
        {
            ScriptAssemblyImage? inMemory;
            lock (_gate)
            {
                inMemory = _latest;
            }

            if (inMemory is not null)
            {
                Log.ImageServedFromFallback(_logger, "the last in-memory", exception);
                return inMemory;
            }

            var cached = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                Log.ImageServedFromFallback(_logger, "the locally cached", exception);
                lock (_gate)
                {
                    _latest ??= cached;
                    return _latest;
                }
            }

            throw;
        }
    }

    /// <inheritdoc />
    public Task StartWatchingAsync(CancellationToken cancellationToken)
    {
        if (_watchLoop is not null)
        {
            return Task.CompletedTask;
        }

        _watchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _watchLoop = WatchLoopAsync(_watchCts.Token);
        Log.SourceWatchingStarted(_logger, $"http image: {_options.ImageUrl.GetLeftPart(UriPartial.Path)} (poll: {_options.PollInterval})");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopWatchingAsync(CancellationToken cancellationToken)
    {
        if (_watchCts is not null)
        {
            await _watchCts.CancelAsync().ConfigureAwait(false);
        }

        if (_watchLoop is not null)
        {
            try
            {
                await _watchLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _watchCts?.Dispose();
        _watchCts = null;
        _watchLoop = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopWatchingAsync(CancellationToken.None).ConfigureAwait(false);
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static void ValidateOptions(HttpAssemblyImageSourceOptions options)
    {
        if (!options.ImageUrl.IsAbsoluteUri
            || (options.ImageUrl.Scheme != Uri.UriSchemeHttp && options.ImageUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("ImageUrl must be an absolute http(s) URL.", nameof(options));
        }

        if (options.ExpectedSha256 is not null && options.ChecksumUrl is not null)
        {
            throw new ArgumentException("Configure ExpectedSha256 or ChecksumUrl, not both.", nameof(options));
        }

        if (options.ExpectedSha256 is not null && !IsSha256Hex(options.ExpectedSha256))
        {
            throw new ArgumentException("ExpectedSha256 must be 64 hexadecimal digits.", nameof(options));
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("PollInterval must be positive.", nameof(options));
        }
    }

    private static Uri DeriveSymbolsUrl(Uri imageUrl)
    {
        var builder = new UriBuilder(imageUrl);
        builder.Path = Path.ChangeExtension(builder.Path, ".pdb");
        return builder.Uri;
    }

    private static bool IsTransientNetworkError(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static bool IsSha256Hex(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private async Task WatchLoopAsync(CancellationToken cancellationToken)
    {
        // The orchestrator's StartAsync already performed the initial LoadImageAsync, so the
        // first poll waits a full interval instead of hammering the server immediately.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var status = await FetchAsync(cancellationToken).ConfigureAwait(false);
                if (status == FetchStatus.Downloaded)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ScriptImageIntegrityException)
            {
                // Already logged with the hashes; keep polling — the publisher may fix the artifact.
            }
            catch (Exception exception)
            {
                Log.ImagePollFailed(_logger, exception);
            }
        }
    }

    /// <summary>
    /// Conditional fetch: sends <c>If-None-Match</c> when an ETag is known, verifies integrity,
    /// and atomically publishes the new image to memory and to the local cache.
    /// </summary>
    private async Task<FetchStatus> FetchAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ImageUrl);

        string? knownETag;
        lock (_gate)
        {
            knownETag = _etag;
        }

        if (knownETag is not null && EntityTagHeaderValue.TryParse(knownETag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            Log.ImageNotModified(_logger, knownETag);
            return FetchStatus.NotModified;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return FetchStatus.NotFound;
        }

        response.EnsureSuccessStatusCode();

        var peBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        await VerifyIntegrityAsync(peBytes, cancellationToken).ConfigureAwait(false);

        var pdbBytes = _options.DownloadSymbols
            ? await TryDownloadSymbolsAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var image = new ScriptAssemblyImage(_assemblyName, peBytes, pdbBytes);
        var newETag = response.Headers.ETag?.ToString();

        lock (_gate)
        {
            _latest = image;
            _etag = newETag;
        }

        await SaveToCacheAsync(image, newETag, cancellationToken).ConfigureAwait(false);
        Log.ImageDownloaded(_logger, _assemblyName, peBytes.Length, pdbBytes is not null, newETag);
        return FetchStatus.Downloaded;
    }

    private async Task VerifyIntegrityAsync(byte[] peBytes, CancellationToken cancellationToken)
    {
        var expected = _options.ExpectedSha256;
        if (expected is null && _options.ChecksumUrl is not null)
        {
            var manifest = await _httpClient.GetStringAsync(_options.ChecksumUrl, cancellationToken).ConfigureAwait(false);
            expected = ParseChecksumManifest(manifest);
        }

        if (expected is null)
        {
            return;
        }

        var actual = Convert.ToHexString(SHA256.HashData(peBytes));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            Log.ChecksumMismatch(_logger, expected[..8], actual[..8]);
            throw new ScriptImageIntegrityException(
                $"SHA-256 mismatch for '{_assemblyName}': expected {expected[..8]}…, computed {actual[..8]}….");
        }
    }

    private static string ParseChecksumManifest(string manifest)
    {
        foreach (var token in manifest.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsSha256Hex(token))
            {
                return token;
            }
        }

        throw new ScriptImageIntegrityException("The checksum manifest contains no SHA-256 (64 hex digits) token.");
    }

    private async Task<byte[]?> TryDownloadSymbolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .SendAsync(new HttpRequestMessage(HttpMethod.Get, _symbolsUrl), cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransientNetworkError(exception, cancellationToken))
        {
            Log.SymbolsDownloadFailed(_logger, exception);
            return null;
        }
    }

    private async Task SaveToCacheAsync(ScriptAssemblyImage image, string? etag, CancellationToken cancellationToken)
    {
        if (_options.CacheDirectory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_options.CacheDirectory);

            // Stale artifacts (possibly under a different assembly name) must not survive.
            foreach (var pattern in (string[])["*.dll", "*.pdb", "*.etag"])
            {
                foreach (var stale in Directory.EnumerateFiles(_options.CacheDirectory, pattern))
                {
                    File.Delete(stale);
                }
            }

            var dllPath = Path.Combine(_options.CacheDirectory, image.AssemblyName + ".dll");
            await WriteAtomicAsync(dllPath, image.PeBytes, cancellationToken).ConfigureAwait(false);

            if (image.PdbBytes is not null)
            {
                await WriteAtomicAsync(Path.ChangeExtension(dllPath, ".pdb"), image.PdbBytes, cancellationToken).ConfigureAwait(false);
            }

            if (etag is not null)
            {
                await WriteAtomicAsync(dllPath + ".etag", System.Text.Encoding.UTF8.GetBytes(etag), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.CacheWriteFailed(_logger, _options.CacheDirectory, exception);
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private async Task<ScriptAssemblyImage?> TryLoadFromCacheAsync(CancellationToken cancellationToken)
    {
        if (_options.CacheDirectory is null || !Directory.Exists(_options.CacheDirectory))
        {
            return null;
        }

        var dllPath = Directory.EnumerateFiles(_options.CacheDirectory, "*.dll").FirstOrDefault();
        if (dllPath is null)
        {
            return null;
        }

        var image = await ScriptAssemblyImage.ReadFromFileAsync(dllPath, cancellationToken).ConfigureAwait(false);

        var etagPath = dllPath + ".etag";
        if (File.Exists(etagPath))
        {
            var cachedETag = await File.ReadAllTextAsync(etagPath, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _etag ??= cachedETag;
            }
        }

        return image;
    }
}
