using Engine.Scripting.Compilation;
using Engine.Scripting.Orchestration.Sources;

namespace Engine.Scripting.Orchestration.Tests;

public class OrchestratorValidationTests
{
    [Fact]
    public void Ctor_NenhumaOrigemConfigurada_LancaArgumentException()
    {
        var options = new HotReloadOptions
        {
            Compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions()),
        };

        Assert.Throws<ArgumentException>(() => new HotReloadOrchestrator(options));
    }

    [Fact]
    public void Ctor_OrigemDuplicada_LancaArgumentException()
    {
        var options = new HotReloadOptions
        {
            Source = new InMemoryScriptSource(),
            ScriptsPath = "scripts",
            Compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions()),
        };

        Assert.Throws<ArgumentException>(() => new HotReloadOrchestrator(options));
    }

    [Fact]
    public void Ctor_ModoFonteSemCompiler_LancaArgumentException()
    {
        var options = new HotReloadOptions
        {
            Source = new InMemoryScriptSource(),
        };

        Assert.Throws<ArgumentException>(() => new HotReloadOrchestrator(options));
    }

    [Fact]
    public void Ctor_ModoImagemComCompiler_LancaArgumentException()
    {
        var options = new HotReloadOptions
        {
            ImageSource = new FileSystemAssemblyImageSource(Path.Combine(Path.GetTempPath(), "scripts.dll")),
            Compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions()),
        };

        Assert.Throws<ArgumentException>(() => new HotReloadOrchestrator(options));
    }

    [Fact]
    public async Task ReloadAsync_SemStartAsync_LancaInvalidOperationException()
    {
        var options = new HotReloadOptions
        {
            Source = new InMemoryScriptSource(),
            Compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions()),
        };
        await using var orchestrator = new HotReloadOrchestrator(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ReloadAsync(TestContext.Current.CancellationToken));
    }
}
