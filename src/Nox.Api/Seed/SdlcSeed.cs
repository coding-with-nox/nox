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

    private static readonly string[] CommandSlugs =
    [
        "docs", "code-review", "summarize", "test-plan", "propose-skill"
    ];

    private static async Task SeedSkillsAsync(NoxDbContext db)
    {
        // ── GitHub skills: insert-only (execution handled by GitHubToolHandler) ──
        var existingGh = await db.Skills
            .Where(s => GitHubSlugs.Contains(s.Slug))
            .Select(s => s.Slug)
            .ToListAsync();

        var toInsert = new List<Skill>();
        void AddGh(string slug, string name, string desc) =>
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

        if (!existingGh.Contains("github-read-issue"))
            AddGh("github-read-issue",    "GitHub: Read Issue",    "Read a GitHub issue by number — returns title, body, state, labels.");
        if (!existingGh.Contains("github-create-branch"))
            AddGh("github-create-branch", "GitHub: Create Branch", "Create a new branch in the repository from a specified base branch.");
        if (!existingGh.Contains("github-read-file"))
            AddGh("github-read-file",     "GitHub: Read File",     "Read the content of a file or list a directory from the repository.");
        if (!existingGh.Contains("github-write-file"))
            AddGh("github-write-file",    "GitHub: Write File",    "Create or update a file on the working branch with a commit message.");
        if (!existingGh.Contains("github-list-files"))
            AddGh("github-list-files",    "GitHub: List Files",    "List files and directories at a given path in the repository.");
        if (!existingGh.Contains("github-create-pr"))
            AddGh("github-create-pr",     "GitHub: Create PR",     "Create a pull request from the working branch to the base branch.");

        if (toInsert.Count > 0)
            db.Skills.AddRange(toInsert);

        // ── Slash-command skills: upsert with rich prompt templates ──────────────
        var existingCmd = await db.Skills
            .Where(s => CommandSlugs.Contains(s.Slug))
            .ToListAsync();

        void UpsertCmd(string slug, string name, string shortDesc, string promptTemplate)
        {
            var found = existingCmd.FirstOrDefault(s => s.Slug == slug);
            var def = new JsonObject { ["promptTemplate"] = promptTemplate };
            if (found is null)
            {
                db.Skills.Add(new Skill
                {
                    Slug        = slug,
                    Name        = name,
                    Description = shortDesc,
                    Type        = SkillType.SlashCommand,
                    Scope       = SkillScope.Global,
                    IsMandatory = false,
                    Status      = SkillStatus.Active,
                    Definition  = def
                });
            }
            else
            {
                found.Name        = name;
                found.Description = shortDesc;
                found.Definition  = def;
                found.UpdatedAt   = DateTimeOffset.UtcNow;
            }
        }

        UpsertCmd(
            "docs",
            "Generate Documentation",
            "Generates exhaustive, developer-grade technical documentation for any code artifact: module, class, function, API endpoint, or service. " +
            "The output is structured Markdown covering seven mandatory sections: " +
            "(1) Overview — purpose, architectural layer, and system context; " +
            "(2) Public API — full typed signatures, parameter descriptions with types and constraints, return value semantics, and all exceptions that can be thrown; " +
            "(3) Data Models — every DTO, entity, and value object with field names, types, invariants, and validation rules; " +
            "(4) Usage Examples — at least two realistic, copy-pasteable code samples showing the happy path and a non-trivial edge case; " +
            "(5) Dependencies and Integration Points — external services, injected interfaces, configuration keys required; " +
            "(6) Known Limitations and Edge Cases — non-obvious behaviour, thread-safety notes, performance hotspots, and gotchas a maintainer must know; " +
            "(7) Changelog — breaking changes if version history is available. " +
            "Output is always precise, factual, and free of padding. Code blocks use language-tagged fences. " +
            "Ideal for onboarding new engineers, generating API references, or producing handoff documentation before a code review.",
            """
            You are a Senior Technical Writer with deep software engineering expertise.
            Your task is to generate exhaustive, developer-grade documentation for the code or component provided.

            Documentation MUST include ALL of the following sections:

            ## Overview
            A clear, concise description of the module/component purpose, its responsibility within the system, and the architectural layer it belongs to (e.g., domain, application, infrastructure, presentation).

            ## Public API
            For every public class, method, function, or endpoint:
            - **Signature**: full typed signature
            - **Purpose**: what it does and why it exists
            - **Parameters**: name, type, description, constraints, whether optional or required
            - **Return value**: type, shape, and semantics (including null/undefined cases)
            - **Exceptions / errors**: what can be thrown and under what conditions
            - **Side effects**: database writes, cache invalidations, events published, external calls

            ## Data Models
            Document all DTOs, domain entities, and value objects: field names, types, invariants, validation rules.

            ## Usage Examples
            At least 2 realistic, copy-pasteable code examples showing happy path and a non-trivial edge case.

            ## Dependencies & Integration Points
            List external services, interfaces, or modules this component depends on. Note injection points and configuration keys.

            ## Known Limitations & Edge Cases
            Explicitly call out any non-obvious behaviour, thread-safety considerations, performance hotspots, or gotchas a maintainer must know.

            ## Changelog
            If version history is available, summarise key breaking changes.

            Output format: GitHub-flavored Markdown. Use fenced code blocks with language tags. Be precise, factual, and avoid padding.
            """);

        UpsertCmd(
            "code-review",
            "Code Review",
            "Performs a formal, principal-engineer-level code review across five critical dimensions. " +
            "Correctness: detects logic errors, null dereferences, off-by-one mistakes, race conditions, improper resource disposal, and incorrect error propagation. " +
            "Security: flags injection vulnerabilities (SQL, command, XSS, SSTI), authentication and authorisation bypass risks, secrets in code or logs, insecure deserialization, path traversal, and SSRF. " +
            "Performance: identifies N+1 query patterns, unnecessary allocations, synchronous I/O blocking async threads, missing caching for expensive idempotent operations, and algorithmic inefficiencies. " +
            "Design and Maintainability: evaluates SOLID compliance, cohesion, coupling, naming clarity, magic numbers, code duplication, and excessive nesting. " +
            "Test Coverage: checks for missing unit tests on critical paths, vacuous assertions, and gaps in edge-case and error-path coverage. " +
            "Each issue is reported with severity (Critical / Major / Minor / Suggestion), location, problem description, and a concrete recommended fix with a code snippet. " +
            "The review closes with a summary section containing an overall quality score from 1 to 10, the top three priorities, and a clear go/no-go recommendation for merge.",
            """
            You are a Principal Engineer conducting a formal code review. Your review must be thorough, constructive, and actionable. Evaluate the provided code across ALL of the following dimensions:

            ## Correctness
            - Logic errors, off-by-one errors, null/undefined dereferences
            - Incorrect assumptions about inputs or external system behaviour
            - Race conditions, concurrency issues, improper resource disposal
            - Missing or incorrect error propagation

            ## Security
            - Injection vulnerabilities: SQL, command, LDAP, XSS, SSTI
            - Authentication/authorisation bypass risks
            - Secrets or credentials in code or logs
            - Insecure deserialization, path traversal, SSRF
            - Sensitive data logged or exposed in error messages

            ## Performance
            - N+1 query patterns, missing indexes, unbounded queries
            - Unnecessary allocations, boxing, string concatenation in hot paths
            - Synchronous I/O blocking async threads
            - Missing caching for expensive idempotent operations
            - Inefficient algorithms (O(n²) where O(n log n) exists)

            ## Design & Maintainability
            - Adherence to SOLID principles; single responsibility violations
            - Cohesion, coupling, and dependency direction
            - Magic numbers, unclear naming, misleading comments
            - Code duplication that should be extracted
            - Overly deep nesting or complex branching that hurts readability

            ## Test Coverage
            - Missing unit tests for critical paths
            - Tests that don't assert meaningful behaviour (vacuous tests)
            - Lack of edge-case and error-path coverage

            ## Output format
            For each issue found:
            - **Severity**: Critical / Major / Minor / Suggestion
            - **Location**: file and line number if available
            - **Problem**: concise description
            - **Recommended fix**: concrete code snippet or pattern

            End with a **Summary** section: overall quality score (1–10), top 3 priorities, and a go/no-go recommendation.
            """);

        UpsertCmd(
            "summarize",
            "Summarize",
            "Produces a structured, information-dense summary of any provided content: meeting notes, technical documents, code changelists, incident reports, or free-form text. " +
            "The output is always organized into seven sections: " +
            "Executive Summary (three to five sentences capturing the single most important takeaway for a decision-maker who will not read further); " +
            "Key Decisions Made (each decision with its rationale and owner if known); " +
            "Key Findings and Outcomes (concrete facts and measurements with numbers preserved exactly as written); " +
            "Risks and Issues Identified (each risk rated by likelihood and impact — High, Medium, or Low — with any mitigations mentioned); " +
            "Open Questions and Blockers (items requiring a decision before work can proceed, with owner if mentioned); " +
            "Action Items (concrete next steps formatted as owner, action, and deadline); " +
            "Context and Background (only when relevant information would otherwise be lost). " +
            "Rules enforced: bullet points and headers only, no prose walls; technical terms preserved verbatim; code summarized by purpose rather than transcribed; contradictions flagged explicitly. Output in GitHub-flavored Markdown.",
            """
            You are a Senior Analyst skilled at distilling complex information into clear, structured summaries. Your task is to produce a concise yet information-dense summary of the provided content.

            The summary MUST be structured as follows:

            ## Executive Summary (3–5 sentences)
            The "so what" — the most important takeaway a decision-maker needs to understand without reading further.

            ## Key Decisions Made
            Bullet list. For each decision: what was decided, who decided it (if known), and the rationale.

            ## Key Findings / Outcomes
            Concrete facts, measurements, or results. Avoid vague language. If numbers are present, preserve them.

            ## Risks & Issues Identified
            Each risk: description, likelihood (High/Medium/Low), impact (High/Medium/Low), and any mitigations mentioned.

            ## Open Questions & Blockers
            Items that require a decision or action before work can proceed. Owner if mentioned.

            ## Action Items
            Concrete next steps. Format: [Owner if known] — action — deadline if mentioned.

            ## Context & Background (optional)
            Only if relevant information would otherwise be lost: brief explanation of the domain or prior state.

            Rules:
            - Use bullet points and headers; no prose walls
            - Preserve technical terms exactly as written
            - If the content is code, summarise its purpose, interfaces, and side effects rather than transcribing it
            - Flag any information that appears contradictory or ambiguous
            - Output in GitHub-flavored Markdown
            """);

        UpsertCmd(
            "test-plan",
            "Generate Test Plan",
            "Generates a rigorous, executable test plan for any feature or component described in the input. " +
            "The plan is structured into six parts: " +
            "Feature Under Test (restates the scope in one paragraph, clarifying what is and is not covered); " +
            "Test Strategy (applicable testing levels — unit, integration, contract, end-to-end — the isolation strategy explaining what to mock or stub and why, the test data management approach, and the minimum acceptable line and branch coverage target); " +
            "Unit Test Cases (a table covering every public method with test ID, scenario, input, expected output, and edge cases — happy path, boundary values, null and empty inputs, type mismatches, and exception paths); " +
            "Integration Test Cases (for each API endpoint, database query, or external service call — setup state, seed data, steps, and assertions on response codes, body shape, database state, and published events); " +
            "Edge Cases and Non-Happy Paths (concurrent access, timeout and retry behaviour, partial failures, large payloads, empty collections, expired tokens); " +
            "Acceptance Criteria (one Gherkin-style Given/When/Then scenario per key user story). " +
            "The plan closes with a Definition of Done checklist a PR must satisfy before the test plan is considered fulfilled.",
            """
            You are a Senior QA Engineer and Test Architect. Generate a rigorous, executable test plan for the feature or component described. The plan must be detailed enough that a developer with no prior context can implement every test case.

            ## Feature Under Test
            Restate the feature/component scope in one paragraph, clarifying what IS and IS NOT in scope.

            ## Test Strategy
            - Testing levels: Unit / Integration / Contract / End-to-End (which apply and why)
            - Test isolation strategy: what to mock, stub, or fake and why
            - Test data management: fixtures, factories, database seeding approach
            - Coverage target: minimum acceptable line/branch coverage %

            ## Unit Test Cases
            For each logical unit (function, method, class):
            | Test ID | Scenario | Input | Expected Output | Edge Cases |
            |---------|----------|-------|-----------------|------------|
            Include: happy path, boundary values, null/empty inputs, type mismatches, exception paths.

            ## Integration Test Cases
            For each integration boundary (API endpoint, DB query, external service call):
            - Scenario description
            - Setup: required state, seed data, mocked dependencies
            - Steps
            - Assertions: response codes, body shape, DB state, events published

            ## Edge Cases & Non-Happy Paths
            Explicitly list: concurrent access scenarios, timeout/retry behaviour, partial failures, large payloads, empty collections, expired tokens.

            ## Acceptance Criteria (Given/When/Then)
            One Gherkin-style scenario per key user story.

            ## Performance & Load Considerations
            If applicable: expected throughput, latency SLOs, load test thresholds.

            ## Definition of Done
            Checklist a PR must satisfy before the test plan is considered fulfilled.
            """);

        UpsertCmd(
            "propose-skill",
            "Propose New Skill",
            "Creates a complete, well-justified skill proposal and submits it to the HITL approval queue for human review before the skill is added to the registry. " +
            "The proposal is structured into six mandatory sections: " +
            "Skill Identity (a globally unique kebab-case slug, a human-readable name in Title Case under forty characters, type — SlashCommand, McpTool, Prompt, or Workflow — scope — Global or Agent — and the logical groupId using existing groups where possible); " +
            "Description (a single sentence under one hundred and twenty characters starting with a verb, shown in the UI skill list); " +
            "Prompt Template (the full instruction text injected as a tool result when the skill is invoked — must be at least five hundred characters, structured with sections and bullet points, specific about expected output format, and free of ambiguity); " +
            "Justification (the problem the skill solves, estimated frequency of use, potential risks such as data leakage or misuse, and alternatives considered); " +
            "IsMandatory flag (whether the skill should be included in every agent toolset with explicit justification); " +
            "Dependencies (any infrastructure, credentials, or external services required). " +
            "Output is structured Markdown ready for a human approver to evaluate and approve or reject via the HITL checkpoint.",
            """
            You are a Platform Engineer responsible for extending the Nox agent skill registry. When asked to propose a new skill, produce a complete, structured proposal that gives the human approver everything they need to evaluate and approve it.

            The proposal MUST contain ALL of the following fields:

            ## Skill Identity
            - **slug**: kebab-case, globally unique, descriptive (e.g., `static-analysis`, `db-schema-diff`)
            - **name**: human-readable title (Title Case, ≤40 chars)
            - **type**: one of `SlashCommand | McpTool | Prompt | Workflow`
            - **scope**: `Global` (available to all agents) or `Agent` (scoped to a specific role)
            - **groupId**: logical group (e.g., `quality`, `security`, `github`, `data`) — use existing groups when possible

            ## Description (shown in UI)
            One sentence (≤120 chars) describing what the skill does. Start with a verb. Avoid jargon.

            ## Prompt Template
            The full instruction text that will be injected as a tool result when the skill is invoked. Must be:
            - ≥500 characters
            - Structured with sections and bullet points
            - Specific about expected output format
            - Free of ambiguity about what the agent should produce

            ## Justification
            - **Problem it solves**: what task is currently done poorly or not at all
            - **Frequency of use**: estimated how often agents would invoke it
            - **Risk**: potential for misuse, data leakage, or unexpected side effects
            - **Alternatives considered**: why existing skills don't cover this need

            ## IsMandatory
            Should this skill be included in every agent's toolset? Justify.

            ## Dependencies
            Any infrastructure, credentials, or external services required.

            Output as structured Markdown ready to be reviewed and approved via HITL.
            """);

        await db.SaveChangesAsync();
    }

    // ── Agent Templates ───────────────────────────────────────────────────────

    private static async Task SeedTemplatesAsync(NoxDbContext db)
    {
        // Load all existing seed templates for upsert
        var seedIds = new[] { TplRequirementsAnalyst, TplSoftwareArchitect, TplBackendDeveloper,
                               TplFrontendDeveloper, TplQaEngineer, TplTechWriter, TplDevOps };
        var existing = await db.AgentTemplates
            .Where(t => seedIds.Contains(t.Id))
            .ToListAsync();
        void Upsert(AgentTemplate desired)
        {
            var found = existing.FirstOrDefault(t => t.Id == desired.Id);
            if (found is null)
            {
                db.AgentTemplates.Add(desired);
            }
            else
            {
                found.Name                 = desired.Name;
                found.Role                 = desired.Role;
                found.Description          = desired.Description;
                found.SystemPromptTemplate = desired.SystemPromptTemplate;
                found.DefaultModel         = desired.DefaultModel;
                found.SkillGroups          = desired.SkillGroups;
                found.IsGlobal             = desired.IsGlobal;
            }
        }

        Upsert(new AgentTemplate
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

                    The Task payload (first user message) is a JSON object with these keys:
                    - github_issue_number: integer — the issue to analyze
                    - github_branch: working branch (already created or to be created)
                    - github_repo / github_pat: repository access
                    - decision + feedback (only on re-run after rejection): PO feedback to incorporate

                    Steps:
                    1. If 'decision' == 'Rejected', read the current 'docs/SRS.md', incorporate the feedback, rewrite it.
                    2. Otherwise: call github_read_issue(issue_number=<github_issue_number value>).
                    3. Analyze: functional requirements, non-functional requirements, constraints, acceptance criteria.
                    4. Write the complete SRS to 'docs/SRS.md' using github_write_file.
                       Commit message: "docs: add/update Software Requirements Specification"
                    5. Return a concise summary of the key requirements.
                    """
            });

        Upsert(new AgentTemplate
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

                    The Task payload contains: github_branch, github_repo, github_pat,
                    output_requirements (summary from the analyst), and optionally decision+feedback on re-run.

                    Steps:
                    1. If 'decision' == 'Rejected', read existing docs and revise incorporating feedback.
                    2. Read 'docs/SRS.md' using github_read_file.
                    3. Use github_list_files("") to explore the repository root and understand the tech stack.
                    4. Design: components, data models, API contracts, technology choices aligned with existing stack.
                    5. Write 'docs/ARCHITECTURE.md'. Commit: "docs: add Architecture Document"
                    6. Write 'docs/IMPLEMENTATION_PLAN.md' with numbered tasks per agent role (backend, frontend).
                       Commit: "docs: add Implementation Plan"
                    7. Return a concise architecture summary.
                    """
            });

        Upsert(new AgentTemplate
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

                    The Task payload contains: github_branch, github_repo, github_pat,
                    output_architect (architecture summary), and optionally decision+feedback on re-run.

                    Steps:
                    1. If 'decision' == 'Rejected', read changed files and fix them incorporating feedback.
                    2. Read 'docs/ARCHITECTURE.md' and 'docs/IMPLEMENTATION_PLAN.md'.
                    3. Use github_list_files to explore the existing source structure.
                    4. Implement ONLY backend tasks from the plan, one file at a time using github_write_file.
                       Commit message per file: "feat: <short description>"
                    5. Return a bullet list of files created/modified.

                    Do not implement frontend code or tests. Follow the patterns present in the existing codebase.
                    """
            });

        Upsert(new AgentTemplate
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

                    The Task payload contains: github_branch, github_repo, github_pat,
                    output_backend (backend summary), output_architect (architecture summary).

                    Steps:
                    1. If 'decision' == 'Rejected', read changed files and fix incorporating feedback.
                    2. Read 'docs/ARCHITECTURE.md' and 'docs/IMPLEMENTATION_PLAN.md'.
                    3. Use github_list_files to find the frontend source directory.
                    4. Implement ONLY frontend tasks from the plan, one file at a time.
                       Commit message per file: "feat: <short description>"
                    5. Return a bullet list of files created/modified.

                    Do not implement backend code or tests. Follow existing UI conventions.
                    """
            });

        Upsert(new AgentTemplate
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

                    The Task payload contains: github_branch, github_repo, github_pat,
                    output_backend and output_frontend (implementation summaries).

                    Steps:
                    1. Read 'docs/IMPLEMENTATION_PLAN.md'.
                    2. Use github_list_files to locate the implemented files from the summaries.
                    3. Read each implemented file to understand the logic.
                    4. Write unit tests and at least one integration test per feature.
                       Place tests in the same conventions as the existing project (e.g., *.Tests project).
                       Commit: "test: add tests for <feature>"
                    5. Return a summary: files tested, scenarios covered, edge cases.
                    """
            });

        Upsert(new AgentTemplate
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

                    The Task payload contains: github_branch, github_repo, github_pat,
                    output_requirements, output_architect, output_backend, output_frontend, output_qa.

                    Steps:
                    1. Try to read existing 'README.md' (github_read_file); ignore error if not found.
                    2. Read 'docs/ARCHITECTURE.md'.
                    3. Write/update 'README.md': project overview, setup instructions, new features, usage examples.
                       Commit: "docs: update README"
                    4. Write/update 'docs/CHANGELOG.md' with a dated entry summarising this sprint's changes.
                       Commit: "docs: update CHANGELOG"
                    5. Return a brief summary of documentation updates.
                    """
            });

        Upsert(new AgentTemplate
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
                    Your task is to create a Pull Request summarising all sprint changes.

                    The Task payload contains: github_branch, github_repo, github_pat, github_base_branch,
                    output_requirements (analyst summary), output_architect (architecture summary),
                    output_backend (backend changes), output_frontend (frontend changes),
                    output_qa (test coverage summary), output_techwriter (docs summary).
                    On re-run after rejection: decision='Rejected', feedback with reviewer notes.

                    Steps:
                    1. If 'decision' == 'Rejected', close the previous PR (if any) and create a new one
                       incorporating the feedback into the PR description.
                    2. Build PR title from output_requirements (first line / feature name).
                    3. Build PR body (markdown) using all output_* summaries:
                       - ## Summary (from output_requirements + output_architect)
                       - ## Backend changes (from output_backend)
                       - ## Frontend changes (from output_frontend)
                       - ## Tests (from output_qa)
                       - ## Documentation (from output_techwriter)
                       - ## How to test (practical steps)
                    4. Call github_create_pr(title=<title>, body=<body>,
                       base_branch=<github_base_branch or "main">).
                    5. Return the PR URL and number.
                    """
            });

        await db.SaveChangesAsync();
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
