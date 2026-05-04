using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticResolution.Api.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Data.AppDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260429174520_PriorityEnumReorder")]
public partial class PriorityEnumReorder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 91 WHERE [Priority] = 1;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 92 WHERE [Priority] = 2;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 93 WHERE [Priority] = 3;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 94 WHERE [Priority] = 4;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 4 WHERE [Priority] = 91;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 3 WHERE [Priority] = 92;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 2 WHERE [Priority] = 93;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 1 WHERE [Priority] = 94;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 91 WHERE [Priority] = 1;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 92 WHERE [Priority] = 2;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 93 WHERE [Priority] = 3;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 94 WHERE [Priority] = 4;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 4 WHERE [Priority] = 91;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 3 WHERE [Priority] = 92;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 2 WHERE [Priority] = 93;");
        migrationBuilder.Sql("UPDATE [Tickets] SET [Priority] = 1 WHERE [Priority] = 94;");
    }
}
