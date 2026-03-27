using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nox.Infrastructure.Persistence;

#nullable disable

namespace Nox.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(NoxDbContext))]
    [Migration("20260326120000_AddFlowTriggerKey")]
    public partial class AddFlowTriggerKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trigger_key_hash",
                table: "flows",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trigger_key_hash",
                table: "flows");
        }
    }
}
