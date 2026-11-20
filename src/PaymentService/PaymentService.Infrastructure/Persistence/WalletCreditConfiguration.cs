using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence;

public sealed class WalletCreditConfiguration : IEntityTypeConfiguration<WalletCredit>
{
    public void Configure(EntityTypeBuilder<WalletCredit> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        // Amount is the signed balance adjustment; existing credit history is preserved.
        builder.ToTable("wallet_credits", t => t.HasCheckConstraint("ck_wallet_credit_amount", "amount <> 0"));
    }
}
