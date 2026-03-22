using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acp_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    from_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_flow_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_flow_run_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acp_messages", x => new { x.id, x.timestamp });
                });

            migrationBuilder.CreateTable(
                name: "agent_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    default_model = table.Column<string>(type: "text", nullable: false),
                    system_prompt_template = table.Column<string>(type: "text", nullable: false),
                    default_max_sub_agents = table.Column<int>(type: "integer", nullable: false),
                    skill_groups = table.Column<List<string>>(type: "text[]", nullable: false),
                    default_mcp_servers = table.Column<List<string>>(type: "text[]", nullable: false),
                    token_budget_config = table.Column<string>(type: "jsonb", nullable: false),
                    is_global = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    max_sub_agents = table.Column<int>(type: "integer", nullable: false),
                    current_sub_agent_count = table.Column<int>(type: "integer", nullable: false),
                    tokens_used = table.Column<int>(type: "integer", nullable: false),
                    mcp_server_bindings = table.Column<List<string>>(type: "text[]", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowNodeId = table.Column<string>(type: "text", nullable: false),
                    AssignedAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Input = table.Column<string>(type: "text", nullable: false),
                    Output = table.Column<string>(type: "text", nullable: true),
                    ToolCalls = table.Column<string>(type: "text", nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "flow_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    variables = table.Column<string>(type: "jsonb", nullable: false),
                    current_node_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "flows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    graph = table.Column<string>(type: "jsonb", nullable: false),
                    variables = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hitl_checkpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_node_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    decision_options = table.Column<List<string>>(type: "text[]", nullable: true),
                    decision = table.Column<string>(type: "text", nullable: true),
                    decision_payload = table.Column<string>(type: "jsonb", nullable: true),
                    decision_by = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hitl_checkpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_servers",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    transport = table.Column<string>(type: "text", nullable: false),
                    endpoint_url = table.Column<string>(type: "text", nullable: true),
                    docker_image = table.Column<string>(type: "text", nullable: true),
                    environment_vars = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    proposed_by_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_servers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_memory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    qdrant_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    importance = table.Column<float>(type: "real", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_memory", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    group_id = table.Column<string>(type: "text", nullable: true),
                    owner_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skills", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_agents_flow_run",
                table: "agents",
                column: "flow_run_id");

            migrationBuilder.CreateIndex(
                name: "idx_agents_parent",
                table: "agents",
                column: "parent_agent_id");

            migrationBuilder.CreateIndex(
                name: "idx_flow_runs_flow",
                table: "flow_runs",
                column: "flow_id");

            migrationBuilder.CreateIndex(
                name: "idx_flow_runs_status",
                table: "flow_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_flows_project",
                table: "flows",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_flows_status",
                table: "flows",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_hitl_flow_run",
                table: "hitl_checkpoints",
                column: "flow_run_id");

            migrationBuilder.CreateIndex(
                name: "idx_hitl_pending",
                table: "hitl_checkpoints",
                columns: new[] { "status", "created_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "idx_memory_agent",
                table: "project_memory",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "idx_memory_project",
                table: "project_memory",
                columns: new[] { "project_id", "importance" });

            migrationBuilder.CreateIndex(
                name: "idx_skills_scope",
                table: "skills",
                columns: new[] { "scope", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acp_messages");

            migrationBuilder.DropTable(
                name: "agent_templates");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "AgentTasks");

            migrationBuilder.DropTable(
                name: "flow_runs");

            migrationBuilder.DropTable(
                name: "flows");

            migrationBuilder.DropTable(
                name: "hitl_checkpoints");

            migrationBuilder.DropTable(
                name: "mcp_servers");

            migrationBuilder.DropTable(
                name: "project_memory");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "skills");
        }
    }
}
