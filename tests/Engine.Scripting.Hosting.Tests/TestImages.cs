using Engine.Scripting.Abstractions;
using Engine.Scripting.Compilation;

namespace Engine.Scripting.Hosting.Tests;

/// <summary>Compiles small in-memory script images for hosting tests.</summary>
internal static class TestImages
{
    public static async Task<ScriptAssemblyImage> CompileAsync(string source, CancellationToken cancellationToken)
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("hosting-test.cs", source);

        var result = await compiler.CompileAsync(cancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Image!;
    }
}
