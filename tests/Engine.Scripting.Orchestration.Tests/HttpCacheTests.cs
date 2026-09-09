using System.Net;
using System.Security.Cryptography;
using Engine.Scripting.Orchestration.Sources;

namespace Engine.Scripting.Orchestration.Tests;

public sealed class HttpCacheTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadImageAsync_ChamadasSobrepostas_ApenasUmaAquisicaoEImagemConsistente()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var active = 0;
        var maximumActive = 0;
        var handler = StubHttpMessageHandler.CreateAsync(async _ =>
        {
            Interlocked.Increment(ref calls);
            var current = Interlocked.Increment(ref active);
            while (current > Volatile.Read(ref maximumActive)
                && Interlocked.CompareExchange(ref maximumActive, current, Volatile.Read(ref maximumActive)) < current)
            {
            }
            entered.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref active);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent([1, 2, 3]) };
        });
        await using var source = new HttpAssemblyImageSource(
            new HttpAssemblyImageSourceOptions { ImageUrl = new Uri("https://example.test/scripts.dll"), DownloadSymbols = false },
            new HttpClient(handler));

        var first = source.LoadImageAsync(Token);
        await entered.Task;
        var second = source.LoadImageAsync(Token);
        await Task.Delay(20, Token);
        Assert.Equal(1, calls);
        Assert.Equal(1, maximumActive);
        release.SetResult();

        var images = await Task.WhenAll(first, second);
        Assert.Equal(new byte[] { 1, 2, 3 }, images[0]!.PeBytes);
        Assert.Equal(images[0]!.PeBytes, images[1]!.PeBytes);
        Assert.Equal(2, calls);
        Assert.Equal(1, maximumActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadImageAsync_CacheComHashDiferente_Rejeita(bool tamper)
    {
        using var directory = new TempScriptDirectory();
        byte[] bytes = [1, 2, 3];
        var options = Options(directory.Path, "one");
        using var online = Client(bytes);
        await using (var source = new HttpAssemblyImageSource(options, online))
            Assert.NotNull(await source.LoadImageAsync(Token));
        if (tamper)
        {
            var path = Assert.Single(Directory.GetFiles(directory.Path, "*.script-cache"));
            var content = await File.ReadAllTextAsync(path, Token);
            await File.WriteAllTextAsync(path, content.Replace("AQID", "BAUG", StringComparison.Ordinal), Token);
        }
        using var offline = Offline();
        await using var cached = new HttpAssemblyImageSource(new HttpAssemblyImageSourceOptions
        {
            ImageUrl = options.ImageUrl,
            CacheDirectory = directory.Path,
            DownloadSymbols = false,
            ExpectedSha256 = Convert.ToHexString(SHA256.HashData(tamper ? bytes : [9, 9, 9])),
        }, offline);
        await Assert.ThrowsAsync<ScriptImageIntegrityException>(() => cached.LoadImageAsync(Token));
    }

    [Fact]
    public async Task LoadImageAsync_DuasOrigensNaMesmaPasta_MantemCachesSeparados()
    {
        using var directory = new TempScriptDirectory();
        foreach (var name in new[] { "one", "two" })
        {
            using var online = Client(name == "one" ? [1] : [2]);
            await using var source = new HttpAssemblyImageSource(Options(directory.Path, name), online);
            await source.LoadImageAsync(Token);
        }
        using var offline = Offline();
        foreach (var name in new[] { "one", "two" })
        {
            await using var source = new HttpAssemblyImageSource(Options(directory.Path, name), offline);
            Assert.Equal(name == "one" ? new byte[] { 1 } : new byte[] { 2 },
                (await source.LoadImageAsync(Token))!.PeBytes);
        }
        Assert.Equal(2, Directory.GetFiles(directory.Path, "*.script-cache").Length);
    }

    [Fact]
    public async Task LoadImageAsync_Cancelamento_PreservaCacheAnterior()
    {
        using var directory = new TempScriptDirectory();
        var options = Options(directory.Path, "one");
        using (var online = Client([1]))
        {
            await using var source = new HttpAssemblyImageSource(options, online);
            await source.LoadImageAsync(Token);
        }
        var path = Assert.Single(Directory.GetFiles(directory.Path, "*.script-cache"));
        var original = await File.ReadAllBytesAsync(path, Token);
        using (var online = Client([2]))
        {
            await using var source = new HttpAssemblyImageSource(options, online);
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LoadImageAsync(cancelled.Token));
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path, Token));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static HttpAssemblyImageSourceOptions Options(string directory, string name) => new()
    {
        ImageUrl = new Uri("https://example.test/" + name + "/scripts.dll"),
        CacheDirectory = directory,
        DownloadSymbols = false,
    };

    [Fact]
    public async Task LoadImageAsync_ArquivoDeCacheBloqueado_PreservaUltimaImagem()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("File sharing delete protection is specific to Windows.");
        using var directory = new TempScriptDirectory();
        var options = Options(directory.Path, "one");
        using (var online = Client([1]))
        {
            await using var source = new HttpAssemblyImageSource(options, online);
            await source.LoadImageAsync(Token);
        }
        var path = Assert.Single(Directory.GetFiles(directory.Path, "*.script-cache"));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var online = Client([2]))
        {
            await using var source = new HttpAssemblyImageSource(options, online);
            Assert.Equal(new byte[] { 2 }, (await source.LoadImageAsync(Token))!.PeBytes);
        }
        using var offline = Offline();
        await using var cached = new HttpAssemblyImageSource(options, offline);
        Assert.Equal(new byte[] { 1 }, (await cached.LoadImageAsync(Token))!.PeBytes);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static HttpClient Client(byte[] bytes) => new(new StubHttpMessageHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));

    private static HttpClient Offline() => new(new StubHttpMessageHandler(_ =>
        throw new HttpRequestException("offline")));
}
