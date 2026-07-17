using System.Net;
using System.Text;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.MangaDex.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.MangaDex;

public class MangaDexTokenProviderTests
{
    [Fact]
    public async Task Caches_access_token_across_calls()
    {
        var handler = new QueuingHandler();
        handler.Enqueue(TokenJson("acc1", "ref1", expiresIn: 900));
        var provider = Build(handler, Credentials());

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("acc1", first);
        Assert.Equal("acc1", second);
        Assert.Equal(1, handler.Calls); // token is cached — only one auth request
    }

    [Fact]
    public async Task Returns_null_and_makes_no_request_without_credentials()
    {
        var handler = new QueuingHandler();
        var provider = Build(handler, credentials: null);

        Assert.Null(await provider.GetAccessTokenAsync());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ValidateStored_true_on_success()
    {
        var handler = new QueuingHandler();
        handler.Enqueue(TokenJson("acc", "ref", expiresIn: 900));
        var provider = Build(handler, Credentials());

        Assert.True(await provider.ValidateStoredAsync());
    }

    [Fact]
    public async Task ValidateStored_false_on_auth_failure()
    {
        var handler = new QueuingHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = Build(handler, Credentials());

        Assert.False(await provider.ValidateStoredAsync());
    }

    private static MangaDexTokenProvider Build(HttpMessageHandler handler, IReadOnlyDictionary<string, string>? credentials) =>
        new(new FakeScopeFactory(new FakeCredentialStore(credentials)),
            new SingleClientFactory(handler),
            NullLogger<MangaDexTokenProvider>.Instance);

    private static Dictionary<string, string> Credentials() => new()
    {
        ["clientId"] = "c",
        ["clientSecret"] = "s",
        ["username"] = "u",
        ["password"] = "p",
    };

    private static HttpResponseMessage TokenJson(string access, string refresh, int expiresIn) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"access_token":"{{access}}","refresh_token":"{{refresh}}","expires_in":{{expiresIn}}}""",
            Encoding.UTF8, "application/json"),
    };

    private sealed class QueuingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public int Calls { get; private set; }

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeCredentialStore(IReadOnlyDictionary<string, string>? credentials) : ISourceCredentialStore
    {
        public Task<IReadOnlyDictionary<string, string>?> GetAsync(string sourceId, CancellationToken ct = default) =>
            Task.FromResult(credentials);

        public Task SetAsync(string sourceId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string sourceId, CancellationToken ct = default) =>
            Task.FromResult(credentials is not null);

        public Task DeleteAsync(string sourceId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeScopeFactory(ISourceCredentialStore store) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(store);

        private sealed class Scope(ISourceCredentialStore store) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Provider(store);
            public void Dispose() { }
        }

        private sealed class Provider(ISourceCredentialStore store) : IServiceProvider
        {
            public object? GetService(Type serviceType) =>
                serviceType == typeof(ISourceCredentialStore) ? store : null;
        }
    }
}
