using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TripService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtractIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "TripDriverOffers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql("""
                IF DB_ID(N'UserDb') IS NULL OR OBJECT_ID(N'UserDb.dbo.Users') IS NULL
                    THROW 51001, 'Create UserDb and run scripts/split-identity.ps1 first.', 1;
                IF EXISTS (
                    SELECT Id, RoleId, FullName, Phone, PasswordHash, IsActive, CreatedAt FROM dbo.Users
                    EXCEPT
                    SELECT Id, RoleId, FullName, Phone, PasswordHash, IsActive, CreatedAt FROM UserDb.dbo.Users
                ) THROW 51001, 'Identity copy is missing or stale; stop services and run split-identity.ps1.', 1;
                IF EXISTS (
                    SELECT Id, Name FROM dbo.Roles EXCEPT SELECT Id, Name FROM UserDb.dbo.Roles
                ) THROW 51001, 'Identity roles have not been copied.', 1;
                IF EXISTS (
                    SELECT Id, Type, Payload, CorrelationId, OccurredAt, ProcessedAt FROM dbo.OutboxMessages
                    EXCEPT
                    SELECT Id, Type, Payload, CorrelationId, OccurredAt, ProcessedAt FROM UserDb.dbo.OutboxMessages
                ) THROW 51001, 'Identity outbox has not been copied.', 1;
                UPDATE o SET DriverName = u.FullName
                FROM dbo.TripDriverOffers o JOIN dbo.Users u ON u.Id = o.DriverId;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_TripDriverOffers_Users_DriverId",
                table: "TripDriverOffers");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Users_RiderId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");


        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Restore the pre-cutover backup to roll back identity extraction.");
    }
}
