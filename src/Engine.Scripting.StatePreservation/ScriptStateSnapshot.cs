namespace Engine.Scripting.StatePreservation;

/// <summary>
/// The captured state of one script instance, taken right before its generation is unloaded.
/// </summary>
/// <remarks>
/// By construction the snapshot can never pin the retiring load context: values whose runtime
/// type lives inside the script's collectible context are filtered out at capture time and
/// reported in <paramref name="DiscardedMembers"/>. That filtering is what allows the snapshot
/// to sit in the reload pipeline while the old generation is being collected.
/// </remarks>
/// <param name="TypeFullName">Full name of the script type the snapshot was captured from.</param>
/// <param name="Values">Captured values keyed by their snapshot key (attribute key or member name).</param>
/// <param name="DiscardedMembers">
/// Keys of members whose values could not be captured — non-migratable script-defined types or
/// getter failures — each already logged as a warning.
/// </param>
/// <param name="CapturedAtUtc">UTC timestamp of the capture.</param>
public sealed record ScriptStateSnapshot(
    string TypeFullName,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> DiscardedMembers,
    DateTimeOffset CapturedAtUtc);
