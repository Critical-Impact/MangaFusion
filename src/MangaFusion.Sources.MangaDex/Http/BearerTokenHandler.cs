using System.Net.Http.Headers;
using MangaFusion.Sources.MangaDex.Auth;

namespace MangaFusion.Sources.MangaDex.Http;

/// <summary>Attaches the cached MangaDex access token when one is available. Depends only on the
/// singleton token provider, so it is safe to be pooled/reused by HttpClientFactory.</summary>
public sealed class BearerTokenHandler(MangaDexTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
