using Microsoft.Extensions.Logging;

namespace Engine.Scripting.StatePreservation;

/// <summary>Source-generated log messages for the state-preservation feature (event ids 30xx).</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "State value '{Key}' of type {ValueType} on {ScriptType} cannot migrate across generations "
            + "(its type lives inside the collectible script context); the value was discarded")]
    public static partial void ValueNotMigratable(ILogger logger, string key, string valueType, string scriptType);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "State value '{Key}' was not restored: snapshot value of type {ValueType} is not assignable to member type {MemberType}")]
    public static partial void IncompatibleValue(ILogger logger, string key, string valueType, string memberType);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "Snapshot value '{Key}' has no matching [HotReloadState] member on {ScriptType} (member removed or renamed); the value was dropped")]
    public static partial void OrphanSnapshotValue(ILogger logger, string key, string scriptType);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Debug,
        Message = "Member '{Key}' on {ScriptType} has no value in the snapshot; keeping its initializer value")]
    public static partial void SnapshotMissingKey(ILogger logger, string key, string scriptType);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning,
        Message = "Failed to read state member '{Key}' on {ScriptType}; the member was skipped")]
    public static partial void CaptureMemberFailed(ILogger logger, string key, string scriptType, Exception exception);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning,
        Message = "Failed to write state member '{Key}' on {ScriptType}; the member keeps its initializer value")]
    public static partial void RestoreMemberFailed(ILogger logger, string key, string scriptType, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Debug,
        Message = "Duplicate [HotReloadState] key '{Key}' declared on {ScriptType}; the most derived declaration wins")]
    public static partial void DuplicateStateKey(ILogger logger, string key, string scriptType);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning,
        Message = "[HotReloadState] property '{PropertyName}' on {ScriptType} is not eligible: it needs both a getter and a setter and cannot be an indexer")]
    public static partial void PropertyNotEligible(ILogger logger, string propertyName, string scriptType);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Debug,
        Message = "Captured {ValueCount} state value(s) from {ScriptType} ({DiscardedCount} discarded)")]
    public static partial void StateCaptured(ILogger logger, int valueCount, string scriptType, int discardedCount);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Debug,
        Message = "Restored {RestoredCount} state value(s) onto {ScriptType}")]
    public static partial void StateRestored(ILogger logger, int restoredCount, string scriptType);
}
