using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Security_AiAuditLog_GdprCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelUsed = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Decision = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OutputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_audit_log_AgentId",
                table: "ai_audit_log",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_audit_log_RetainUntil",
                table: "ai_audit_log",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_ai_audit_log_Timestamp",
                table: "ai_audit_log",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_audit_log");
        }
    }
}
