using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DeepWiki.Web.Tests.Fixtures;

/// <summary>
/// A test double for <see cref="HttpMessageHandler"/> that delegates the
/// send logic to a caller-supplied function, making it easy to simulate
/// any HTTP response without a real network connection.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _responder(request, cancellationToken);
}
