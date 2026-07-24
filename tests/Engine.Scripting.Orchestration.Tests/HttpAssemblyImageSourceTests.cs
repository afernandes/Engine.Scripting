using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Engine.Scripting.Orchestration.Sources;
using static Engine.Scripting.Orchestration.Tests.OrchestratorTestHelpers;

namespace Engine.Scripting.Orchestration.Tests;

[Collection("UnloadSensitive")]
public class HttpAssemblyImageSourceTests
{
    private static readonly Uri ImageUrl = new("https://scripts.example/releases/scripts.dll");

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadImageAsync_ServidorPublicouImagem_BaixaComSimbolosEValidaHashPinado()
    {
        var image = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".pdb", StringComparison.Ordinal)
            ? BytesResponse(image.PdbBytes!)
            : ImageResponse(image.PeBytes, "\"v1\""));
        await using var source = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = ImageUrl,
            ExpectedSha256 = Convert.ToHexString(SHA256.HashData(image.PeBytes)),
        }, new HttpClient(handler));

        var loaded = await source.LoadImageAsync(TestToken);

        Assert.NotNull(loaded);
        Assert.Equal("scripts", loaded.AssemblyName);
        Assert.Equal(image.PeBytes, loaded.PeBytes);
        Assert.NotNull(loaded.PdbBytes);
        Assert.Equal(image.PdbBytes, loaded.PdbBytes);
    }

    [Fact]
    public async Task LoadImageAsync_HashNaoConfere_LancaScriptImageIntegrityException()
    {
        var image = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        var handler = new StubHttpMessageHandler(_ => ImageResponse(image.PeBytes, "\"v1\""));
        await using var source = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = ImageUrl,
            ExpectedSha256 = new string('0', 64),
            DownloadSymbols = false,
        }, new HttpClient(handler));

        await Assert.ThrowsAsync<ScriptImageIntegrityException>(() => source.LoadImageAsync(TestToken));
    }

    [Fact]
    public async Task LoadImageAsync_ChecksumUrlComManifestoEstiloSha256sum_Valida()
    {
        var image = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        var manifest = $"{Convert.ToHexString(SHA256.HashData(image.PeBytes)).ToLowerInvariant()}  scripts.dll\n";
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? TextResponse(manifest)
            : ImageResponse(image.PeBytes, "\"v1\""));
        await using var source = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = ImageUrl,
            ChecksumUrl = new Uri("https://scripts.example/releases/scripts.sha256"),
            DownloadSymbols = false,
        }, new HttpClient(handler));

        var loaded = await source.LoadImageAsync(TestToken);

        Assert.NotNull(loaded);
        Assert.Equal(image.PeBytes, loaded.PeBytes);
    }

    [Fact]
    public async Task LoadImageAsync_SemImagemNoServidor_RetornaNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var source = new HttpAssemblyImageSource(
            new HttpAssemblyImageSourceOptions { ImageUrl = ImageUrl, DownloadSymbols = false },
            new HttpClient(handler));

        Assert.Null(await source.LoadImageAsync(TestToken));
    }

    [Fact]
    public async Task LoadImageAsync_EtagInalterado_Recebe304EReutilizaImagemEmMemoria()
    {
        var image = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        var handler = new StubHttpMessageHandler(request =>
            request.Headers.IfNoneMatch.Any(tag => tag.Tag == "\"v1\"")
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : ImageResponse(image.PeBytes, "\"v1\""));
        await using var source = new HttpAssemblyImageSource(
            new HttpAssemblyImageSourceOptions { ImageUrl = ImageUrl, DownloadSymbols = false },
            new HttpClient(handler));

        var first = await source.LoadImageAsync(TestToken);
        var second = await source.LoadImageAsync(TestToken);

        Assert.NotNull(first);
        Assert.Same(first, second); // 304 → same in-memory instance, no re-download
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task WatchLoop_NovaVersaoPublicada_DisparaChangedELoadRetornaANova()
    {
        var imageV1 = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        var imageV2 = await CompileImageAsync(ScriptSources.CounterScript("v2"), TestToken);
        var gate = new Lock();
        var currentVersion = 1;

        var handler = new StubHttpMessageHandler(request =>
        {
            lock (gate)
            {
                var etag = $"\"v{currentVersion}\"";
                if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                return ImageResponse(currentVersion == 1 ? imageV1.PeBytes : imageV2.PeBytes, etag);
            }
        });

        await using var source = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = ImageUrl,
            DownloadSymbols = false,
            PollInterval = TimeSpan.FromMilliseconds(50),
        }, new HttpClient(handler));
        var changedRecorder = new EventRecorder<EventArgs>();
        source.Changed += (_, e) => changedRecorder.Record(e);

        var initial = await source.LoadImageAsync(TestToken);
        await source.StartWatchingAsync(TestToken);

        // A few unchanged polls must not raise Changed.
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestToken);
        Assert.Equal(0, changedRecorder.Count);

        lock (gate)
        {
            currentVersion = 2;
        }

        await changedRecorder.WaitForCountAsync(1, TimeSpan.FromSeconds(10), TestToken);
        var updated = await source.LoadImageAsync(TestToken);

        Assert.NotNull(initial);
        Assert.Equal(imageV1.PeBytes, initial.PeBytes);
        Assert.NotNull(updated);
        Assert.Equal(imageV2.PeBytes, updated.PeBytes);
        await source.StopWatchingAsync(TestToken);
    }

    [Fact]
    public async Task LoadImageAsync_RedeForaComCacheLocalPopulado_ServeDoCache()
    {
        using var cacheDirectory = new TempScriptDirectory();
        var image = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);

        var onlineHandler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".pdb", StringComparison.Ordinal)
            ? BytesResponse(image.PdbBytes!)
            : ImageResponse(image.PeBytes, "\"v1\""));
        await using (var onlineSource = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = ImageUrl,
            CacheDirectory = cacheDirectory.Path,
        }, new HttpClient(onlineHandler)))
        {
            Assert.NotNull(await onlineSource.LoadImageAsync(TestToken)); // populates the cache
        }

        var offlineHandler = new StubHttpMessageHandler(_ => throw new HttpRequestException("network is down"));
        await using var offlineSource = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = ImageUrl,
            CacheDirectory = cacheDirectory.Path,
        }, new HttpClient(offlineHandler));

        var served = await offlineSource.LoadImageAsync(TestToken);

        Assert.NotNull(served);
        Assert.Equal(image.PeBytes, served.PeBytes);
        Assert.NotNull(served.PdbBytes); // cached pdb came along
    }

    [Fact]
    public async Task LoadImageAsync_RedeForaSemCache_PropagaErroDeRede()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("network is down"));
        await using var source = new HttpAssemblyImageSource(
            new HttpAssemblyImageSourceOptions { ImageUrl = ImageUrl, DownloadSymbols = false },
            new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => source.LoadImageAsync(TestToken));
    }

    [Fact]
    public async Task ImageSourceHttp_NovaVersaoNoServidor_OrchestratorRecarregaPreservandoEstado()
    {
        var imageV1 = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        var imageV2 = await CompileImageAsync(ScriptSources.CounterScript("v2"), TestToken);
        var gate = new Lock();
        var currentVersion = 1;

        var handler = new StubHttpMessageHandler(request =>
        {
            lock (gate)
            {
                var etag = $"\"v{currentVersion}\"";
                if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                return ImageResponse(currentVersion == 1 ? imageV1.PeBytes : imageV2.PeBytes, etag);
            }
        });

        var options = new HotReloadOptions
        {
            ImageSource = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
            {
                ImageUrl = ImageUrl,
                DownloadSymbols = false,
            }, new HttpClient(handler)),
            EnableSourceWatching = false,
        };
        ApplyFastUnload(options.Hosting);

        await using var orchestrator = new HotReloadOrchestrator(options);
        await orchestrator.StartAsync(TestToken);

        var handle = Assert.Single(orchestrator.Registry.Handles);
        IncrementScript(orchestrator.Registry, handle);
        IncrementScript(orchestrator.Registry, handle);

        lock (gate)
        {
            currentVersion = 2;
        }

        var result = await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
        Assert.Equal("v2:2", DescribeScript(orchestrator.Registry, handle));
    }

    private static HttpResponseMessage ImageResponse(byte[] peBytes, string etag)
    {
        var response = BytesResponse(peBytes);
        response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private static HttpResponseMessage BytesResponse(byte[] bytes)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static HttpResponseMessage TextResponse(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text) };
}
