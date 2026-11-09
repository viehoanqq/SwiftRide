using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IdentityService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Rider" },
                    { 2, "Driver" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "FullName", "IsActive", "PasswordHash", "Phone", "RoleId" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000001"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Nguyễn Văn An", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0900000001", 1 },
                    { new Guid("11111111-0000-0000-0000-000000000002"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Trần Thị Bình", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0900000002", 1 },
                    { new Guid("22222222-0000-0000-0000-000000000001"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Lê Văn Cường", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0910000001", 2 },
                    { new Guid("22222222-0000-0000-0000-000000000002"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Phạm Minh Dũng", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0910000002", 2 },
                    { new Guid("22222222-0000-0000-0000-000000000003"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Hoàng Văn Em", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0910000003", 2 },
                    { new Guid("22222222-0000-0000-0000-000000000004"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Võ Thị Phương", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0910000004", 2 },
                    { new Guid("22222222-0000-0000-0000-000000000005"), new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Đặng Quốc Gia", true, "$2a$11$hxB.ClZYJvFCwgHXQOq.HONuat.KPFjMXbrVuZ0ndVO6v5N6OHhSW", "0910000005", 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OccurredAt",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "[ProcessedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
