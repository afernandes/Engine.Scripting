using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Orchestration;

/// <summary>Source-generated log messages for orchestration and built-in sources (event ids 50xx).</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Reload starting (trigger: {Trigger}, {ChangedCount} changed document(s))")]
    public static partial void ReloadStarting(ILogger logger, ReloadTrigger trigger, int changedCount);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Reload succeeded: generation {Generation} active after {ElapsedMilliseconds} ms (unload timed out: {UnloadTimedOut})")]
    public static partial void ReloadSucceeded(ILogger logger, int generation, long elapsedMilliseconds, bool unloadTimedOut);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Warning,
        Message = "Reload not applied ({Outcome}); {ErrorCount} error diagnostic(s); the previous generation stays active")]
    public static partial void ReloadNotApplied(ILogger logger, ReloadOutcome outcome, int errorCount);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Error,
        Message = "A {EventName} event handler threw; the reload pipeline is unaffected")]
    public static partial void EventHandlerFailed(ILogger logger, string eventName, Exception exception);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning,
        Message = "OnBeforeReloadAsync failed on {ScriptType}; continuing the reload")]
    public static partial void BeforeReloadHookFailed(ILogger logger, string scriptType, Exception exception);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Warning,
        Message = "OnAfterReloadAsync failed on {ScriptType}; continuing the reload")]
    public static partial void AfterReloadHookFailed(ILogger logger, string scriptType, Exception exception);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Warning,
        Message = "Script type {TypeFullName} no longer exists in the new generation; its handle {HandleId} was unregistered")]
    public static partial void ScriptTypeMissing(ILogger logger, string typeFullName, Guid handleId);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Warning,
        Message = "Failed to activate script type {TypeFullName}; the script is skipped for this generation")]
    public static partial void ScriptActivationFailed(ILogger logger, string typeFullName, Exception exception);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Warning,
        Message = "Some script types failed to load ({FailedCount} loader error(s); first: {FirstError}); continuing with the loadable ones")]
    public static partial void PartialTypeLoad(ILogger logger, int failedCount, string firstError);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Error,
        Message = "Reload pipeline faulted; the previous generation stays active when possible")]
    public static partial void PipelineFaulted(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Error,
        Message = "Debounced reload callback failed")]
    public static partial void DebounceCallbackFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Warning,
        Message = "Failed to read {Path} after retries")]
    public static partial void FileReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 5013, Level = LogLevel.Warning,
        Message = "File watcher error (possible buffer overflow); a full rescan was scheduled")]
    public static partial void WatcherError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5014, Level = LogLevel.Debug,
        Message = "Activated script {TypeFullName} as {HandleId}")]
    public static partial void ScriptActivated(ILogger logger, string typeFullName, Guid handleId);

    [LoggerMessage(EventId = 5015, Level = LogLevel.Warning,
        Message = "Assembly image source has no published image; keeping the current generation")]
    public static partial void ImageSourceEmpty(ILogger logger);

    [LoggerMessage(EventId = 5016, Level = LogLevel.Debug,
        Message = "Source watching started ({SourceDescription})")]
    public static partial void SourceWatchingStarted(ILogger logger, string sourceDescription);

    [LoggerMessage(EventId = 5017, Level = LogLevel.Information,
        Message = "Downloaded script assembly image: {AssemblyName} ({PeBytes} bytes, symbols: {HasSymbols}, etag: {ETag})")]
    public static partial void ImageDownloaded(ILogger logger, string assemblyName, int peBytes, bool hasSymbols, string? etag);

    [LoggerMessage(EventId = 5018, Level = LogLevel.Debug,
        Message = "Remote script assembly image not modified (etag: {ETag})")]
    public static partial void ImageNotModified(ILogger logger, string? etag);

    [LoggerMessage(EventId = 5019, Level = LogLevel.Warning,
        Message = "Polling the remote script assembly image failed; will retry on the next interval")]
    public static partial void ImagePollFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5020, Level = LogLevel.Error,
        Message = "Script assembly image REJECTED: SHA-256 mismatch (expected {ExpectedPrefix}…, computed {ActualPrefix}…). "
            + "The current generation stays active")]
    public static partial void ChecksumMismatch(ILogger logger, string expectedPrefix, string actualPrefix);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Warning,
        Message = "Remote script assembly image unavailable; serving {Origin} copy instead")]
    public static partial void ImageServedFromFallback(ILogger logger, string origin, Exception exception);

    [LoggerMessage(EventId = 5022, Level = LogLevel.Debug,
        Message = "Symbols (.pdb) download failed; the image loads without debug symbols")]
    public static partial void SymbolsDownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5023, Level = LogLevel.Debug,
        Message = "Failed to update the local image cache at {CacheDirectory}; continuing without cache")]
    public static partial void CacheWriteFailed(ILogger logger, string cacheDirectory, Exception exception);
}
