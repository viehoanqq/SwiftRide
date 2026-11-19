using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IdentityService.Infrastructure.Persistence.Migrations;

[DbContext(typeof(UserDbContext))]
[Migration("20261119123000_AddAdminRoleForRefundApproval")]
public sealed class AddAdminRoleForRefundApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.InsertData(
            table: "Roles", columns: new[] { "Id", "Name" },
            columnTypes: new[] { "int", "nvarchar(20)" }, values: new object[] { 3, "Admin" });

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DeleteData(table: "Roles", keyColumn: "Id", keyColumnType: "int", keyValue: 3);
}
