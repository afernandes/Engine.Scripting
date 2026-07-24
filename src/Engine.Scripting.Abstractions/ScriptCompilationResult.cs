namespace Engine.Scripting.Abstractions;

/// <summary>
/// Outcome of a script compilation: structured diagnostics plus the emitted image on success.
/// </summary>
/// <remarks>
/// A failed compilation is a normal, expected outcome — the consumer keeps the previous
/// generation running and surfaces <see cref="Diagnostics"/>. Compilers implementing
/// <see cref="IScriptCompiler"/> never throw for compilation problems.
/// </remarks>
public sealed record ScriptCompilationResult
{
    /// <summary>Whether an assembly image was produced.</summary>
    public required bool Success { get; init; }

    /// <summary>Compiler diagnostics, ordered as reported.</summary>
    public required IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }

    /// <summary>The emitted image, or <see langword="null"/> when <see cref="Success"/> is false.</summary>
    public ScriptAssemblyImage? Image { get; init; }

    /// <summary>Wall-clock duration of the compilation.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Creates a successful result carrying the emitted <paramref name="image"/>.</summary>
    /// <param name="image">The emitted assembly image.</param>
    /// <param name="diagnostics">Diagnostics reported alongside the successful emit.</param>
    /// <param name="duration">Wall-clock duration of the compilation.</param>
    public static ScriptCompilationResult Succeeded(ScriptAssemblyImage image, IReadOnlyList<ScriptDiagnostic> diagnostics, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new ScriptCompilationResult { Success = true, Image = image, Diagnostics = diagnostics, Duration = duration };
    }

    /// <summary>Creates a failed result carrying the reported <paramref name="diagnostics"/>.</summary>
    /// <param name="diagnostics">Diagnostics explaining the failure.</param>
    /// <param name="duration">Wall-clock duration of the compilation attempt.</param>
    public static ScriptCompilationResult Failed(IReadOnlyList<ScriptDiagnostic> diagnostics, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new ScriptCompilationResult { Success = false, Diagnostics = diagnostics, Duration = duration };
    }
}
