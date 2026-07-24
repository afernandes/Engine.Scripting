using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Instances;

/// <summary>Source-generated log messages for the instance registry (event ids 40xx).</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug,
        Message = "Registered script instance {HandleId} of type {TypeFullName}")]
    public static partial void InstanceRegistered(ILogger logger, Guid handleId, string typeFullName);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Debug,
        Message = "Unregistered script instance {HandleId} of type {TypeFullName}")]
    public static partial void InstanceUnregistered(ILogger logger, Guid handleId, string typeFullName);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Debug,
        Message = "Detached {Count} script instance(s) ahead of a reload")]
    public static partial void InstancesDetached(ILogger logger, int count);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning,
        Message = "Attach failed: handle {HandleId} is not registered")]
    public static partial void AttachUnknownHandle(ILogger logger, Guid handleId);
}
