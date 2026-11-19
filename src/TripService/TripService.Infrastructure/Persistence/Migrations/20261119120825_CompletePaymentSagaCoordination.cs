using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletePaymentSagaCoordination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefundApprovedAt",
                table: "PaymentSagas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundApprovedBy",
                table: "PaymentSagas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReasonCode",
                table: "PaymentSagas",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundTimedOutAt",
                table: "PaymentSagas",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundApprovedAt",
                table: "PaymentSagas");

            migrationBuilder.DropColumn(
                name: "RefundApprovedBy",
                table: "PaymentSagas");

            migrationBuilder.DropColumn(
                name: "RefundReasonCode",
                table: "PaymentSagas");

            migrationBuilder.DropColumn(
                name: "RefundTimedOutAt",
                table: "PaymentSagas");
        }
    }
}
