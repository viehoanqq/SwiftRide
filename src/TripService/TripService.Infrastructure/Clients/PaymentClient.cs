using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SwiftRide.Contracts.Payments;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public sealed class PaymentClient(HttpClient httpClient) : IPaymentClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    public async Task<CreatePaymentResponse> CreateAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TripId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ValidationException("Thông tin yêu cầu thanh toán không hợp lệ.");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.TryAddWithoutValidation(PaymentProtocol.IdempotencyHeader, request.IdempotencyKey);

        return await SendAsync(httpRequest, cancellationToken, async (response, ct) =>
        {
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
            {
                throw new DownstreamException(
                    502,
                    PaymentErrorCodes.PaymentUnavailable,
                    $"Payment trả về HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<CreatePaymentResponse>(cancellationToken: ct);
            if (body is null || body.PaymentId == Guid.Empty || !Enum.IsDefined(body.Status))
            {
                throw new DownstreamException(
                    502,
                    PaymentErrorCodes.InvalidPaymentResponse,
                    "Payment trả về dữ liệu không hợp lệ.");
            }

            return body;
        });
    }

    public async Task<PaymentDetailResponse?> GetByTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty)
            throw new ValidationException("TripId không hợp lệ.");

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/payments/by-trip/{tripId:D}");

        return await SendAsync<PaymentDetailResponse?>(httpRequest, cancellationToken, async (response, ct) =>
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new DownstreamException(
                    502,
                    PaymentErrorCodes.PaymentUnavailable,
                    $"Payment trả về HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<PaymentDetailResponse>(cancellationToken: ct);
            if (body is null || body.Id == Guid.Empty || body.TripId != tripId || !Enum.IsDefined(body.Status))
            {
                throw new DownstreamException(
                    502,
                    PaymentErrorCodes.InvalidPaymentResponse,
                    "Payment trả về dữ liệu không hợp lệ.");
            }

            return body;
        });
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage httpRequest,
        CancellationToken cancellationToken,
        Func<HttpResponseMessage, CancellationToken, Task<T>> handleResponse)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            return await handleResponse(response, timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownstreamException(
                504,
                PaymentErrorCodes.PaymentTimeout,
                "Payment không phản hồi trong thời gian cho phép.");
        }
        catch (HttpRequestException)
        {
            throw new DownstreamException(
                502,
                PaymentErrorCodes.PaymentUnavailable,
                "Không thể kết nối đến Payment.");
        }
        catch (JsonException)
        {
            throw new DownstreamException(
                502,
                PaymentErrorCodes.InvalidPaymentResponse,
                "Payment trả về JSON không hợp lệ.");
        }
        catch (NotSupportedException)
        {
            throw new DownstreamException(
                502,
                PaymentErrorCodes.InvalidPaymentResponse,
                "Định dạng phản hồi Payment không hợp lệ.");
        }
    }
}
