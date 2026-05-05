using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticResolution.Api.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Data.AppDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260501000000_AddClassificationField")]
public partial class AddClassificationField : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Classification", "Tickets", "nvarchar(20)", maxLength: 20, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("Classification", "Tickets");
    }
}
