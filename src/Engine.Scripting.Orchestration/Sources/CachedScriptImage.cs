namespace Engine.Scripting.Orchestration.Sources;

internal sealed record CachedScriptImage(
    string Origin, string? ChecksumUrl, string? ETag,
    byte[] PeBytes, byte[]? PdbBytes, string PeHash, string? PdbHash);
