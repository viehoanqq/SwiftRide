namespace PaymentService.Application.Interfaces;

public interface IUnitOfWork
{
    // One shared scoped DbContext for all repositories. Execute operation, SaveChanges, commit;
    // rollback on error. No nested transaction, HTTP call, or automatic callback replay.
    // Unique-key races must rollback then reread the committed result with a fresh tracking state.
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
