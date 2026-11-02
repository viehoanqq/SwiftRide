using MatchingService.Domain.Entities;

namespace MatchingService.Application.Interfaces;

public interface IQuoteRepository
{
    // Minh – POST /quote: lưu kết quả báo giá
    Task AddAsync(Quote quote);
}
