using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticResolution.Api.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Data.AppDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260430000000_AddAgentFields")]
public partial class AddAgentFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("AgentAction", "Tickets", "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<double>("AgentConfidence", "Tickets", "float", nullable: true);
        migrationBuilder.AddColumn<string>("MatchedTicketNumber", "Tickets", "nvarchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>("ResolutionNotes", "Tickets", "nvarchar(max)", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("AgentAction", "Tickets");
        migrationBuilder.DropColumn("AgentConfidence", "Tickets");
        migrationBuilder.DropColumn("MatchedTicketNumber", "Tickets");
        migrationBuilder.DropColumn("ResolutionNotes", "Tickets");
    }
}
