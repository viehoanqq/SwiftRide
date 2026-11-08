using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public class MatchingClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor) : IMatchingClient
{
    public async Task<QuoteResult?> GetQuoteAsync(Guid tripId, GeoPointDto pickup, GeoPointDto dropoff)
    {
        var payload = new
        {
            tripId,
            pickup,
            dropoff
        };

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/quote")
                {
                    Content = JsonContent.Create(payload)
                };

                var authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader))
                {
                    if (AuthenticationHeaderValue.TryParse(authHeader, out var parsedAuth))
                    {
                        request.Headers.Authorization = parsedAuth;
                    }
                    else
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                    }
                }

                using var response = await httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
                    continue;
                }

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<QuoteResult>();
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException) && attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        throw new HttpRequestException("Matching service không phản hồi sau khi thử lại nhiều lần.");
    }
}
