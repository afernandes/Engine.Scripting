using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Engine.Scripting.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Compilation;

/// <summary>
/// Roslyn-based incremental script compiler: keeps one parsed syntax tree per document, replaces
/// only the tree of a changed document, and emits straight to memory (PE + portable PDB) —
/// no file is ever written or locked.
/// </summary>
/// <remarks>
/// <para>
/// Each successful compilation emits an assembly with a unique name
/// (<c>{prefix}.g{n}</c>), so consecutive hot-reload generations are distinguishable in stack
/// traces, logs and debugger module lists.
/// </para>
/// <para>
/// <see cref="CompileAsync"/> never throws for compilation problems; see
/// <see cref="IScriptCompiler"/> for the contract.
/// </para>
/// </remarks>
public sealed class IncrementalScriptCompiler : IScriptCompiler
{
    private readonly ScriptCompilerOptions _options;
    private readonly ILogger<IncrementalScriptCompiler> _logger;
    private readonly CSharpParseOptions _parseOptions;
    private readonly Lazy<ImmutableArray<MetadataReference>> _references;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SyntaxTree> _trees = new(StringComparer.Ordinal);
    private CSharpCompilation? _compilation;
    private int _emitCounter;

    /// <summary>Creates the compiler.</summary>
    /// <param name="options">
    /// Compiler configuration. The reference set is materialized on the first compilation;
    /// configure it fully before compiling.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public IncrementalScriptCompiler(ScriptCompilerOptions options, ILogger<IncrementalScriptCompiler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _logger = logger ?? NullLogger<IncrementalScriptCompiler>.Instance;
        _parseOptions = new CSharpParseOptions(
            languageVersion: options.LanguageVersion,
            preprocessorSymbols: options.PreprocessorSymbols.ToArray());
        _references = new Lazy<ImmutableArray<MetadataReference>>(
            () => ReferenceSetBuilder.Build(options),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public int SourceCount
    {
        get
        {
            lock (_gate)
            {
                return _trees.Count;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DocumentIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _trees.Keys];
            }
        }
    }

    /// <inheritdoc />
    public void AddOrUpdateSource(string documentId, string sourceText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(sourceText);

        // UTF-8 encoding is mandatory: without an encoding the tree cannot participate in
        // portable-PDB emit (CS8055) nor be embedded into the PDB for debugging.
        var text = SourceText.From(sourceText, Encoding.UTF8);
        var newTree = CSharpSyntaxTree.ParseText(text, _parseOptions, path: documentId);

        lock (_gate)
        {
            if (_trees.TryGetValue(documentId, out var oldTree))
            {
                _trees[documentId] = newTree;
                _compilation = _compilation?.ReplaceSyntaxTree(oldTree, newTree);
                Log.SourceUpdated(_logger, documentId);
            }
            else
            {
                _trees.Add(documentId, newTree);
                _compilation = _compilation?.AddSyntaxTrees(newTree);
                Log.SourceAdded(_logger, documentId);
            }
        }
    }

    /// <summary>
    /// Convenience: loads every file matching <paramref name="searchPattern"/> under
    /// <paramref name="rootPath"/> as a source document, keyed by full file path.
    /// </summary>
    /// <param name="rootPath">Directory to scan.</param>
    /// <param name="searchPattern">File pattern, defaults to <c>*.cs</c>.</param>
    /// <param name="recursive">Whether subdirectories are included (default).</param>
    /// <param name="cancellationToken">Token that aborts the reads.</param>
    /// <returns>The number of documents loaded.</returns>
    public async Task<int> AddSourcesFromDirectoryAsync(
        string rootPath,
        string searchPattern = "*.cs",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(rootPath, searchPattern, option))
        {
            var fullPath = Path.GetFullPath(file);
            var content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            AddOrUpdateSource(fullPath, content);
            count++;
        }

        return count;
    }

    /// <inheritdoc />
    public bool RemoveSource(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        lock (_gate)
        {
            if (!_trees.Remove(documentId, out var oldTree))
            {
                return false;
            }

            _compilation = _compilation?.RemoveSyntaxTrees(oldTree);
            Log.SourceRemoved(_logger, documentId);
            return true;
        }
    }

    /// <inheritdoc />
    public Task<ScriptCompilationResult> CompileAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => CompileCore(cancellationToken), cancellationToken);

    private ScriptCompilationResult CompileCore(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            CSharpCompilation compilation;
            SyntaxTree[] trees;
            lock (_gate)
            {
                compilation = _compilation ??= CreateCompilation();
                trees = [.. _trees.Values];
            }

            var assemblyName = $"{_options.AssemblyNamePrefix}.g{Interlocked.Increment(ref _emitCounter)}";
            var toEmit = compilation.WithAssemblyName(assemblyName);

            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();

            var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
            var embeddedTexts = _options.EmbedSourcesInPdb
                ? trees.Select(tree => EmbeddedText.FromSource(tree.FilePath, tree.GetText(cancellationToken))).ToArray()
                : null;

            var emitResult = toEmit.Emit(
                peStream,
                pdbStream,
                options: emitOptions,
                embeddedTexts: embeddedTexts,
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            var diagnostics = DiagnosticMapper.Map(emitResult.Diagnostics);

            if (!emitResult.Success)
            {
                var errorCount = diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Error);
                Log.CompilationFailed(_logger, errorCount, stopwatch.ElapsedMilliseconds);
                return ScriptCompilationResult.Failed(diagnostics, stopwatch.Elapsed);
            }

            var warningCount = diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Warning);
            Log.CompilationSucceeded(_logger, assemblyName, trees.Length, warningCount, stopwatch.ElapsedMilliseconds);

            var image = new ScriptAssemblyImage(assemblyName, peStream.ToArray(), pdbStream.ToArray());
            return ScriptCompilationResult.Succeeded(image, diagnostics, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Log.CompilationCrashed(_logger, exception);

            var diagnostic = new ScriptDiagnostic(
                "ESC0001",
                ScriptDiagnosticSeverity.Error,
                $"Unexpected compilation failure ({exception.GetType().Name}): {exception.Message}",
                DocumentId: null,
                Line: 0,
                Column: 0);
            return ScriptCompilationResult.Failed([diagnostic], stopwatch.Elapsed);
        }
    }

    private CSharpCompilation CreateCompilation()
    {
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: _options.OptimizationLevel,
            allowUnsafe: _options.AllowUnsafe,
            deterministic: true);

        return CSharpCompilation.Create(
            _options.AssemblyNamePrefix,
            _trees.Values,
            _references.Value,
            compilationOptions);
    }
}
