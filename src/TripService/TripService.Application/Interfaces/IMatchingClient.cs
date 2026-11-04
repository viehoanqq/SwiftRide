using TripService.Application.DTOs;

namespace TripService.Application.Interfaces;

public interface IMatchingClient
{
    // Gọi POST /quote bên Matching. Trả null khi Matching báo 404 (không có tài xế gần)
    Task<QuoteResult?> GetQuoteAsync(Guid tripId, GeoPointDto pickup, GeoPointDto dropoff);
}
