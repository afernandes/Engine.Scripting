namespace Engine.Scripting.Orchestration.Tests;

/// <summary>Disposable unique temp directory for file-system based tests.</summary>
internal sealed class TempScriptDirectory : IDisposable
{
    public TempScriptDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EngineScriptingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string FilePath(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a watcher may still be flushing; the OS temp cleaner will get it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
