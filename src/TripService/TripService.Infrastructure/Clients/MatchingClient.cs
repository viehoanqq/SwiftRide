using System.Net;
using System.Net.Http.Json;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public sealed class MatchingClient(HttpClient httpClient) : IMatchingClient
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<QuoteResult?> GetQuoteAsync(Guid tripId, GeoPointDto pickup, GeoPointDto dropoff)
    {
        _httpClient.Timeout = TimeSpan.FromSeconds(3);

        var payload = new
        {
            tripId,
            pickup,
            dropoff
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync("/quote", payload);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var quote = await response.Content.ReadFromJsonAsync<QuoteResult>();
                return quote;
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (TaskCanceledException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        throw new HttpRequestException("The Matching service did not respond successfully after retries.");
    }
}
