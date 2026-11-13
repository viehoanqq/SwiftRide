using System.Net;
using System.Net.Http.Json;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public sealed class PaymentClient(HttpClient httpClient) : IPaymentClient
{
    public async Task<PaymentResponse?> ProcessTripPaymentAsync(Guid tripId, Guid riderId, Guid driverId, decimal amount, string method, string? cardToken = null)
    {
        var payload = new
        {
            tripId,
            riderId,
            driverId,
            amount,
            method,
            cardToken
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync("/payments", payload);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<PaymentResponse>();
                return result;
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
            catch (TaskCanceledException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }

        throw new HttpRequestException("The Payment service did not respond successfully after retries.");
    }
}
