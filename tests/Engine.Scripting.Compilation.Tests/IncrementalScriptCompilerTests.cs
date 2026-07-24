using System.Reflection.Metadata;
using Engine.Scripting.Abstractions;
using Engine.Scripting.Compilation.Tests.HostContracts;

namespace Engine.Scripting.Compilation.Tests;

public class IncrementalScriptCompilerTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompileAsync_CodigoValido_RetornaImagemComPePdbENomeUnico()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("script1.cs", "public class Foo { public int Bar() => 42; }");

        var result = await compiler.CompileAsync(TestToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Image);
        Assert.NotEmpty(result.Image.PeBytes);
        Assert.NotNull(result.Image.PdbBytes);
        Assert.NotEmpty(result.Image.PdbBytes);
        Assert.StartsWith("Engine.Scripting.Generated.g", result.Image.AssemblyName, StringComparison.Ordinal);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task CompileAsync_ErroDeSintaxe_RetornaDiagnosticoComLocalizacaoSemLancar()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("broken.cs", "public class Foo {\n    public int Bar() => ;\n}");

        var result = await compiler.CompileAsync(TestToken);

        Assert.False(result.Success);
        Assert.Null(result.Image);
        var error = Assert.Single(result.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error);
        Assert.Equal("broken.cs", error.DocumentId);
        Assert.Equal(2, error.Line);
        Assert.True(error.Column >= 1);
        Assert.StartsWith("CS", error.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompileAsync_ErroCorrigidoNaMesmaInstancia_VoltaACompilar()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("script.cs", "public class Foo { public int Bar() => ; }");

        var broken = await compiler.CompileAsync(TestToken);
        compiler.AddOrUpdateSource("script.cs", "public class Foo { public int Bar() => 1; }");
        var fixedResult = await compiler.CompileAsync(TestToken);

        Assert.False(broken.Success);
        Assert.True(fixedResult.Success);
        Assert.Equal(1, compiler.SourceCount);
    }

    [Fact]
    public async Task AddOrUpdateSource_AtualizarUmDeDoisDocumentos_MantemContagemECompila()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("a.cs", "public class ClassA { public int Value() => 1; }");
        compiler.AddOrUpdateSource("b.cs", "public class ClassB { public ClassA A { get; } = new(); }");

        var first = await compiler.CompileAsync(TestToken);
        compiler.AddOrUpdateSource("a.cs", "public class ClassA { public int Value() => 2; }");
        var second = await compiler.CompileAsync(TestToken);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(2, compiler.SourceCount);
        Assert.NotNull(first.Image);
        Assert.NotNull(second.Image);
        Assert.NotEqual(first.Image.AssemblyName, second.Image.AssemblyName);
    }

    [Fact]
    public async Task RemoveSource_DocumentoComTipoReferenciadoPorOutro_QuebraCompilacaoComCs0246()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("a.cs", "public class ClassA { }");
        compiler.AddOrUpdateSource("b.cs", "public class ClassB { public ClassA? A { get; set; } }");

        var before = await compiler.CompileAsync(TestToken);
        var removed = compiler.RemoveSource("a.cs");
        var after = await compiler.CompileAsync(TestToken);

        Assert.True(before.Success);
        Assert.True(removed);
        Assert.Equal(1, compiler.SourceCount);
        Assert.False(after.Success);
        Assert.Contains(after.Diagnostics, d => d.Id == "CS0246");
    }

    [Fact]
    public async Task CompileAsync_ReferenciaDeAssemblyDoHost_PermiteImplementarContratoDoHost()
    {
        var options = new ScriptCompilerOptions();
        options.AddReference(typeof(IHostContract));
        var compiler = new IncrementalScriptCompiler(options);
        compiler.AddOrUpdateSource("impl.cs", $$"""
            public class Impl : {{typeof(IHostContract).FullName}}
            {
                public int GetValue() => 7;
            }
            """);

        var result = await compiler.CompileAsync(TestToken);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public async Task CompileAsync_TpaHabilitada_PermiteUsarSystemTextJson()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("json.cs", """
            public static class JsonUser
            {
                public static string Serialize() => System.Text.Json.JsonSerializer.Serialize(new[] { 1, 2, 3 });
            }
            """);

        var result = await compiler.CompileAsync(TestToken);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public async Task CompileAsync_ReferenciaComCaminhoInexistente_RetornaEsc0001SemLancar()
    {
        var options = new ScriptCompilerOptions();
        options.ReferencePaths.Add(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"));
        var compiler = new IncrementalScriptCompiler(options);
        compiler.AddOrUpdateSource("script.cs", "public class Foo { }");

        var result = await compiler.CompileAsync(TestToken);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ESC0001", diagnostic.Id);
        Assert.Equal(ScriptDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void AddReference_AssemblySemLocation_LancaScriptingConfigurationException()
    {
        var options = new ScriptCompilerOptions();
        var dynamicAssembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName($"Dynamic{Guid.NewGuid():N}"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);

        Assert.Throws<ScriptingConfigurationException>(() => options.AddReference(dynamicAssembly));
    }

    [Fact]
    public async Task CompileAsync_Cancelado_LancaOperationCanceledException()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("script.cs", "public class Foo { }");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(cts.Token));
    }

    [Fact]
    public async Task CompileAsync_EmbedSourcesInPdbHabilitado_PdbContemFonteEmbedado()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());
        compiler.AddOrUpdateSource("embedded.cs", "public class Embedded { public int V() => 3; }");

        var result = await compiler.CompileAsync(TestToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Image?.PdbBytes);
        Assert.True(PdbInspector.HasEmbeddedSource(result.Image.PdbBytes, "embedded.cs"));
    }

    [Fact]
    public async Task CompileAsync_EmbedSourcesInPdbDesabilitado_PdbNaoContemFonteEmbedado()
    {
        var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions { EmbedSourcesInPdb = false });
        compiler.AddOrUpdateSource("plain.cs", "public class Plain { public int V() => 3; }");

        var result = await compiler.CompileAsync(TestToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Image?.PdbBytes);
        Assert.False(PdbInspector.HasEmbeddedSource(result.Image.PdbBytes, "plain.cs"));
    }

    [Fact]
    public async Task AddSourcesFromDirectoryAsync_DiretorioComDoisArquivos_CarregaAmbos()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EngineScriptingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "one.cs"), "public class One { }", TestToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "two.cs"), "public class Two { }", TestToken);
            var compiler = new IncrementalScriptCompiler(new ScriptCompilerOptions());

            var loaded = await compiler.AddSourcesFromDirectoryAsync(directory, cancellationToken: TestToken);
            var result = await compiler.CompileAsync(TestToken);

            Assert.Equal(2, loaded);
            Assert.Equal(2, compiler.SourceCount);
            Assert.True(result.Success);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
