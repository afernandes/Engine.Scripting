# Engine.Scripting

Hot-reload de scripts C# para .NET moderno (CoreCLR), no espírito do **"partial domain reload"** que a Unity está construindo na migração de Mono para CoreCLR: edite uma classe de script em runtime, salve, e veja o código novo rodando **sem reiniciar o processo host**, com o estado dos objetos vivos preservado.

A biblioteca é **standalone e agnóstica de domínio** — serve igualmente para uma game engine, um host de automação com regras de negócio hot-swappable (estilo ABAP/ADVPL), uma API ASP.NET Core, Blazor Server ou MAUI (ver [matriz de plataformas](#matriz-de-plataformas)).

```
dotnet build          # compila a solução inteira
dotnet test           # roda os 63 testes
dotnet run --project samples/Engine.Scripting.Sample.ConsoleDemo   # demo interativa
dotnet pack -c Release -o artifacts/packages    # gera os 7 pacotes NuGet (0.1.0)
```

---

## Por que isso é difícil (e como o CoreCLR mudou o jogo)

O .NET Framework clássico tinha **AppDomains** com unload forçado. O CoreCLR **não tem AppDomains** — o único mecanismo de descarregamento é o [`AssemblyLoadContext` coletável](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability) (`isCollectible: true`), e o unload é **cooperativo, não forçado**: chamar `Unload()` apenas *inicia* o processo; ele só se completa quando **nenhuma referência forte** (stack, registrador, campo estático, delegate, GC handle) alcança, direta ou transitivamente, qualquer assembly, tipo ou instância carregada dentro do contexto. Threads executando código daquelas assemblies também bloqueiam a conclusão.

A verificação recomendada pela Microsoft — e implementada aqui — é manter uma `WeakReference` para o contexto e, após `Unload()`, rodar ciclos de `GC.Collect()` + `GC.WaitForPendingFinalizers()` até a referência morrer, com timeout. Se ela nunca morre, há um vazamento real, e a biblioteca o diagnostica em vez de fingir que não existe.

## Os quatro problemas resolvidos

| # | Problema | Solução | Onde |
|---|----------|---------|------|
| 1 | **Compilação incremental** | Uma `SyntaxTree` por documento; ao mudar um documento, só a árvore dele é re-parseada (`ReplaceSyntaxTree`); `Emit` direto para `MemoryStream` (PE + PDB portátil — disco nunca é tocado, nenhum arquivo é lockado); referências do host configuráveis + TPA (`TRUSTED_PLATFORM_ASSEMBLIES`) cacheada por processo; diagnósticos estruturados; **erro de compilação nunca derruba o host** | `Engine.Scripting.Compilation` |
| 2 | **Unload cooperativo sem vazamento** | Um ALC coletável por geração; `LoadFromStream` (nunca por path); verificação `WeakReference` + GC em loop com timeout configurável (default 5 s); zero referências internas à geração antiga após o unload — o código que solta a última referência é confinado num método `[MethodImpl(NoInlining)]` para nem um local estendido pelo JIT segurar o contexto; timeout vira evento de diagnóstico com a lista dos suspeitos clássicos no log | `Engine.Scripting.Hosting` |
| 3 | **Identidade de tipo + preservação de estado** | `OldGen!Foo` e `NewGen!Foo` são `Type`s diferentes mesmo com nome igual. Campos/propriedades `[HotReloadState]` são capturados por reflection antes do unload e reaplicados na instância nova; campo removido/renomeado/tipo incompatível → descartado com warning, nunca exceção. A filtragem de valores acontece **na captura**: valores de tipos declarados no próprio script são descartados ali, senão o snapshot pinaria o ALC antigo e todo unload daria timeout | `Engine.Scripting.StatePreservation` |
| 4 | **Orquestração** | Origem de scripts plugável (`IScriptSource` / `IScriptAssemblyImageSource`) + debounce (default 250 ms) agnóstico da origem + pipeline assíncrono serializado (compilar/carregar → snapshot → hooks → unload → load → restore → hooks) com `CancellationToken` propagado em toda a cadeia; eventos `ReloadStarted`, `ReloadSucceeded`, `ReloadFailed`, `AssemblyUnloadTimedOut` | `Engine.Scripting.Orchestration` |

Complementos: `Engine.Scripting.Instances` dá a cada script uma **identidade lógica estável** (`ScriptHandle`, um `Guid` + nome do tipo) que sobrevive aos reloads — o host guarda o handle, nunca a instância; e `Engine.Scripting.Abstractions` contém apenas contratos, **sem nenhuma dependência** (nem Roslyn, nem `System.Runtime.Loader`, nem logging).

## Arquitetura

```
Engine.Scripting.Abstractions        contratos puros: IReloadableScript, [HotReloadState],
        ▲                            IScriptSource, IScriptAssemblyImageSource, IScriptCompiler, DTOs
        │
        ├── Engine.Scripting.Compilation        IncrementalScriptCompiler (Roslyn)      ─┐
        ├── Engine.Scripting.Hosting            ReloadableScriptContext (ALC coletável)  │
        ├── Engine.Scripting.StatePreservation  StatePreservationService                 ├── Engine.Scripting.Orchestration
        └── Engine.Scripting.Instances          ScriptInstanceRegistry                   │        HotReloadOrchestrator
                                                                                        ─┘        + sources built-in
                                                                                                          ▲
                                                          Engine.Scripting.Extensions.Hosting ────────────┘
                                                          AddHotReloadScripting() · hosted service · DI nos scripts
```

Ponto estrutural importante: **`Orchestration` não referencia `Compilation`**. O compilador entra por injeção (`IScriptCompiler`, interface das Abstractions). Consequência prática: um deploy de produção que consome DLL pré-compilada **não carrega Roslyn** — relevante para dispositivos e servidores enxutos.

## Quick start (modo desenvolvimento: fonte + watcher)

```csharp
using Engine.Scripting.Abstractions;
using Engine.Scripting.Compilation;
using Engine.Scripting.Orchestration;

var compilerOptions = new ScriptCompilerOptions();
compilerOptions.AddReference(typeof(IMeuContratoDoHost)); // contratos que os scripts implementam
compilerOptions.AddReference(typeof(IReloadableScript));  // hooks + [HotReloadState]

var options = new HotReloadOptions
{
    ScriptsPath = "scripts",                              // FileSystemScriptSource built-in
    Compiler = new IncrementalScriptCompiler(compilerOptions),
    DebounceInterval = TimeSpan.FromMilliseconds(250),
};
options.Hosting.UnloadTimeout = TimeSpan.FromSeconds(5);

await using var orchestrator = new HotReloadOrchestrator(options, loggerFactory);
orchestrator.ReloadFailed += (_, e) => { /* mostrar diagnósticos; a versão anterior segue rodando */ };
orchestrator.AssemblyUnloadTimedOut += (_, e) => { /* alarme de vazamento: geração e.GenerationNumber presa */ };

await orchestrator.StartAsync(ct);

// consumo: guarde o ScriptHandle (Guid), NUNCA a instância
var handle = orchestrator.Registry.Handles.Single();
var saida = orchestrator.Registry.GetAs<IMeuContratoDoHost>(handle)?.Executar();
```

Um script é uma classe C# comum que implementa `IReloadableScript` (e os contratos do seu host):

```csharp
public class MinhaRegra : IReloadableScript, IMeuContratoDoHost
{
    [HotReloadState]                 // sobrevive ao reload
    private int _execucoes;

    [HotReloadState("saldo")]        // chave explícita: sobrevive até a rename do campo
    private decimal _saldoAcumulado;

    public ValueTask OnBeforeReloadAsync(CancellationToken ct)
    {
        // ÚLTIMA chance de soltar tudo que pinaria o ALC antigo:
        // dessinscrever eventos do host, cancelar timers/tasks, liberar handles nativos.
        return ValueTask.CompletedTask;
    }

    public ValueTask OnAfterReloadAsync(CancellationToken ct)
    {
        // reconectar recursos, revalidar estado restaurado
        return ValueTask.CompletedTask;
    }
}
```

**Regra de ouro do consumidor:** resolva a instância a cada uso via `Registry.GetAs<T>(handle)`. Guardar o retorno num campo do host é exatamente a referência forte que impede o unload — e é o que o evento `AssemblyUnloadTimedOut` vai denunciar.

### Em um Generic Host / ASP.NET Core (DI)

Com o pacote `Engine.Scripting.Extensions.Hosting`, o orchestrator vira um serviço do host — e os **scripts ganham injeção de construtor do container**:

```csharp
builder.Services.AddSingleton<IPedidoRepository, PedidoRepository>();

builder.Services.AddHotReloadScripting(options =>
{
    options.ScriptsPath = "scripts";
    options.Compiler = new IncrementalScriptCompiler(compilerOptions);
});
```

```csharp
// O script recebe serviços do container pelo construtor, re-resolvidos a cada reload:
public class RegraDeDesconto : IReloadableScript, IRegraDePedido
{
    private readonly IPedidoRepository _pedidos;

    [HotReloadState] private int _avaliacoes;

    public RegraDeDesconto(IPedidoRepository pedidos) => _pedidos = pedidos;
    // ...
}
```

O `AddHotReloadScripting` registra três coisas: o `HotReloadOrchestrator` (singleton, usando o `ILoggerFactory` do host), o `ScriptInstanceRegistry` (injete-o onde for resolver scripts por handle) e um hosted service que faz a carga inicial no startup e vigia mudanças pelo tempo de vida da aplicação (`ApplicationStopping`). Notas de uso: há um overload com `IServiceProvider` para puxar serviços do container na configuração (ex.: `HttpClient` de `IHttpClientFactory` para o `HttpAssemblyImageSource`); scripts são objetos de vida longa resolvidos do root provider — injete singletons ou um `IServiceScopeFactory` (criando scopes por operação), nunca serviços scoped diretamente; chamadas repetidas de `AddHotReloadScripting` são no-ops; e o pacote não referencia Roslyn — no modo pré-compilado (`ImageSource`) o deploy continua sem compilador.

## Publicação estilo ERP: pré-compilar e distribuir a DLL

O paralelo com as plataformas que inspiraram o design: a Unity compila no editor e o player só carrega binários; o ABAP "ativa" o código no servidor; o ADVPL distribui o RPO compilado. Ninguém interpreta fonte em produção — e aqui é igual: **fonte + reload é o modo de desenvolvimento; produção consome a DLL pré-compilada**, com o mesmo pipeline de hot-swap e preservação de estado.

**Lado do build (uma vez, no servidor de build ou na máquina do dev):**

```csharp
var compiler = new IncrementalScriptCompiler(compilerOptions);
await compiler.AddSourcesFromDirectoryAsync("scripts", cancellationToken: ct);
var result = await compiler.CompileAsync(ct);
if (!result.Success) { /* falhar o build com result.Diagnostics */ }

await result.Image!.WriteToDirectoryAsync("publicacao", ct);   // grava scripts .dll + .pdb
```

> Alternativa igualmente válida: um **csproj comum** referenciando só `Engine.Scripting.Abstractions`. O dev trabalha num projeto normal (IntelliSense, testes, análise), e o `dotnet build` produz a mesma DLL+PDB consumível abaixo.

**Lado da produção/dispositivo (sem Roslyn no deploy):**

```csharp
var options = new HotReloadOptions
{
    ImageSource = new FileSystemAssemblyImageSource(@"C:\app\scripts\scripts.dll"),
    // sem Compiler, sem ScriptsPath — modo pré-compilado
};

await using var orchestrator = new HotReloadOrchestrator(options, loggerFactory);
await orchestrator.StartAsync(ct);
// publicar uma nova scripts.dll por cima → hot-swap com estado preservado
```

Ganhos: startup sem custo de compilação, sem Roslyn (dezenas de MB + memória) no deploy, fonte não distribuído, e o `.pdb` ao lado mantém breakpoints e stack traces legíveis até em produção.

**Distribuição para dispositivos (MAUI) e hosts remotos** — use o `HttpAssemblyImageSource` pronto: o publisher sobe `scripts.dll` + `scripts.pdb` + um manifesto de checksum em qualquer servidor HTTP, e cada host troca a quente no próximo poll:

```csharp
var options = new HotReloadOptions
{
    ImageSource = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
    {
        ImageUrl = new Uri("https://meuservidor.com/scripts/scripts.dll"),
        ChecksumUrl = new Uri("https://meuservidor.com/scripts/scripts.sha256"), // saída de `sha256sum` serve
        CacheDirectory = Path.Combine(FileSystem.AppDataDirectory, "scripts-cache"), // MAUI: boot offline usa o cache
        PollInterval = TimeSpan.FromMinutes(5),
    }, httpClientFactory.CreateClient("scripts")),   // HttpClient próprio opcional
};
```

Semântica de falha: rede fora → serve a última cópia (memória, depois cache em disco) com warning; **hash divergente → `ScriptImageIntegrityException`**, nunca mascarada por fallback — a geração corrente permanece ativa. O `ReloadAsync` manual sempre revalida no servidor com requisição condicional (um 304 não custa corpo), então funciona igualmente com o watching desligado.

## Origens de scripts plugáveis

| Origem | Tipo | Uso |
|--------|------|-----|
| `FileSystemScriptSource` | fonte (texto) | dev loop com editor; trata saves atômicos (write-temp-then-rename), rajadas de eventos e locks transitórios do editor |
| `InMemoryScriptSource` | fonte (texto) | testes determinísticos e referência mínima de implementação custom |
| `FileSystemAssemblyImageSource` | imagem pré-compilada | produção: vigia uma `.dll` (+`.pdb`) e troca a quente quando o publisher copia a nova versão |
| `HttpAssemblyImageSource` | imagem pré-compilada | dispositivos/hosts remotos: baixa a `.dll` (+`.pdb`) de um servidor com polling condicional (ETag → 304 sem corpo), verificação SHA-256 (hash pinado ou manifesto remoto) e cache local offline-first |
| *a sua* (`IScriptSource`) | fonte (texto) | banco de dados, serviço remoto, S3… |

O debounce é do **orchestrator**, não da origem — vale igualmente para rajadas do `FileSystemWatcher` e para tempestades de NOTIFY de banco. Uma origem custom só precisa: carregar documentos e disparar `Changed`.

### Exemplo completo: scripts no PostgreSQL

Esquema com notificação por trigger (`LISTEN/NOTIFY`):

```sql
CREATE TABLE scripts (
    name        text        PRIMARY KEY,
    content     text        NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION notify_script_changed() RETURNS trigger AS $$
BEGIN
    PERFORM pg_notify('script_changed', COALESCE(NEW.name, OLD.name));
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER scripts_changed
AFTER INSERT OR UPDATE OR DELETE ON scripts
FOR EACH ROW EXECUTE FUNCTION notify_script_changed();
```

Implementação de `IScriptSource` com Npgsql (pronta para copiar — a biblioteca não referencia Npgsql de propósito):

```csharp
using Engine.Scripting.Abstractions;
using Npgsql;

public sealed class PostgresScriptSource : IScriptSource
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeSpan _reconnectDelay;
    private CancellationTokenSource? _watchCts;
    private Task? _watchLoop;

    public PostgresScriptSource(NpgsqlDataSource dataSource, TimeSpan? reconnectDelay = null)
    {
        _dataSource = dataSource;
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
    }

    public event EventHandler<ScriptSourceChangedEventArgs>? Changed;

    public async Task<IReadOnlyList<ScriptDocument>> LoadAllAsync(CancellationToken ct)
    {
        var documents = new List<ScriptDocument>();
        await using var command = _dataSource.CreateCommand("SELECT name, content FROM scripts");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            documents.Add(new ScriptDocument(reader.GetString(0), reader.GetString(1)));
        }

        return documents;
    }

    public async Task<ScriptDocument?> LoadAsync(string documentId, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT content FROM scripts WHERE name = $1");
        command.Parameters.AddWithValue(documentId);
        return await command.ExecuteScalarAsync(ct) is string content
            ? new ScriptDocument(documentId, content)
            : null; // linha removida → o pipeline remove o documento da compilação
    }

    public Task StartWatchingAsync(CancellationToken ct)
    {
        _watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watchLoop = WatchLoopAsync(_watchCts.Token);
        return Task.CompletedTask;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await _dataSource.OpenConnectionAsync(ct);
                connection.Notification += (_, e) =>
                    Changed?.Invoke(this, new ScriptSourceChangedEventArgs([e.Payload]));

                await using (var listen = new NpgsqlCommand("LISTEN script_changed", connection))
                {
                    await listen.ExecuteNonQueryAsync(ct);
                }

                while (!ct.IsCancellationRequested)
                {
                    await connection.WaitAsync(ct); // acorda a cada NOTIFY
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // conexão caiu: notificações podem ter se perdido → rescan completo ao religar
                Changed?.Invoke(this, new ScriptSourceChangedEventArgs([], requiresFullRescan: true));
                try { await Task.Delay(_reconnectDelay, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public async Task StopWatchingAsync(CancellationToken ct)
    {
        if (_watchCts is not null)
        {
            await _watchCts.CancelAsync();
        }

        if (_watchLoop is not null)
        {
            try { await _watchLoop; } catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopWatchingAsync(CancellationToken.None);
        _watchCts?.Dispose();
    }
}
```

Uso: `options.Source = new PostgresScriptSource(dataSource); options.Compiler = new IncrementalScriptCompiler(...);` — ou, sem trigger, um loop de polling por `updated_at` disparando `Changed` com os nomes alterados.

## Debugging de scripts (breakpoints funcionam)

Três decisões garantem a experiência de debug:

1. **PDB portátil sempre emitido** e carregado junto (`LoadFromStream(pe, pdb)`) — o runtime registra os símbolos de cada geração.
2. **Fonte embedado no PDB por default** (`ScriptCompilerOptions.EmbedSourcesInPdb`) — o debugger extrai o próprio fonte do PDB, então breakpoints funcionam **até quando o script veio de um banco** e não existe arquivo local.
3. **`OptimizationLevel.Debug` por default** — variáveis inspecionáveis, step-through fiel.

Na prática:

- **VS Code** (extensão C# / C# Dev Kit): F5 no host (ou *attach* ao processo), abra o `.cs` do script, F9 no breakpoint. Quando o script executar, para ali, com locals e step-into normais.
- **Visual Studio / Rider**: `Debug > Attach to Process` no host; mesmo comportamento.
- **Após um reload**, a nova assembly+PDB carregam e o debugger **rebinda os breakpoints sozinho** (o caminho do documento no novo PDB é o mesmo).
- **Modo pré-compilado**: distribua o `.pdb` junto da `.dll` e o debug funciona como em qualquer biblioteca.

Duas observações esperadas: com debugger **anexado**, a coleta do ALC antigo pode atrasar — o `AssemblyUnloadTimedOut` em sessão de debug é um falso-positivo inofensivo; e breakpoints em código de uma geração já descartada ficam obsoletos até o rebind (instantâneo ao salvar).

## Ciclo de vida, estado e as regras do jogo

- **O que migra entre gerações**: valores de tipos carregados **fora** do ALC do script — primitivos, `string`, enums e tipos do host/BCL/Abstractions. Valores de **tipos declarados no próprio script não migram** (a identidade do `Type` morre com a geração): são descartados na captura, com warning e registro em `ScriptStateSnapshot.DiscardedMembers`. Payload de script escondido em campo `object`/coleção de `object` escapa da checagem estática — responsabilidade do consumidor.
- **Statics são resetados a cada reload** (nova assembly ⇒ novos statics) — exatamente o comportamento do domain reload da Unity. Estado que importa vive em campos de instância `[HotReloadState]`.
- **`OnBeforeReloadAsync` é um contrato, não uma cortesia**: dessinscreva eventos do host, cancele timers e background tasks, solte handles nativos. Um script que não faz isso é a causa nº 1 de `AssemblyUnloadTimedOut`.
- **Timeout de unload não aborta o reload**: a nova geração sobe, o vazamento fica limitado à geração presa e resolve sozinho quando a referência morrer. O evento existe para você caçar o culpado (o log lista os suspeitos clássicos).
- **Erro de compilação nunca derruba nada**: `ReloadFailed` com os diagnósticos, geração anterior intacta, próximo save tenta de novo.

## Matriz de plataformas

| Plataforma | Suporte | Observações |
|---|---|---|
| CoreCLR desktop/server (Windows, Linux, macOS) | ✅ Pleno | Cenário primário; é onde a suíte de testes roda |
| ASP.NET Core (APIs) e Blazor **Server** | ✅ Pleno | Caso ideal do source Postgres (regras hot-swappable server-side) |
| MAUI **Windows** | ✅ Pleno | CoreCLR + JIT |
| MAUI **Android** (MonoVM) | ⚠️ Parcial | Carregar/trocar DLL pré-compilada funciona (via `HttpAssemblyImageSource` com cache offline-first); a **coleta** do ALC descarregado não é garantida pelo MonoVM — memória da geração antiga pode ficar retida. Troque com parcimônia e valide no device |
| iOS / MacCatalyst (AOT) | ❌ Não suportado | Sem JIT nem carregamento dinâmico pleno — mesma limitação do IL2CPP da Unity |
| Blazor **WebAssembly** | ❌ Não suportado | Unload de ALC coletável não é confiável no runtime WASM |

## Limitações conhecidas

- **AOT/IL2CPP/trimming**: o mecanismo depende de JIT + carregamento dinâmico de assembly; backends AOT são estruturalmente incompatíveis.
- **Unload cooperativo tem custo**: cada reload força coletas de GC (pausas de ms). Culpados clássicos de timeout: statics segurando tipos/instâncias do script, delegates/closures não dessinscritos, caches de serializadores keyed por `Type` (`JsonSerializer` — prefira um `JsonSerializerOptions` por geração, ou não serialize tipos do script; `TypeDescriptor`; call-site caches do DLR — evite `dynamic` sobre instâncias de script), threads rodando código do script, e debugger anexado.
- **Host single-file/trimmed**: assemblies sem `Assembly.Location` fazem `AddReference(Assembly)` lançar `ScriptingConfigurationException` — use `ReferencePaths` (reference assemblies em disco) ou `ReferenceImages` (bytes embarcados). A TPA também pode não refletir o app single-file.
- **Campos não migráveis**: ver regras acima — tipos do script e payloads opacos em `object` são descartados com warning.
- **Construtores de script**: por default `Activator.CreateInstance` (ctor sem parâmetros); para DI, injete `HotReloadOptions.InstanceFactory`.

## Sample

```
dotnet run --project samples/Engine.Scripting.Sample.ConsoleDemo
```

O demo semeia `scripts/CounterScript.cs`, imprime um tick por segundo e fica vigiando o diretório. Edite a constante `Message` e salve → o texto muda no tick seguinte e o contador `[HotReloadState]` continua de onde estava. Salve um erro de sintaxe → `ReloadFailed` com o diagnóstico e a versão anterior segue rodando. Todos os eventos (start/success/failure/unload-timeout) são impressos com timestamp UTC.

## Estrutura da solução e testes

```
src/       Abstractions · Compilation · Hosting · StatePreservation · Instances · Orchestration · Extensions.Hosting
tests/     Compilation.Tests · Hosting.Tests · StatePreservation.Tests · Orchestration.Tests · Extensions.Hosting.Tests
samples/   Engine.Scripting.Sample.ConsoleDemo
```

Cobertura dos cenários críticos (xUnit v3, padrão `Metodo_Cenario_ResultadoEsperado`): reload preservando `[HotReloadState]`; erro de compilação mantendo a geração anterior + diagnósticos via evento; **10 ciclos consecutivos sem acumular ALCs vivos** (medido por `WeakReference` + GC forçado, via `RetiredGenerationProbes`); campo removido e tipo incompatível ignorados com log; debounce (5 gravações → 1 reload, com `FileSystemWatcher` real e com origem em memória); `AssemblyUnloadTimedOut` com referência forte intencionalmente presa; modo pré-compilado preservando estado; PDB com fonte embedado verificado byte a byte.

Os testes de unload seguem a mesma disciplina da biblioteca: interações com instâncias confinadas em helpers `static` `[MethodImpl(NoInlining)]`, nada de `dynamic` sobre scripts, e testes sensíveis a GC em collection não-paralelizada.

## Roadmap

Melhorias consideradas, em ordem de prioridade — nenhuma é pré-requisito para usar a biblioteca hoje:

| # | Item | Motivação |
|---|------|-----------|
| 1 | **Pacote NuGet + CI/CD** ✅ *feito* — `dotnet pack` gera os 7 pacotes com metadata completa (licença MIT, repositório, autor), README embarcado e símbolos+fontes embedados na DLL (debug sem symbol server); GitHub Actions faz build/test/pack em push-PR e publica no nuget.org por tag `v*` (ver [CI/CD](#cicd)) | Logística de reutilização entre projetos consumidores |
| 2 | **`Engine.Scripting.Extensions.Hosting`** ✅ *feito* — `AddHotReloadScripting(...)` (com overload `IServiceProvider`), hosted service amarrado ao `ApplicationStopping`, scripts com injeção de construtor via `ActivatorUtilities` (coleta do ALC verificada por teste) | Scripts com dependências injetadas (ex.: regra de negócio recebendo repositório no construtor) em hosts ASP.NET Core |
| 3 | **Rollback de geração + validação pré-swap** — ring buffer das últimas N `ScriptAssemblyImage`, validação do header PE antes do teardown, `RollbackAsync()` | Resiliência de produção: hoje, uma imagem corrompida detectada na fase C deixa o host sem geração; com rollback, regra ruim publicada → reversão em segundos |
| 4 | **Gate de execução para swap atômico** (`IScriptExecutionGate`: leitores = chamadas de script, escritor = o swap) | Sob concorrência, hoje `GetAs<T>()` retorna `null` durante a fase B e threads executando script atrasam o unload; o gate fecha as duas lacunas |
| 5 | **Serialização opcional de estado** (`[HotReloadState(Serialize = true)]` / `IStateConverter`) | Migrar valores de **tipos declarados no próprio script** (hoje descartados por identidade de `Type`): serializa na captura, desserializa no tipo homônimo da nova geração — o "partial domain reload" completo, à la Unity |
| 6 | **Analisador de APIs permitidas** — passe Roslyn no compile bloqueando namespaces/APIs configuráveis (`System.IO`, `System.Net`, `Reflection.Emit`, `unsafe`, `DllImport`) com diagnostics `ESC1xxx` | Governança para scripts de terceiros (cenário ERP). É análise estática, não boundary de segurança — a documentar como tal |
| 7 | **`HttpAssemblyImageSource`** ✅ *feito* — polling condicional por ETag, download de dll+pdb, verificação SHA-256 (hash pinado ou manifesto) e cache local offline-first | Distribuição de scripts pré-compilados para dispositivos (MAUI) |
| 8 | **Métricas OpenTelemetry** (`Meter`/`ActivitySource`: duração de compile/unload, timeouts, gerações vivas) | Observabilidade em hosts de produção |
| 9 | **Canário de geração** (`IScriptValidator`: carregar em ALC de teste, sanity check, só então o swap real) | Reduzir a janela de risco de publicações em produção |
| 10 | **Soak test** (100+ ciclos medindo `WorkingSet64` com tolerância) | Pega fragmentação/LOH que o teste de `WeakReference` não vê |
| 11 | **CLI `dotnet tool` de build** (compila a pasta de scripts → dll+pdb publicáveis) | Formalizar o passo de "ativação" do fluxo pré-compilado |

**Fora de escopo por decisão** (armadilhas conhecidas): múltiplas assemblies de script com referências entre si (unload em cascata de ALCs interdependentes — uma geração presa segura a cadeia inteira; use N orchestrators independentes sem cross-referências) e restore de pacotes NuGet em runtime (superfície de segurança enorme; o modo pré-compilado já cobre o caso legítimo).

## CI/CD

| Workflow | Quando | O que faz |
|---|---|---|
| [`ci.yml`](https://github.com/afernandes/Engine.Scripting/blob/main/.github/workflows/ci.yml) | push/PR em `main` | restore → build → **63 testes** → `pack` de validação, com os `.nupkg` como artifact |
| [`release.yml`](https://github.com/afernandes/Engine.Scripting/blob/main/.github/workflows/release.yml) | tag `v*` (ou manual) | valida a versão (SemVer) → build → **testes como gate** → `pack` versionado → publica os 7 pacotes no nuget.org → cria o GitHub Release |

A versão vem da **tag** (o `VersionPrefix` de [`src/Directory.Build.props`](https://github.com/afernandes/Engine.Scripting/blob/main/src/Directory.Build.props)
serve a builds locais), separada em `VersionPrefix`/`VersionSuffix` — assim `v0.2.0-beta.1` publica
como prerelease e cada projeto pode manter um sufixo próprio quando precisar.

**Para publicar:**

1. **Trusted Publishing** (já configurado): a publicação usa **OIDC** — o workflow troca o token do
   GitHub Actions por uma API key **efêmera** via [`NuGet/login`](https://github.com/NuGet/login),
   sem nenhum secret de longa duração para rotacionar. Exige a policy em *nuget.org → Trusted
   Publishing* (Package owner + este repositório + `release.yml`) e `id-token: write` no workflow.
   O usuário do nuget.org fica em `env.NUGET_USER`.
2. `git tag v0.1.1 && git push origin v0.1.1`.
3. Para ensaiar sem publicar: *Actions → Release → Run workflow* com `dry_run` marcado.

> Os testes de unload são sensíveis a GC (`WeakReference` + coleta forçada, collection não-paralelizada),
> por isso a suíte roda inteira num único job.

## Decisão de idioma

Esta biblioteca é infraestrutura pura, sem domínio de negócio — **todo o código, API pública e mensagens estão em inglês**, decisão consciente alinhada à convenção da stack (building blocks técnicos em inglês; identificadores de domínio em português ficam para as bibliotecas que tenham domínio). Os nomes dos cenários de teste usam português no padrão `Metodo_Cenario_ResultadoEsperado`, como no restante da stack.

## Licença

[MIT](https://github.com/afernandes/Engine.Scripting/blob/main/LICENSE).

## Referências

- [Unloadability no .NET (`AssemblyLoadContext` coletável)](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)
- [`AssemblyLoadContext.Unload`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext.unload)
- [Unity: Path to CoreCLR](https://discussions.unity.com/t/path-to-coreclr-2026-upgrade-guide/1714279)
- [Caches internos retendo `Type`s como causa real de vazamento (dotnet/coreclr#26271)](https://github.com/dotnet/coreclr/issues/26271)
- [Collectible assemblies e custo de memória](https://www.strathweb.com/2019/01/collectible-assemblies-in-net-core-3-0/)
