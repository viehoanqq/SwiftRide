using Microsoft.AspNetCore.Http;

namespace TripService.Infrastructure.Clients;

// Attach only to trusted internal HttpClients. Never forward cookies, Host or all headers.
public sealed class ForwardRequestHeadersHandler(IHttpContextAccessor contextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = contextAccessor.HttpContext?.Request.Headers;
        if (incoming is not null)
        {
            foreach (var name in new[] { "Authorization", "X-Correlation-ID" })
            {
                if (!request.Headers.Contains(name) && incoming.TryGetValue(name, out var values))
                    request.Headers.TryAddWithoutValidation(name, values.ToArray());
            }
        }
        return base.SendAsync(request, cancellationToken);
    }
}
