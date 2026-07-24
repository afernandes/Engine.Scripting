using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.StatePreservation;

/// <summary>
/// Captures and restores <c>[HotReloadState]</c> members across hot-reload generations via
/// reflection.
/// </summary>
/// <remarks>
/// Neither <see cref="Capture"/> nor <see cref="Restore"/> ever throws for state problems: a
/// member that cannot be captured, migrated or restored is skipped and logged, and the reload
/// carries on. Losing one field's value must never break the whole reload.
/// </remarks>
public sealed class StatePreservationService
{
    private readonly ILogger<StatePreservationService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public StatePreservationService(ILogger<StatePreservationService>? logger = null)
    {
        _logger = logger ?? NullLogger<StatePreservationService>.Instance;
    }

    /// <summary>
    /// Extracts a snapshot of every <c>[HotReloadState]</c> member of
    /// <paramref name="scriptInstance"/>.
    /// </summary>
    /// <remarks>
    /// Values whose runtime type lives inside the instance's collectible load context are
    /// discarded here, at capture time — see <see cref="ScriptStateSnapshot"/> for why that
    /// placement matters.
    /// </remarks>
    /// <param name="scriptInstance">The live script instance about to be retired.</param>
    public ScriptStateSnapshot Capture(object scriptInstance)
    {
        ArgumentNullException.ThrowIfNull(scriptInstance);

        var type = scriptInstance.GetType();
        var typeName = type.FullName ?? type.Name;
        var scriptContext = AssemblyLoadContext.GetLoadContext(type.Assembly);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var discarded = new List<string>();

        foreach (var member in HotReloadMemberScanner.Scan(type, _logger))
        {
            object? value;
            try
            {
                value = member.GetValue(scriptInstance);
            }
            catch (Exception exception)
            {
                Log.CaptureMemberFailed(_logger, member.Key, typeName, exception);
                discarded.Add(member.Key);
                continue;
            }

            if (!AlcSafetyInspector.CanMigrate(value, scriptContext))
            {
                Log.ValueNotMigratable(_logger, member.Key, value!.GetType().FullName ?? "?", typeName);
                discarded.Add(member.Key);
                continue;
            }

            values[member.Key] = value;
        }

        Log.StateCaptured(_logger, values.Count, typeName, discarded.Count);
        return new ScriptStateSnapshot(typeName, values, discarded, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Writes the compatible values of <paramref name="snapshot"/> onto
    /// <paramref name="scriptInstance"/> (an instance of the <b>new</b> generation's type).
    /// </summary>
    /// <remarks>
    /// Tolerant by member: a snapshot key without a matching member (removed/renamed field), a
    /// value whose type no longer fits (changed type — including script-declared types, whose
    /// identity never survives a reload), or a setter that throws, are each logged and skipped.
    /// </remarks>
    /// <param name="scriptInstance">The freshly created instance to restore onto.</param>
    /// <param name="snapshot">Snapshot captured from the previous generation.</param>
    public void Restore(object scriptInstance, ScriptStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(scriptInstance);
        ArgumentNullException.ThrowIfNull(snapshot);

        var type = scriptInstance.GetType();
        var typeName = type.FullName ?? type.Name;

        var addressedKeys = new HashSet<string>(StringComparer.Ordinal);
        var restoredCount = 0;

        foreach (var member in HotReloadMemberScanner.Scan(type, _logger))
        {
            if (!snapshot.Values.TryGetValue(member.Key, out var value))
            {
                Log.SnapshotMissingKey(_logger, member.Key, typeName);
                continue;
            }

            addressedKeys.Add(member.Key);

            if (value is null)
            {
                var isNonNullableValueType = member.MemberType.IsValueType
                    && Nullable.GetUnderlyingType(member.MemberType) is null;
                if (isNonNullableValueType)
                {
                    Log.IncompatibleValue(_logger, member.Key, "null", member.MemberType.FullName ?? "?");
                    continue;
                }
            }
            else if (!member.MemberType.IsInstanceOfType(value))
            {
                Log.IncompatibleValue(_logger, member.Key, value.GetType().FullName ?? "?", member.MemberType.FullName ?? "?");
                continue;
            }

            try
            {
                member.SetValue(scriptInstance, value);
                restoredCount++;
            }
            catch (Exception exception)
            {
                Log.RestoreMemberFailed(_logger, member.Key, typeName, exception);
            }
        }

        foreach (var key in snapshot.Values.Keys)
        {
            if (!addressedKeys.Contains(key))
            {
                Log.OrphanSnapshotValue(_logger, key, typeName);
            }
        }

        Log.StateRestored(_logger, restoredCount, typeName);
    }
}
