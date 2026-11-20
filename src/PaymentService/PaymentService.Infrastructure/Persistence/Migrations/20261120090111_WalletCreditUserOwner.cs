using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WalletCreditUserOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "rider_id",
                table: "wallet_credits",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_wallet_credits_rider_id_created_at",
                table: "wallet_credits",
                newName: "ix_wallet_credits_user_id_created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "wallet_credits",
                newName: "rider_id");

            migrationBuilder.RenameIndex(
                name: "ix_wallet_credits_user_id_created_at",
                table: "wallet_credits",
                newName: "ix_wallet_credits_rider_id_created_at");
        }
    }
}
