-- Nox PostgreSQL initialization
-- This runs on first startup of the nox-postgres container

-- Enable uuid-ossp extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================
-- Projects
-- ============================================================
CREATE TABLE IF NOT EXISTS projects (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- Agent Templates
-- ============================================================
CREATE TABLE IF NOT EXISTS agent_templates (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL,
    role TEXT NOT NULL,
    description TEXT,
    default_model TEXT NOT NULL DEFAULT 'Claude4',
    system_prompt_template TEXT NOT NULL DEFAULT '',
    default_max_sub_agents INT NOT NULL DEFAULT 3,
    skill_groups TEXT[] DEFAULT '{}',
    default_mcp_servers TEXT[] DEFAULT '{}',
    token_budget_config JSONB NOT NULL DEFAULT '{"totalBudget":128000,"workingMemoryReserve":8000,"memoryContextBudget":20000,"outputReserve":4000,"skillContextBudget":2000}',
    is_global BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- Flows
-- ============================================================
CREATE TABLE IF NOT EXISTS flows (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL,
    description TEXT,
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    status TEXT NOT NULL DEFAULT 'Draft',
    version INT NOT NULL DEFAULT 1,
    graph JSONB NOT NULL DEFAULT '{"nodes":[],"edges":[]}',
    variables JSONB DEFAULT '{}',
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    created_by TEXT NOT NULL DEFAULT 'system'
);
CREATE INDEX IF NOT EXISTS idx_flows_project ON flows(project_id);
CREATE INDEX IF NOT EXISTS idx_flows_status ON flows(status);

-- ============================================================
-- Flow Runs
-- ============================================================
CREATE TABLE IF NOT EXISTS flow_runs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    flow_id UUID NOT NULL REFERENCES flows(id),
    status TEXT NOT NULL DEFAULT 'Running',
    variables JSONB DEFAULT '{}',
    current_node_ids TEXT[] DEFAULT '{}',
    started_at TIMESTAMPTZ DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    error TEXT
);
CREATE INDEX IF NOT EXISTS idx_flow_runs_flow ON flow_runs(flow_id);
CREATE INDEX IF NOT EXISTS idx_flow_runs_status ON flow_runs(status);

-- ============================================================
-- Agents (runtime instances)
-- ============================================================
CREATE TABLE IF NOT EXISTS agents (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    template_id UUID NOT NULL REFERENCES agent_templates(id),
    flow_run_id UUID NOT NULL REFERENCES flow_runs(id),
    parent_agent_id UUID REFERENCES agents(id),
    name TEXT NOT NULL,
    role TEXT NOT NULL,
    model TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Idle',
    max_sub_agents INT NOT NULL DEFAULT 3,
    current_sub_agent_count INT NOT NULL DEFAULT 0,
    tokens_used INT NOT NULL DEFAULT 0,
    mcp_server_bindings TEXT[] DEFAULT '{}',
    metadata JSONB DEFAULT '{}',
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_agents_flow_run ON agents(flow_run_id);
CREATE INDEX IF NOT EXISTS idx_agents_parent ON agents(parent_agent_id);
CREATE INDEX IF NOT EXISTS idx_agents_status ON agents(status);

-- ============================================================
-- Agent Tasks
-- ============================================================
CREATE TABLE IF NOT EXISTS agent_tasks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    flow_run_id UUID NOT NULL REFERENCES flow_runs(id),
    flow_node_id TEXT NOT NULL,
    assigned_agent_id UUID NOT NULL REFERENCES agents(id),
    parent_task_id UUID REFERENCES agent_tasks(id),
    status TEXT NOT NULL DEFAULT 'Pending',
    input JSONB NOT NULL DEFAULT '{}',
    output JSONB,
    tool_calls JSONB DEFAULT '[]',
    tokens_used INT DEFAULT 0,
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    error TEXT
);
CREATE INDEX IF NOT EXISTS idx_tasks_flow_run ON agent_tasks(flow_run_id);
CREATE INDEX IF NOT EXISTS idx_tasks_agent ON agent_tasks(assigned_agent_id);

-- ============================================================
-- HITL Checkpoints
-- ============================================================
CREATE TABLE IF NOT EXISTS hitl_checkpoints (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    flow_run_id UUID NOT NULL REFERENCES flow_runs(id),
    flow_node_id TEXT NOT NULL,
    type TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Pending',
    title TEXT NOT NULL,
    description TEXT,
    context JSONB NOT NULL DEFAULT '{}',
    decision_options TEXT[],
    decision TEXT,
    decision_payload JSONB,
    decision_by TEXT,
    expires_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    resolved_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_hitl_pending ON hitl_checkpoints(status, created_at) WHERE status = 'Pending';
CREATE INDEX IF NOT EXISTS idx_hitl_flow_run ON hitl_checkpoints(flow_run_id);

-- ============================================================
-- Skills
-- ============================================================
CREATE TABLE IF NOT EXISTS skills (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    slug TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    type TEXT NOT NULL,
    scope TEXT NOT NULL,
    group_id TEXT,
    owner_agent_id UUID REFERENCES agents(id),
    definition JSONB NOT NULL DEFAULT '{}',
    status TEXT NOT NULL DEFAULT 'Active',
    approved_by TEXT,
    version INT NOT NULL DEFAULT 1,
    is_mandatory BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_skills_slug_unique
    ON skills(slug, scope, COALESCE(group_id, ''), COALESCE(owner_agent_id::text, ''));
CREATE INDEX IF NOT EXISTS idx_skills_scope ON skills(scope, status);

-- ============================================================
-- MCP Servers
-- ============================================================
CREATE TABLE IF NOT EXISTS mcp_servers (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    transport TEXT NOT NULL DEFAULT 'sse',
    endpoint_url TEXT,
    docker_image TEXT,
    environment_vars JSONB DEFAULT '{}',
    status TEXT NOT NULL DEFAULT 'Active',
    proposed_by_agent_id UUID REFERENCES agents(id),
    approved_by TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- AI Audit Log (security / GDPR compliance)
-- ============================================================
CREATE TABLE IF NOT EXISTS ai_audit_log (
    "Id" UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "Timestamp" TIMESTAMPTZ NOT NULL,
    "AgentId" UUID NOT NULL,
    "FlowRunId" UUID NOT NULL,
    "EventType" VARCHAR(100) NOT NULL,
    "ModelUsed" VARCHAR(100) NOT NULL,
    "InputTokens" INT NOT NULL DEFAULT 0,
    "OutputTokens" INT NOT NULL DEFAULT 0,
    "DecidedBy" VARCHAR(200),
    "Decision" VARCHAR(200),
    "InputHash" VARCHAR(64),
    "OutputHash" VARCHAR(64),
    "Summary" VARCHAR(500),
    "RetainUntil" TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_ai_audit_log_AgentId" ON ai_audit_log("AgentId");
CREATE INDEX IF NOT EXISTS "IX_ai_audit_log_RetainUntil" ON ai_audit_log("RetainUntil");
CREATE INDEX IF NOT EXISTS "IX_ai_audit_log_Timestamp" ON ai_audit_log("Timestamp");

-- ============================================================
-- Project Memory (metadata; vectors in Qdrant)
-- ============================================================
CREATE TABLE IF NOT EXISTS project_memory (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    agent_id UUID REFERENCES agents(id),
    content TEXT NOT NULL,
    content_type TEXT NOT NULL DEFAULT 'Summary',
    qdrant_point_id UUID NOT NULL UNIQUE,
    token_count INT NOT NULL DEFAULT 0,
    tags TEXT[] DEFAULT '{}',
    importance FLOAT NOT NULL DEFAULT 0.5,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    expires_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_memory_project ON project_memory(project_id, importance);
CREATE INDEX IF NOT EXISTS idx_memory_agent ON project_memory(agent_id);

-- ============================================================
-- ACP Messages (audit log, partitioned by month)
-- ============================================================
CREATE TABLE IF NOT EXISTS acp_messages (
    id UUID NOT NULL,
    correlation_id UUID NOT NULL,
    type TEXT NOT NULL,
    from_agent_id UUID,
    from_flow_run_id UUID,
    to_agent_id UUID,
    to_flow_run_id UUID,
    topic TEXT NOT NULL,
    payload JSONB NOT NULL DEFAULT '{}',
    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (id, timestamp)
) PARTITION BY RANGE (timestamp);

CREATE TABLE IF NOT EXISTS acp_messages_default PARTITION OF acp_messages DEFAULT;
CREATE INDEX IF NOT EXISTS idx_acp_correlation ON acp_messages(correlation_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_acp_topic ON acp_messages(topic, timestamp);

-- ============================================================
-- Seed: default project
-- ============================================================
INSERT INTO projects (id, name, description)
VALUES ('00000000-0000-0000-0000-000000000001', 'Default Project', 'Default Nox project')
ON CONFLICT DO NOTHING;

-- ============================================================
-- Seed: global agent templates
-- ============================================================
INSERT INTO agent_templates (id, name, role, description, default_model, system_prompt_template, default_max_sub_agents, skill_groups, is_global)
VALUES
(
    '10000000-0000-0000-0000-000000000001',
    'Requirements Analyst',
    'RequirementsAnalyst',
    'Analyzes business requirements and produces structured specifications',
    'Claude4',
    'You are an expert Requirements Analyst working in a software house. Your job is to analyze the given input and produce clear, structured software requirements. Be thorough and ask clarifying questions when needed. Output requirements in a structured format with functional and non-functional sections.',
    2,
    ARRAY['analysis', 'global'],
    TRUE
),
(
    '10000000-0000-0000-0000-000000000002',
    'Software Architect',
    'SoftwareArchitect',
    'Designs system architecture based on requirements',
    'Claude4',
    'You are an expert Software Architect. Given requirements, produce a comprehensive architecture document covering: system components, data flows, technology choices with justifications, API contracts, and deployment topology. Be precise and opinionated.',
    3,
    ARRAY['design', 'global'],
    TRUE
),
(
    '10000000-0000-0000-0000-000000000003',
    'Backend Engineer',
    'BackendEngineer',
    'Implements backend services, APIs, and business logic',
    'Claude4',
    'You are a senior Backend Engineer. Implement the assigned backend components following the architecture and coding standards. Write clean, testable, well-documented code. Use existing patterns from the codebase. Always include error handling.',
    5,
    ARRAY['implementation', 'backend', 'global'],
    TRUE
),
(
    '10000000-0000-0000-0000-000000000004',
    'Frontend Engineer',
    'FrontendEngineer',
    'Implements UI components and frontend logic',
    'Claude4',
    'You are a senior Frontend Engineer. Implement the assigned frontend components following the design system and architecture. Write clean, accessible, performant code. Ensure responsive design and good UX.',
    3,
    ARRAY['implementation', 'frontend', 'global'],
    TRUE
),
(
    '10000000-0000-0000-0000-000000000005',
    'QA Engineer',
    'QaEngineer',
    'Writes and executes tests, validates quality',
    'Claude4',
    'You are a QA Engineer. Write comprehensive test suites (unit, integration, e2e) for the assigned components. Identify edge cases, write clear test descriptions, and ensure good coverage. Report any bugs found with reproduction steps.',
    2,
    ARRAY['testing', 'global'],
    TRUE
),
(
    '10000000-0000-0000-0000-000000000006',
    'Code Reviewer',
    'CodeReviewer',
    'Reviews code for quality, security, and standards compliance',
    'Claude4',
    'You are a thorough Code Reviewer. Review the provided code for: correctness, security vulnerabilities, performance issues, maintainability, and adherence to architecture. Provide actionable feedback with specific line references.',
    1,
    ARRAY['review', 'global'],
    TRUE
)
ON CONFLICT DO NOTHING;

-- ============================================================
-- Seed: global skills (slash commands)
-- ============================================================
INSERT INTO skills (id, slug, name, description, type, scope, definition, status)
VALUES
(
    '20000000-0000-0000-0000-000000000001',
    'docs',
    'Generate Documentation',
    'Generate comprehensive documentation for the provided code',
    'SlashCommand',
    'Global',
    '{"promptTemplate": "Generate comprehensive documentation for the following code. Include: purpose, parameters, return values, examples, and any important notes.\n\nCode:\n{{input}}"}',
    'Active'
),
(
    '20000000-0000-0000-0000-000000000002',
    'code-review',
    'Code Review',
    'Perform a thorough code review',
    'SlashCommand',
    'Global',
    '{"promptTemplate": "Perform a thorough code review of the following code. Check for: correctness, security, performance, maintainability, and best practices. Provide specific, actionable feedback.\n\nCode:\n{{input}}"}',
    'Active'
),
(
    '20000000-0000-0000-0000-000000000003',
    'propose-skill',
    'Propose New Skill',
    'Propose a new skill to be added to the registry (requires HITL approval)',
    'SlashCommand',
    'Global',
    '{"promptTemplate": "Create a skill proposal with the following structure:\n- slug: unique-kebab-case-name\n- name: Human readable name\n- description: What this skill does\n- type: SlashCommand|McpTool|Prompt|Workflow\n- scope: Global|Group|Personal\n- definition: The skill implementation details\n\nProposal context: {{input}}"}',
    'Active'
),
(
    '20000000-0000-0000-0000-000000000004',
    'summarize',
    'Summarize',
    'Produce a concise summary of the provided content',
    'SlashCommand',
    'Global',
    '{"promptTemplate": "Produce a concise, structured summary of the following content. Highlight key decisions, outcomes, and any open questions.\n\nContent:\n{{input}}"}',
    'Active'
),
(
    '20000000-0000-0000-0000-000000000005',
    'test-plan',
    'Generate Test Plan',
    'Generate a comprehensive test plan for the given feature or component',
    'SlashCommand',
    'Global',
    '{"promptTemplate": "Generate a comprehensive test plan for the following feature/component. Include: unit tests, integration tests, edge cases, and acceptance criteria.\n\nFeature/Component:\n{{input}}"}',
    'Active'
)
ON CONFLICT DO NOTHING;

-- ============================================================
-- Seed: default flow (Software Delivery)
-- ============================================================
INSERT INTO flows (id, name, description, project_id, status, graph, created_by)
VALUES (
    '30000000-0000-0000-0000-000000000001',
    'Software Delivery',
    'End-to-end software delivery: requirements → architecture → implementation → QA → release',
    '00000000-0000-0000-0000-000000000001',
    'Draft',
    '{
      "nodes": [
        {"id": "start", "nodeType": "Start", "label": "Start", "position": {"x": 100, "y": 300}},
        {"id": "requirements", "nodeType": "AgentTask", "label": "Requirements Analysis", "agentTemplateId": "10000000-0000-0000-0000-000000000001", "config": {"timeoutSeconds": 3600}, "position": {"x": 300, "y": 300}},
        {"id": "arch-review", "nodeType": "HitlCheckpoint", "label": "Review Architecture", "config": {"checkpointType": "Review", "title": "Review architecture proposal", "expiresInHours": 24}, "position": {"x": 500, "y": 300}},
        {"id": "implementation-fork", "nodeType": "Fork", "label": "Implementation Fork", "config": {"branches": ["backend", "frontend", "tests"]}, "position": {"x": 700, "y": 300}},
        {"id": "backend", "nodeType": "AgentTask", "label": "Backend Implementation", "agentTemplateId": "10000000-0000-0000-0000-000000000003", "config": {"maxSubAgents": 5}, "position": {"x": 900, "y": 100}},
        {"id": "frontend", "nodeType": "AgentTask", "label": "Frontend Implementation", "agentTemplateId": "10000000-0000-0000-0000-000000000004", "config": {"maxSubAgents": 3}, "position": {"x": 900, "y": 300}},
        {"id": "tests", "nodeType": "AgentTask", "label": "QA & Tests", "agentTemplateId": "10000000-0000-0000-0000-000000000005", "config": {}, "position": {"x": 900, "y": 500}},
        {"id": "implementation-join", "nodeType": "Join", "label": "Implementation Complete", "config": {"waitFor": ["backend", "frontend", "tests"]}, "position": {"x": 1100, "y": 300}},
        {"id": "code-review-checkpoint", "nodeType": "HitlCheckpoint", "label": "Code Review Approval", "config": {"checkpointType": "Approval", "title": "Approve code for merge"}, "position": {"x": 1300, "y": 300}},
        {"id": "end", "nodeType": "End", "label": "Done", "position": {"x": 1500, "y": 300}}
      ],
      "edges": [
        {"fromNodeId": "start", "toNodeId": "requirements"},
        {"fromNodeId": "requirements", "toNodeId": "arch-review"},
        {"fromNodeId": "arch-review", "toNodeId": "implementation-fork", "condition": "decision == ''Approved''"},
        {"fromNodeId": "arch-review", "toNodeId": "requirements", "condition": "decision == ''Rejected''"},
        {"fromNodeId": "implementation-fork", "toNodeId": "backend"},
        {"fromNodeId": "implementation-fork", "toNodeId": "frontend"},
        {"fromNodeId": "implementation-fork", "toNodeId": "tests"},
        {"fromNodeId": "backend", "toNodeId": "implementation-join"},
        {"fromNodeId": "frontend", "toNodeId": "implementation-join"},
        {"fromNodeId": "tests", "toNodeId": "implementation-join"},
        {"fromNodeId": "implementation-join", "toNodeId": "code-review-checkpoint"},
        {"fromNodeId": "code-review-checkpoint", "toNodeId": "end", "condition": "decision == ''Approved''"},
        {"fromNodeId": "code-review-checkpoint", "toNodeId": "backend", "condition": "decision == ''Rejected''"}
      ]
    }',
    'system'
)
ON CONFLICT DO NOTHING;

-- ============================================================
-- EF Migrations history — marks all migrations as already applied
-- so EF Core does not attempt to re-create the schema on startup
-- ============================================================
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" VARCHAR(150) NOT NULL,
    "ProductVersion" VARCHAR(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
    ('20260322083206_Initial',                            '10.0.5'),
    ('20260322094005_Phase5_McpServerStatusRejected',     '10.0.5'),
    ('20260322234456_Security_AiAuditLog_GdprCompliance', '10.0.5'),
    ('20260323000000_AddMandatorySkillFlag',               '10.0.5')
ON CONFLICT DO NOTHING;
