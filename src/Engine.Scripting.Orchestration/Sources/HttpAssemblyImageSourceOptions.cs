namespace Engine.Scripting.Orchestration.Sources;

/// <summary>
/// Configuration of <see cref="HttpAssemblyImageSource"/>: where the precompiled script assembly
/// lives, how its integrity is verified, and how change detection behaves.
/// </summary>
public sealed class HttpAssemblyImageSourceOptions
{
    /// <summary>Absolute HTTP(S) URL of the script assembly (<c>.dll</c>).</summary>
    public required Uri ImageUrl { get; init; }

    /// <summary>
    /// URL of the portable PDB. Defaults to <see cref="ImageUrl"/> with a <c>.pdb</c> extension.
    /// Ignored when <see cref="DownloadSymbols"/> is <see langword="false"/>.
    /// </summary>
    public Uri? SymbolsUrl { get; init; }

    /// <summary>
    /// Whether to fetch the <c>.pdb</c> next to the image (default). A missing PDB is tolerated —
    /// the image simply loads without debug symbols.
    /// </summary>
    public bool DownloadSymbols { get; init; } = true;

    /// <summary>
    /// URL of a checksum manifest for the image — any text whose first 64-hex-digit token is the
    /// SHA-256 of the <c>.dll</c> (the output of <c>sha256sum</c> qualifies). Mutually exclusive
    /// with <see cref="ExpectedSha256"/>. When neither is configured, integrity is not verified.
    /// </summary>
    public Uri? ChecksumUrl { get; init; }

    /// <summary>
    /// Pinned SHA-256 (64 hex digits) of the expected image. Mutually exclusive with
    /// <see cref="ChecksumUrl"/>.
    /// </summary>
    public string? ExpectedSha256 { get; init; }

    /// <summary>
    /// Optional local directory where the last verified image (and its ETag) is persisted.
    /// Enables offline-first startup on devices: when the server is unreachable, the cached copy
    /// is served instead. Integrity failures never fall back to the cache.
    /// </summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// Interval between conditional polls (<c>If-None-Match</c>) while watching. HTTP has no
    /// push, so change detection is polling; unchanged versions cost a 304 with no body.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
}
