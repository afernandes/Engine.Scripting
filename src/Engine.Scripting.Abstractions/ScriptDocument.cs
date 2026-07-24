namespace Engine.Scripting.Abstractions;

/// <summary>
/// A single script source document produced by an <see cref="IScriptSource"/>.
/// </summary>
/// <param name="DocumentId">
/// Opaque, stable identifier of the document within its source: a full file path for a
/// file-system source, a row key or logical name for a database source. It is also used as the
/// document path in compilation diagnostics and in the emitted debug information.
/// </param>
/// <param name="Content">The C# source text of the document.</param>
public sealed record ScriptDocument(string DocumentId, string Content);
