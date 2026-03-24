using Microsoft.EntityFrameworkCore;
using Nox.Domain;
using Nox.Domain.Agents;
using Nox.Domain.Flows;
using Nox.Domain.Skills;
using Nox.Infrastructure.Persistence;
using System.Text.Json.Nodes;

namespace Nox.Api.Seed;

/// <summary>
/// Seeds agent templates, GitHub skills, and SDLC flow on first startup.
/// Idempotent: checks existence by fixed GUIDs before inserting.
/// </summary>
public static class SdlcSeed
{
    // ── Fixed GUIDs for idempotent seed ──────────────────────────────────────

    // Agent Templates
    private static readonly Guid TplRequirementsAnalyst = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid TplSoftwareArchitect   = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid TplBackendDeveloper    = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid TplFrontendDeveloper   = Guid.Parse("a1000000-0000-0000-0000-000000000004");
    private static readonly Guid TplQaEngineer          = Guid.Parse("a1000000-0000-0000-0000-000000000005");
    private static readonly Guid TplTechWriter          = Guid.Parse("a1000000-0000-0000-0000-000000000006");
    private static readonly Guid TplDevOps              = Guid.Parse("a1000000-0000-0000-0000-000000000007");

    // SDLC Flow
    private static readonly Guid SdlcFlowId = Guid.Parse("f1000000-0000-0000-0000-000000000001");

    // ── GitHub Skill slugs (checked by slug, not ID) ──────────────────────────
    private static readonly string[] GitHubSlugs =
    [
        "github-read-issue", "github-create-branch", "github-read-file",
        "github-write-file",  "github-list-files",    "github-create-pr"
    ];

    public static async Task RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoxDbContext>();

