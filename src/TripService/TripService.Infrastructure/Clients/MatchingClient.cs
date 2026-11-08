using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public class MatchingClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor) : IMatchingClient
{
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(1);

    public async Task<QuoteResult?> GetQuoteAsync(Guid tripId, GeoPointDto pickup, GeoPointDto dropoff)
    {
        var payload = new
        {
            tripId,
            pickup,
            dropoff
        };

        const int maxAttempts = 3;
        using var totalTimeout = new CancellationTokenSource(TotalTimeout);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
            attemptTimeout.CancelAfter(AttemptTimeout);

            using var request = CreateQuoteRequest(payload);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptTimeout.Token);
            }
            catch (HttpRequestException) when (attempt < maxAttempts && !totalTimeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), totalTimeout.Token);
                continue;
            }
            catch (OperationCanceledException) when (attempt < maxAttempts && !totalTimeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), totalTimeout.Token);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), totalTimeout.Token);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                QuoteResult? quote;
                try
                {
                    quote = await response.Content.ReadFromJsonAsync<QuoteResult>(cancellationToken: attemptTimeout.Token);
                }
                catch (OperationCanceledException) when (attempt < maxAttempts && !totalTimeout.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), totalTimeout.Token);
                    continue;
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("Matching service trả về JSON quote không hợp lệ.", ex);
                }

                return quote ?? throw new InvalidDataException("Matching service trả về response rỗng cho quote thành công.");
            }
        }

        throw new InvalidOperationException("Matching retry loop kết thúc ngoài dự kiến.");
    }

    private HttpRequestMessage CreateQuoteRequest(object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/quote")
        {
            Content = JsonContent.Create(payload)
        };

        ForwardAuthorizationHeader(request);
        return request;
    }

    private void ForwardAuthorizationHeader(HttpRequestMessage request)
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return;
        }

        if (AuthenticationHeaderValue.TryParse(authorization, out var parsedAuthorization))
        {
            request.Headers.Authorization = parsedAuthorization;
            return;
        }

        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }
}
