using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nox.Infrastructure.Persistence;

#nullable disable

namespace Nox.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(NoxDbContext))]
    [Migration("20260327100000_AddFlowRunConfig")]
    public partial class AddFlowRunConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flow_run_configs",
                columns: table => new
                {
                    flow_id           = table.Column<Guid>(nullable: false),
                    github_repo       = table.Column<string>(type: "text", nullable: true),
                    github_pat        = table.Column<string>(type: "text", nullable: true),
                    github_pat_hash   = table.Column<string>(type: "text", nullable: true),
                    github_branch     = table.Column<string>(type: "text", nullable: true),
                    github_base_branch= table.Column<string>(type: "text", nullable: true),
                    github_issue_number = table.Column<int>(nullable: true),
                    extra_variables   = table.Column<string>(type: "text", nullable: true),
                    updated_at        = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_run_configs", x => x.flow_id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "flow_run_configs");
        }
    }
}