        await SeedSkillsAsync(db);
        await SeedTemplatesAsync(db);
        await SeedFlowAsync(db);
    }

    // ── Skills ────────────────────────────────────────────────────────────────

    private static async Task SeedSkillsAsync(NoxDbContext db)
    {
        var existing = await db.Skills
            .Where(s => GitHubSlugs.Contains(s.Slug))
            .Select(s => s.Slug)
            .ToListAsync();

        var toInsert = new List<Skill>();
        void Add(string slug, string name, string desc) =>
            toInsert.Add(new Skill
            {
                Slug        = slug,
                Name        = name,
                Description = desc,
                Type        = SkillType.SlashCommand,
                Scope       = SkillScope.Global,
                GroupId     = "github",
                IsMandatory = false,
                Status      = SkillStatus.Active,
                Definition  = new JsonObject { ["group"] = "github" }
            });

        if (!existing.Contains("github-read-issue"))
            Add("github-read-issue",    "GitHub: Read Issue",    "Read a GitHub issue by number — returns title, body, state, labels.");
        if (!existing.Contains("github-create-branch"))
            Add("github-create-branch", "GitHub: Create Branch", "Create a new branch in the repository from a specified base branch.");
        if (!existing.Contains("github-read-file"))
            Add("github-read-file",     "GitHub: Read File",     "Read the content of a file or list a directory from the repository.");
        if (!existing.Contains("github-write-file"))
            Add("github-write-file",    "GitHub: Write File",    "Create or update a file on the working branch with a commit message.");
        if (!existing.Contains("github-list-files"))
            Add("github-list-files",    "GitHub: List Files",    "List files and directories at a given path in the repository.");
        if (!existing.Contains("github-create-pr"))
            Add("github-create-pr",     "GitHub: Create PR",     "Create a pull request from the working branch to the base branch.");

        if (toInsert.Count > 0)
        {
            db.Skills.AddRange(toInsert);
            await db.SaveChangesAsync();
        }
    }

    // ── Agent Templates ───────────────────────────────────────────────────────

    private static async Task SeedTemplatesAsync(NoxDbContext db)
    {
        var existingIds = await db.AgentTemplates
            .Where(t => new[] { TplRequirementsAnalyst, TplSoftwareArchitect, TplBackendDeveloper,
                                  TplFrontendDeveloper, TplQaEngineer, TplTechWriter, TplDevOps }
                        .Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync();

        var templates = new List<AgentTemplate>();

        if (!existingIds.Contains(TplRequirementsAnalyst))
            templates.Add(new AgentTemplate
            {
                Id          = TplRequirementsAnalyst,
                Name        = "Requirements Analyst",
                Role        = "requirements-analyst",
                Description = "Reads the GitHub issue and produces a structured Software Requirements Specification.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude3Sonnet,
                SystemPromptTemplate = """
                    You are a Senior Requirements Analyst in a software development team.
                    Your task is to analyze a GitHub issue and produce a structured SRS.

                    Steps:
                    1. Call github_read_issue with the issue_number from flow context (variable: github_issue_number).
                    2. Analyze functional requirements, non-functional requirements, and constraints.
                    3. Write a complete SRS document to 'docs/SRS.md' using github_write_file.
                       Commit message: "docs: add Software Requirements Specification"

                    The flow context (Task payload) contains:
                    - github_repo: repository in "owner/repo" format
                    - github_pat: personal access token
                    - github_branch: working branch name
                    - github_issue_number: issue number to analyze

                    Produce a comprehensive SRS that the architect can use to design the system.
                    """
            });

        if (!existingIds.Contains(TplSoftwareArchitect))
            templates.Add(new AgentTemplate
            {
                Id          = TplSoftwareArchitect,
                Name        = "Software Architect",
                Role        = "software-architect",
                Description = "Reads the SRS and designs the technical architecture and implementation plan.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude4,
                SystemPromptTemplate = """
                    You are a Senior Software Architect.
                    Your task is to read the requirements and design the technical architecture.

                    Steps:
                    1. Read 'docs/SRS.md' using github_read_file.
                    2. List existing source files with github_list_files to understand the codebase.
                    3. Design: components, data models, API contracts, technology choices.
                    4. Write 'docs/ARCHITECTURE.md' using github_write_file.
                       Commit message: "docs: add Architecture Document"
                    5. Write 'docs/IMPLEMENTATION_PLAN.md' with a detailed task-by-task plan.
                       Commit message: "docs: add Implementation Plan"

                    The implementation plan must be detailed enough for developers to follow without further clarification.
                    """
            });

        if (!existingIds.Contains(TplBackendDeveloper))
            templates.Add(new AgentTemplate
            {
                Id          = TplBackendDeveloper,
                Name        = "Backend Developer",
                Role        = "backend-developer",
                Description = "Implements backend features based on the architecture and implementation plan.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude4,
                SystemPromptTemplate = """
                    You are a Senior Backend Developer.
                    Your task is to implement the backend changes described in the architecture.

                    Steps:
                    1. Read 'docs/ARCHITECTURE.md' and 'docs/IMPLEMENTATION_PLAN.md'.
                    2. List existing source files to understand the project structure.
                    3. Implement each backend change (API, services, models) one file at a time.
                    4. Write each file using github_write_file on the working branch.
                    5. Produce a summary of what was implemented.

                    Write clean, idiomatic code that follows the patterns already present in the codebase.
                    Do not implement frontend or tests — focus only on backend.
                    """
            });

        if (!existingIds.Contains(TplFrontendDeveloper))
            templates.Add(new AgentTemplate
            {
                Id          = TplFrontendDeveloper,
                Name        = "Frontend Developer",
                Role        = "frontend-developer",
                Description = "Implements frontend UI components and pages based on the architecture.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude4,
                SystemPromptTemplate = """
                    You are a Senior Frontend Developer.
                    Your task is to implement the UI changes described in the architecture.

                    Steps:
                    1. Read 'docs/ARCHITECTURE.md' and 'docs/IMPLEMENTATION_PLAN.md'.
                    2. List existing frontend source files to understand the structure.
                    3. Implement each UI component/page one file at a time.
                    4. Write each file using github_write_file on the working branch.
                    5. Produce a summary of what was implemented.

                    Write clean, maintainable UI code that follows existing conventions.
                    Do not implement backend or tests — focus only on frontend.
                    """
            });

        if (!existingIds.Contains(TplQaEngineer))
            templates.Add(new AgentTemplate
            {
                Id          = TplQaEngineer,
                Name        = "QA Engineer",
                Role        = "qa-engineer",
                Description = "Writes unit and integration tests for the implemented features.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude3Sonnet,
                SystemPromptTemplate = """
                    You are a Senior QA Engineer.
                    Your task is to write tests for the features implemented in this sprint.

                    Steps:
                    1. Read 'docs/IMPLEMENTATION_PLAN.md' to understand what was implemented.
                    2. List source files to find the implemented code.
                    3. Write unit tests and integration tests covering the key scenarios.
                    4. Write test files using github_write_file on the working branch.
                    5. Produce a test coverage summary.

                    Focus on correctness, edge cases, and error paths.
                    """
            });

        if (!existingIds.Contains(TplTechWriter))
            templates.Add(new AgentTemplate
            {
                Id          = TplTechWriter,
                Name        = "Tech Writer",
                Role        = "tech-writer",
                Description = "Updates README and documentation to reflect the new features.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude3Sonnet,
                SystemPromptTemplate = """
                    You are a Technical Writer.
                    Your task is to update project documentation for the delivered features.

                    Steps:
                    1. Read existing 'README.md' (use github_read_file; if not found, create it).
                    2. Read 'docs/ARCHITECTURE.md' for technical context.
                    3. Update or create 'README.md' with clear setup, usage, and feature descriptions.
                    4. Update or create 'docs/CHANGELOG.md' with an entry for this sprint.
                    5. Write both files using github_write_file.

                    Write clear, concise documentation for both developers and end users.
                    """
            });

        if (!existingIds.Contains(TplDevOps))
            templates.Add(new AgentTemplate
            {
                Id          = TplDevOps,
                Name        = "DevOps Engineer",
                Role        = "devops-engineer",
                Description = "Creates the pull request summarizing all changes from the sprint.",
                IsGlobal    = true,
                SkillGroups = ["github"],
                DefaultModel = LlmModel.Claude3Sonnet,
                SystemPromptTemplate = """
                    You are a DevOps Engineer.
                    Your task is to create a Pull Request with all changes from this sprint.

                    Steps:
                    1. Read 'docs/SRS.md' for the requirements context.
                    2. Read 'docs/ARCHITECTURE.md' for the technical summary.
                    3. List files on the working branch to enumerate all changes.
                    4. Create a PR using github_create_pr with:
                       - A clear, descriptive title
                       - A comprehensive body: summary of changes, how to test, notes for reviewers
                       - base_branch: use the value from flow context (github_base_branch or "main")
                    5. Return the PR URL.
                    """
            });

        if (templates.Count > 0)
        {
            db.AgentTemplates.AddRange(templates);
            await db.SaveChangesAsync();
        }
    }

    // ── SDLC Flow ─────────────────────────────────────────────────────────────

    private static async Task SeedFlowAsync(NoxDbContext db)
    {
        if (await db.Flows.AnyAsync(f => f.Id == SdlcFlowId))
            return;

        // Helper positions
        static FlowPosition P(double x, double y) => new((float)x, (float)y);

        var graph = new FlowGraph();

        // ── Nodes ──────────────────────────────────────────────────────────
        graph.Nodes.AddRange(
        [
            new FlowNode { Id = "start",           NodeType = NodeType.Start,          Label = "Start",                      Position = P(100, 300) },

            new FlowNode { Id = "requirements",    NodeType = NodeType.AgentTask,       Label = "Requirements Analyst",        AgentTemplateId = TplRequirementsAnalyst, Position = P(300, 300) },
            new FlowNode { Id = "hitl-spec",       NodeType = NodeType.HitlCheckpoint,  Label = "Approva Specifiche",
                Config = new JsonObject { ["checkpointType"] = "Approval", ["title"] = "Approvazione Specifiche",
                    ["description"] = "Revisiona la SRS prodotta dall'analista. Approva per procedere con l'architettura o rifiuta per revisionare.",
                    ["expiresInHours"] = 72 },
                Position = P(500, 300) },

            new FlowNode { Id = "architect",       NodeType = NodeType.AgentTask,       Label = "Software Architect",          AgentTemplateId = TplSoftwareArchitect, Position = P(700, 300) },
            new FlowNode { Id = "hitl-arch",       NodeType = NodeType.HitlCheckpoint,  Label = "Approva Architettura",
                Config = new JsonObject { ["checkpointType"] = "Approval", ["title"] = "Approvazione Architettura",
                    ["description"] = "Revisiona l'architettura e il piano di implementazione. Approva per avviare lo sviluppo.",
                    ["expiresInHours"] = 72 },
                Position = P(900, 300) },

            new FlowNode { Id = "backend",         NodeType = NodeType.AgentTask,       Label = "Backend Developer",           AgentTemplateId = TplBackendDeveloper,  Position = P(300, 500) },
            new FlowNode { Id = "frontend",        NodeType = NodeType.AgentTask,       Label = "Frontend Developer",          AgentTemplateId = TplFrontendDeveloper, Position = P(500, 500) },
            new FlowNode { Id = "hitl-impl",       NodeType = NodeType.HitlCheckpoint,  Label = "Revisiona Implementazione",
                Config = new JsonObject { ["checkpointType"] = "Review", ["title"] = "Revisione Implementazione",
                    ["description"] = "Revisiona il codice implementato da backend e frontend. Approva per procedere con QA.",
                    ["expiresInHours"] = 72 },
                Position = P(700, 500) },

            new FlowNode { Id = "qa",              NodeType = NodeType.AgentTask,       Label = "QA Engineer",                 AgentTemplateId = TplQaEngineer,        Position = P(900, 500) },
            new FlowNode { Id = "tech-writer",     NodeType = NodeType.AgentTask,       Label = "Tech Writer",                 AgentTemplateId = TplTechWriter,        Position = P(300, 700) },
            new FlowNode { Id = "devops",          NodeType = NodeType.AgentTask,       Label = "DevOps Engineer",             AgentTemplateId = TplDevOps,            Position = P(500, 700) },
            new FlowNode { Id = "hitl-pr",         NodeType = NodeType.HitlCheckpoint,  Label = "Approva Pull Request",
                Config = new JsonObject { ["checkpointType"] = "Approval", ["title"] = "Approvazione Pull Request",
                    ["description"] = "Revisiona la PR creata. Approva per completare il flusso o rifiuta per tornare allo sviluppo.",
                    ["expiresInHours"] = 72 },
                Position = P(700, 700) },

            new FlowNode { Id = "end",             NodeType = NodeType.End,             Label = "End",                        Position = P(900, 700) }
        ]);

        // ── Edges (with revision loops on rejection) ───────────────────────
        graph.Edges.AddRange(
        [
            new FlowEdge { FromNodeId = "start",       ToNodeId = "requirements" },

            // Spec approval loop
            new FlowEdge { FromNodeId = "requirements", ToNodeId = "hitl-spec" },
            new FlowEdge { FromNodeId = "hitl-spec",    ToNodeId = "architect",    Condition = "decision == 'Approved'" },
            new FlowEdge { FromNodeId = "hitl-spec",    ToNodeId = "requirements", Condition = "decision == 'Rejected'" },

            // Architecture approval loop
            new FlowEdge { FromNodeId = "architect",   ToNodeId = "hitl-arch" },
            new FlowEdge { FromNodeId = "hitl-arch",   ToNodeId = "backend",      Condition = "decision == 'Approved'" },
            new FlowEdge { FromNodeId = "hitl-arch",   ToNodeId = "architect",    Condition = "decision == 'Rejected'" },

            // Development (sequential: backend → frontend → review)
            new FlowEdge { FromNodeId = "backend",     ToNodeId = "frontend" },
            new FlowEdge { FromNodeId = "frontend",    ToNodeId = "hitl-impl" },
            new FlowEdge { FromNodeId = "hitl-impl",   ToNodeId = "qa",           Condition = "decision == 'Approved'" },
            new FlowEdge { FromNodeId = "hitl-impl",   ToNodeId = "backend",      Condition = "decision == 'Rejected'" },

            // QA → Docs → PR
            new FlowEdge { FromNodeId = "qa",          ToNodeId = "tech-writer" },
            new FlowEdge { FromNodeId = "tech-writer",  ToNodeId = "devops" },
            new FlowEdge { FromNodeId = "devops",       ToNodeId = "hitl-pr" },
            new FlowEdge { FromNodeId = "hitl-pr",      ToNodeId = "end",          Condition = "decision == 'Approved'" },
            new FlowEdge { FromNodeId = "hitl-pr",      ToNodeId = "backend",      Condition = "decision == 'Rejected'" },
        ]);

        db.Flows.Add(new Flow
        {
            Id          = SdlcFlowId,
            Name        = "SDLC Completo",
            Description = "Flusso di sviluppo software completo: requirements → architettura → implementazione → QA → documentazione → PR. Include checkpoint HITL con loop di revisione.",
            ProjectId   = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            CreatedBy   = "system",
            Status      = FlowStatus.Active,
            Graph       = graph,
        });

        await db.SaveChangesAsync();
    }
}
