using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDistanceToPickupKm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DistanceToPickupKm",
                table: "TripDriverOffers",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistanceToPickupKm",
                table: "TripDriverOffers");
        }
    }
}
