using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedPaymentResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedPaymentResults",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedPaymentResults", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedPaymentResults_TripId",
                table: "ProcessedPaymentResults",
                column: "TripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedPaymentResults");
        }
    }
}
