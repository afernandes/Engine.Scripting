namespace Engine.Scripting.Hosting.Tests;

/// <summary>
/// Tests in this collection force full GCs and probe WeakReferences; running them in parallel
/// with other tests would let unrelated allocations and pinned statics contaminate the results.
/// </summary>
[CollectionDefinition("UnloadSensitive", DisableParallelization = true)]
public sealed class UnloadSensitiveCollection;
