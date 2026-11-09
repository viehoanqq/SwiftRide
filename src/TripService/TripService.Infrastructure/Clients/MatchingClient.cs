using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public class MatchingClient(HttpClient httpClient, IHttpContextAccessor contextAccessor) : IMatchingClient
{
    public Task<QuoteResult?> GetQuoteAsync(Guid tripId, GeoPointDto pickup, GeoPointDto dropoff) =>
        // Quote writes data: do not retry without an idempotency key.
        PostAsync<QuoteResult>("/quote", new { tripId, pickup, dropoff }, 1);

    public Task<MatchDriverResult?> MatchAsync(MatchDriverRequest request) =>
        PostAsync<MatchDriverResult>("/match", request, 3);

    private async Task<T?> PostAsync<T>(string path, object payload, int attempts)
    {
        var caller = contextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        using var total = CancellationTokenSource.CreateLinkedTokenSource(caller);
        total.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(attempts == 1 ? 3 : 1));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, path)
                    {
                        Content = JsonContent.Create(payload)
                    };
                    using var response = await httpClient.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        var error = await response.Content.ReadFromJsonAsync<MatchingError>(cancellationToken: timeout.Token);
                        if (error?.Code == "NO_DRIVER_AVAILABLE") return default;
                    }
                    if ((int)response.StatusCode >= 500 && attempt < attempts)
                    {
                        await Task.Delay(100 * attempt, total.Token);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                        throw new DownstreamException(502, "MATCHING_REQUEST_FAILED",
                            $"Matching trả về HTTP {(int)response.StatusCode} cho {path}.");

                    return await response.Content.ReadFromJsonAsync<T>(cancellationToken: timeout.Token)
                        ?? throw new DownstreamException(502, "MATCHING_INVALID_RESPONSE", "Matching trả về dữ liệu rỗng.");
                }
                catch (HttpRequestException) when (attempt < attempts && !total.IsCancellationRequested)
                { await Task.Delay(100 * attempt, total.Token); }
                catch (OperationCanceledException) when (attempt < attempts && !total.IsCancellationRequested)
                { await Task.Delay(100 * attempt, total.Token); }
            }
            throw new DownstreamException(502, "MATCHING_UNAVAILABLE", "Không thể gọi Matching.");
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        { throw new DownstreamException(504, "MATCHING_TIMEOUT", "Matching không phản hồi trong thời gian cho phép."); }
        catch (HttpRequestException)
        { throw new DownstreamException(502, "MATCHING_UNAVAILABLE", "Không thể kết nối Matching."); }
        catch (JsonException)
        { throw new DownstreamException(502, "MATCHING_INVALID_RESPONSE", "Matching trả về JSON không hợp lệ."); }
        catch (NotSupportedException)
        { throw new DownstreamException(502, "MATCHING_INVALID_RESPONSE", "Định dạng phản hồi Matching không hợp lệ."); }
    }

    private sealed record MatchingError(string Code);
}
