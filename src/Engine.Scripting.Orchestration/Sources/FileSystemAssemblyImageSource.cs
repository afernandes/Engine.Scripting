using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Orchestration.Sources;

/// <summary>
/// Built-in <see cref="IScriptAssemblyImageSource"/> over a precompiled script assembly on disk:
/// watches one <c>.dll</c> path and picks up its sibling <c>.pdb</c> so breakpoints keep working
/// in production-style deployments.
/// </summary>
/// <remarks>
/// This is the "compile once, distribute the binary" consumption side: a build step publishes
/// <c>scripts.dll</c> (via <see cref="ScriptAssemblyImage.WriteToDirectoryAsync"/> or a plain
/// <c>dotnet build</c> of a script project), a deployment copies it over this path, and the
/// orchestrator hot-swaps the running generation. Reads retry briefly because the copy may still
/// be in progress when the change event arrives.
/// </remarks>
public sealed class FileSystemAssemblyImageSource : IScriptAssemblyImageSource
{
    private const int MaxReadAttempts = 5;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly string _dllPath;
    private readonly string _directory;
    private readonly string _fileName;
    private readonly ILogger<FileSystemAssemblyImageSource> _logger;
    private FileSystemWatcher? _watcher;

    /// <summary>Creates the source.</summary>
    /// <param name="assemblyFilePath">Full path of the <c>.dll</c> to watch.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public FileSystemAssemblyImageSource(string assemblyFilePath, ILogger<FileSystemAssemblyImageSource>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyFilePath);

        _dllPath = Path.GetFullPath(assemblyFilePath);
        _directory = Path.GetDirectoryName(_dllPath)
            ?? throw new ArgumentException("The assembly path must include a directory.", nameof(assemblyFilePath));
        _fileName = Path.GetFileName(_dllPath);
        _logger = logger ?? NullLogger<FileSystemAssemblyImageSource>.Instance;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task<ScriptAssemblyImage?> LoadImageAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dllPath))
        {
            return null;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ScriptAssemblyImage.ReadFromFileAsync(_dllPath, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < MaxReadAttempts)
            {
                // The publisher may still be copying the file; wait briefly and retry.
                await Task.Delay(ReadRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                Log.FileReadFailed(_logger, _dllPath, exception);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public Task StartWatchingAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_directory);

        var watcher = new FileSystemWatcher(_directory, _fileName)
        {
            InternalBufferSize = 16 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };

        watcher.Changed += OnFileEvent;
        watcher.Created += OnFileEvent;
        watcher.Renamed += OnFileEvent;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;

        _watcher = watcher;
        Log.SourceWatchingStarted(_logger, $"assembly image: {_dllPath}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopWatchingAsync(CancellationToken cancellationToken)
    {
        DisposeWatcher();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DisposeWatcher();
        return ValueTask.CompletedTask;
    }

    private void DisposeWatcher()
    {
        var watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Changed -= OnFileEvent;
        watcher.Created -= OnFileEvent;
        watcher.Renamed -= OnFileEvent;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Log.WatcherError(_logger, e.GetException());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
