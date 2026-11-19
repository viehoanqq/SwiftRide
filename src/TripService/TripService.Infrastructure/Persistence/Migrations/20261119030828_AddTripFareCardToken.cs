using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripFareCardToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardToken",
                table: "TripFares",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardToken",
                table: "TripFares");
        }
    }
}
