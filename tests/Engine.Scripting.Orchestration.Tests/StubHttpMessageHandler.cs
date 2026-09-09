namespace Engine.Scripting.Orchestration.Tests;

/// <summary>In-process HTTP stub: routes every request through a synchronous responder.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _asyncResponder;
    private int _requestCount;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    private StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _asyncResponder = responder;
        _responder = _ => throw new InvalidOperationException("The async responder must be used asynchronously.");
    }

    public static StubHttpMessageHandler CreateAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        => new(responder);

    public int RequestCount => Volatile.Read(ref _requestCount);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        return _asyncResponder is null
            ? Task.FromResult(_responder(request))
            : _asyncResponder(request);
    }
}
