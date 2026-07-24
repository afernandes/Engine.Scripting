namespace Engine.Scripting.Extensions.Hosting.Tests;

/// <summary>GC-probing tests must not run in parallel with anything else.</summary>
[CollectionDefinition("UnloadSensitive", DisableParallelization = true)]
public sealed class UnloadSensitiveCollection;
