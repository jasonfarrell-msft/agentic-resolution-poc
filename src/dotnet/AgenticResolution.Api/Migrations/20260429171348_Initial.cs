using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticResolution.Api.Migrations;

/// <inheritdoc />
[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Data.AppDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260429171348_Initial")]
public partial class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TicketNumberSequences",
            columns: table => new
            {
                Id = table.Column<int>("int"),
                LastValue = table.Column<long>("bigint")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketNumberSequences", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tickets",
            columns: table => new
            {
                Id = table.Column<Guid>("uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                Number = table.Column<string>("nvarchar(15)", maxLength: 15, nullable: false),
                ShortDescription = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>("nvarchar(max)", nullable: true),
                Category = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
                Priority = table.Column<int>("int", nullable: false),
                State = table.Column<int>("int", nullable: false),
                AssignedTo = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: true),
                Caller = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>("datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAt = table.Column<DateTime>("datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tickets", x => x.Id);
            });

        migrationBuilder.Sql("""
            INSERT INTO [TicketNumberSequences] ([Id], [LastValue])
            VALUES (1, 10000);
            """);

        migrationBuilder.CreateIndex("IX_Tickets_CreatedAt", "Tickets", "CreatedAt");
        migrationBuilder.CreateIndex("IX_Tickets_Number", "Tickets", "Number", unique: true);
        migrationBuilder.CreateIndex("IX_Tickets_State", "Tickets", "State");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TicketNumberSequences");
        migrationBuilder.DropTable("Tickets");
    }
}
