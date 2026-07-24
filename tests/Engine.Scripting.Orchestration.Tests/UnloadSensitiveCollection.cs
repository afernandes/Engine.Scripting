namespace Engine.Scripting.Orchestration.Tests;

/// <summary>
/// Reload tests force full GCs (unload verification) and some pin statics on purpose; they must
/// not run in parallel with other tests.
/// </summary>
[CollectionDefinition("UnloadSensitive", DisableParallelization = true)]
public sealed class UnloadSensitiveCollection;
