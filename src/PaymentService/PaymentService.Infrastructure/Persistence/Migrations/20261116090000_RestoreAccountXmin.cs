using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Persistence.Migrations;

public partial class RestoreAccountXmin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // xmin is maintained by PostgreSQL. The merge introduced an unused manual version column.
        migrationBuilder.Sql("ALTER TABLE accounts DROP COLUMN IF EXISTS version;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "version", table: "accounts", type: "bigint",
            nullable: false, defaultValue: 0L);
    }
}
