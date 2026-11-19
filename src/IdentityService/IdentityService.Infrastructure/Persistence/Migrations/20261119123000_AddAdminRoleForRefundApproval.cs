using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IdentityService.Infrastructure.Persistence.Migrations;

[DbContext(typeof(UserDbContext))]
[Migration("20261119123000_AddAdminRoleForRefundApproval")]
public sealed class AddAdminRoleForRefundApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.InsertData("Roles", new[] { "Id", "Name" }, new object[] { 3, "Admin" });

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DeleteData("Roles", "Id", 3);
}
