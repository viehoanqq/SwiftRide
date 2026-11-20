using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowAdminBalanceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wallet_credit_amount",
                table: "wallet_credits");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wallet_credit_amount",
                table: "wallet_credits",
                sql: "amount <> 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wallet_credit_amount",
                table: "wallet_credits");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wallet_credit_amount",
                table: "wallet_credits",
                sql: "amount > 0 AND amount <= 100000000");
        }
    }
}
