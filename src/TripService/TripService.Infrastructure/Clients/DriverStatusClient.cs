using System.Net;
using System.Net.Http.Json;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Clients;

public sealed class DriverStatusClient(HttpClient httpClient) : IDriverStatusClient
{
    public async Task SetStatusAsync(
        Guid driverId,
        DriverAvailabilityStatus status,
        CancellationToken cancellationToken)
    {
        if (driverId == Guid.Empty)
            throw new ValidationException("DriverId không hợp lệ.");

        if (!Enum.IsDefined(status))
            throw new ValidationException("Trạng thái tài xế không hợp lệ.");

        cancellationToken.ThrowIfCancellationRequested();

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/drivers/{driverId:D}/status")
        {
            Content = JsonContent.Create(new
            {
                status = status.ToString()
            })
        };

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new NotFoundException(
                    "DRIVER_NOT_FOUND",
                    "Không tìm thấy tài xế bên Matching.");
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new DownstreamException(
                    502,
                    "MATCHING_UNAVAILABLE",
                    "Matching đang gặp lỗi, vui lòng thử lại.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DownstreamException(
                    502,
                    "MATCHING_REQUEST_REJECTED",
                    "Matching từ chối yêu cầu cập nhật tài xế.");
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownstreamException(
                504,
                "MATCHING_TIMEOUT",
                "Matching không phản hồi trong thời gian cho phép.");
        }
        catch (HttpRequestException)
        {
            throw new DownstreamException(
                502,
                "MATCHING_UNAVAILABLE",
                "Không thể kết nối đến Matching.");
        }
    }
}
