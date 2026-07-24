using System.IO.Enumeration;
using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Orchestration.Sources;

/// <summary>
/// Built-in <see cref="IScriptSource"/> over a directory of script files, watched with
/// <see cref="FileSystemWatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Editors save in bursts (2–4 <c>Changed</c> events per save) and often atomically
/// (write-temp-then-rename), so this source listens to <c>Changed</c>, <c>Created</c>,
/// <c>Deleted</c> and <c>Renamed</c> with an unfiltered watcher, matches paths against the
/// search pattern itself (which also catches renames <i>away from</i> the pattern), and lets the
/// orchestrator's debouncer collapse the burst. Reads retry briefly because the editor may still
/// hold the file lock when the first event arrives.
/// </para>
/// <para>
/// Document ids are normalized full paths, compared ordinal-insensitively on the watcher side to
/// match Windows semantics.
/// </para>
/// </remarks>
public sealed class FileSystemScriptSource : IScriptSource
{
    private const int MaxReadAttempts = 5;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(40);

    private readonly string _rootPath;
    private readonly string _searchPattern;
    private readonly bool _includeSubdirectories;
    private readonly ILogger<FileSystemScriptSource> _logger;
    private FileSystemWatcher? _watcher;

    /// <summary>Creates the source.</summary>
    /// <param name="rootPath">Directory containing the scripts (created on watch start if missing).</param>
    /// <param name="searchPattern">File pattern, defaults to <c>*.cs</c>.</param>
    /// <param name="includeSubdirectories">Whether subdirectories are included (default).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public FileSystemScriptSource(
        string rootPath,
        string searchPattern = "*.cs",
        bool includeSubdirectories = true,
        ILogger<FileSystemScriptSource>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        _rootPath = Path.GetFullPath(rootPath);
        _searchPattern = searchPattern;
        _includeSubdirectories = includeSubdirectories;
        _logger = logger ?? NullLogger<FileSystemScriptSource>.Instance;
    }

    /// <inheritdoc />
    public event EventHandler<ScriptSourceChangedEventArgs>? Changed;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScriptDocument>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootPath))
        {
            return [];
        }

        var option = _includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var documents = new List<ScriptDocument>();
        foreach (var file in Directory.EnumerateFiles(_rootPath, _searchPattern, option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(file);
            var content = await ReadWithRetryAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (content is not null)
            {
                documents.Add(new ScriptDocument(fullPath, content));
            }
        }

        return documents;
    }

    /// <inheritdoc />
    public async Task<ScriptDocument?> LoadAsync(string documentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var fullPath = Path.GetFullPath(documentId);
        var content = await ReadWithRetryAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return content is null ? null : new ScriptDocument(fullPath, content);
    }

    /// <inheritdoc />
    public Task StartWatchingAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_rootPath);

        // Watch everything and match the pattern ourselves: an atomic save renames a temp file
        // into the pattern, and a rename AWAY from the pattern is a removal — a filtered watcher
        // would miss half of those transitions.
        var watcher = new FileSystemWatcher(_rootPath, "*.*")
        {
            IncludeSubdirectories = _includeSubdirectories,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };

        watcher.Changed += OnFileEvent;
        watcher.Created += OnFileEvent;
        watcher.Deleted += OnFileEvent;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;

        _watcher = watcher;
        Log.SourceWatchingStarted(_logger, $"file system: {_rootPath} ({_searchPattern})");
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
        watcher.Deleted -= OnFileEvent;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (MatchesPattern(e.FullPath))
        {
            RaiseChanged([Path.GetFullPath(e.FullPath)], requiresFullRescan: false);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var changed = new List<string>(2);
        if (MatchesPattern(e.OldFullPath))
        {
            changed.Add(Path.GetFullPath(e.OldFullPath)); // renamed away: resolves to a removal
        }

        if (MatchesPattern(e.FullPath))
        {
            changed.Add(Path.GetFullPath(e.FullPath)); // renamed into the pattern: an add/update
        }

        if (changed.Count > 0)
        {
            RaiseChanged(changed, requiresFullRescan: false);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Typically an InternalBufferOverflowException: individual events were lost, so ask the
        // consumer to rescan everything.
        Log.WatcherError(_logger, e.GetException());
        RaiseChanged([], requiresFullRescan: true);
    }

    private bool MatchesPattern(string path)
        => FileSystemName.MatchesSimpleExpression(_searchPattern, Path.GetFileName(path.AsSpan()), ignoreCase: true);

    private void RaiseChanged(IReadOnlyList<string> documentIds, bool requiresFullRescan)
        => Changed?.Invoke(this, new ScriptSourceChangedEventArgs(documentIds, requiresFullRescan));

    private async Task<string?> ReadWithRetryAsync(string fullPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < MaxReadAttempts)
            {
                // The editor may still hold the write lock; wait briefly and retry.
                await Task.Delay(ReadRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                Log.FileReadFailed(_logger, fullPath, exception);
                throw;
            }
        }
    }
}
