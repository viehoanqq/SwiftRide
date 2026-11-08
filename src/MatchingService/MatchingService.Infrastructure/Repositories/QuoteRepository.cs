using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Infrastructure.Mongo;

namespace MatchingService.Infrastructure.Repositories;

public class QuoteRepository(MongoContext context) : IQuoteRepository
{
    public Task AddAsync(Quote quote)
    {
        var document = new QuoteDocument
        {
            Id = quote.Id,
            TripId = quote.TripId,
            DistanceKm = quote.DistanceKm,
            DurationMin = quote.DurationMin,
            Strategy = quote.Strategy,
            Price = quote.Price,
            CreatedAt = quote.CreatedAt,
            Params = new PricingParams
            {
                Cd = quote.Params.Cd,
                Ct = quote.Params.Ct,
                S = quote.Params.S,
                Cm = quote.Params.Cm,
                Eta = quote.Params.Eta,
                Tax = quote.Params.Tax
            }
        };

        return context.Quotes.InsertOneAsync(document);
    }
}