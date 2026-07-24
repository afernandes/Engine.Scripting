using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Compilation;

/// <summary>Source-generated log messages for the compilation feature (event ids 10xx).</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Script source added: {DocumentId}")]
    public static partial void SourceAdded(ILogger logger, string documentId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Script source updated: {DocumentId}")]
    public static partial void SourceUpdated(ILogger logger, string documentId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Script source removed: {DocumentId}")]
    public static partial void SourceRemoved(ILogger logger, string documentId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Script compilation succeeded: {AssemblyName} ({SourceCount} document(s), {WarningCount} warning(s), {ElapsedMilliseconds} ms)")]
    public static partial void CompilationSucceeded(ILogger logger, string assemblyName, int sourceCount, int warningCount, long elapsedMilliseconds);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "Script compilation failed with {ErrorCount} error(s) after {ElapsedMilliseconds} ms; the previous generation stays active")]
    public static partial void CompilationFailed(ILogger logger, int errorCount, long elapsedMilliseconds);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error,
        Message = "Unexpected script compilation failure; reported as diagnostic ESC0001")]
    public static partial void CompilationCrashed(ILogger logger, Exception exception);
}
