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

    public Task<QuoteResult?> GetQuoteAsync(Guid tripId, GeoPointDto pickup, GeoPointDto dropoff) =>
        PostJsonAsync<QuoteResult>("/quote", new { tripId, pickup, dropoff });

    public Task<MatchDriverResult?> MatchAsync(MatchDriverRequest request) =>
        PostJsonAsync<MatchDriverResult>("/match", request);

    private async Task<TResponse?> PostJsonAsync<TResponse>(string path, object payload)
    {
        const int maxAttempts = 3;
        using var totalTimeout = new CancellationTokenSource(TotalTimeout);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
            attemptTimeout.CancelAfter(AttemptTimeout);
            using var request = CreateRequest(path, payload);

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
                await DelayBeforeRetryAsync(attempt, totalTimeout.Token);
                continue;
            }
            catch (OperationCanceledException) when (attempt < maxAttempts && !totalTimeout.IsCancellationRequested)
            {
                await DelayBeforeRetryAsync(attempt, totalTimeout.Token);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, totalTimeout.Token);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                try
                {
                    var result = await response.Content.ReadFromJsonAsync<TResponse>(
                        cancellationToken: attemptTimeout.Token);
                    return result ?? throw new InvalidDataException(
                        $"Matching service trả về response rỗng cho {path} thành công.");
                }
                catch (OperationCanceledException) when (attempt < maxAttempts && !totalTimeout.IsCancellationRequested)
                {
                    await DelayBeforeRetryAsync(attempt, totalTimeout.Token);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(
                        $"Matching service trả về JSON không hợp lệ cho {path}.", ex);
                }
            }
        }

        throw new HttpRequestException($"Matching service không phản hồi {path} sau khi thử lại.");
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private HttpRequestMessage CreateRequest(string path, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
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
