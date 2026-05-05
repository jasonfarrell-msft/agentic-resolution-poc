using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticResolution.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentsAndWorkflowRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TriggeredBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalAction = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FinalConfidence = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRuns_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRunEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ExecutorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRunEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRunEvents_WorkflowRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "WorkflowRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedTo",
                table: "Tickets",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Category",
                table: "Tickets",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_UpdatedAt",
                table: "Tickets",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TicketId",
                table: "Comments",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRunEvents_RunId_Sequence",
                table: "WorkflowRunEvents",
                columns: new[] { "RunId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_TicketId_Status",
                table: "WorkflowRuns",
                columns: new[] { "TicketId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "WorkflowRunEvents");

            migrationBuilder.DropTable(
                name: "WorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AssignedTo",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Category",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_UpdatedAt",
                table: "Tickets");
        }
    }
}
