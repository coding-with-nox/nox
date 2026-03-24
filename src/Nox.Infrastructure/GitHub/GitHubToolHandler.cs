using Microsoft.Extensions.AI;
using Nox.Domain.Skills;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.GitHub;

public class GitHubToolHandler(IHttpClientFactory httpClientFactory)
{
    private const string BaseUrl = "https://api.github.com";

    /// <summary>
    /// Creates a typed AITool for a GitHub skill, capturing PAT and repo from flow variables.
    /// Returns null if required variables are missing.
    /// </summary>
    public AITool? CreateAITool(Skill skill, JsonObject flowVariables)
    {
        var pat  = flowVariables["github_pat"]?.ToString();
        var repo = flowVariables["github_repo"]?.ToString();
        var branch = flowVariables["github_branch"]?.ToString() ?? "main";

        if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(repo))
            return null;

        return skill.Slug switch
        {
            "github-read-issue" => AIFunctionFactory.Create(
                async (int issue_number) => await ReadIssueAsync(repo, issue_number, pat),
                name: "github_read_issue",
                description:
                    "Reads a GitHub issue by number from the current repository. " +
                    "Returns: title, body (markdown), state (open/closed), label names. " +
                    "Use this as the first step to understand what needs to be built. " +
                    $"Repository: {repo}."),

            "github-create-branch" => AIFunctionFactory.Create(
                async (string branch_name, string base_branch = "main") =>
                    await CreateBranchAsync(repo, branch_name, base_branch, pat),
                name: "github_create_branch",
                description:
                    "Creates a new git branch in the repository. " +
                    "branch_name: name for the new branch (e.g. 'ai/issue-42-auth'). " +
                    "base_branch: existing branch to fork from (default: 'main'). " +
                    "Returns confirmation with the base commit SHA. " +
                    "Call this before writing any files."),

            "github-read-file" => AIFunctionFactory.Create(
                async (string path) => await ReadFileAsync(repo, path, branch, pat),
                name: "github_read_file",
                description:
                    $"Reads the raw text content of a file from branch '{branch}'. " +
                    "path: relative path from repo root (e.g. 'src/api/Program.cs'). " +
                    "If path points to a directory, returns a file/directory listing instead. " +
                    "Content is truncated at 8000 chars if the file is large."),

            "github-write-file" => AIFunctionFactory.Create(
                async (string path, string content, string commit_message) =>
                    await WriteFileAsync(repo, path, content, commit_message, branch, pat),
                name: "github_write_file",
                description:
                    $"Creates or overwrites a file on branch '{branch}'. " +
                    "path: full relative path including filename and extension. " +
                    "content: complete file content as plain text (will be base64-encoded). " +
                    "commit_message: short imperative sentence (e.g. 'feat: add UserService'). " +
                    "If file already exists it is updated; otherwise created."),

            "github-list-files" => AIFunctionFactory.Create(
                async (string path = "") => await ListFilesAsync(repo, path, branch, pat),
                name: "github_list_files",
                description:
                    $"Lists files and subdirectories at a given path on branch '{branch}'. " +
                    "path: relative directory path, or empty string for the repository root. " +
                    "Returns lines in format '[file] name' or '[dir] name'. " +
                    "Use to explore the codebase structure before reading or writing files."),

            "github-create-pr" => AIFunctionFactory.Create(
                async (string title, string body, string base_branch = "main") =>
                    await CreatePrAsync(repo, title, body, branch, base_branch, pat),
                name: "github_create_pr",
                description:
                    $"Opens a Pull Request from the working branch '{branch}' into base_branch. " +
                    "title: concise PR title (imperative, ≤72 chars). " +
                    "body: markdown PR description — include: summary of changes, how to test, " +
                    "breaking changes if any. " +
                    "base_branch: target branch (default 'main', use github_base_branch variable). " +
                    "Returns the PR URL and number."),

            _ => null
        };
    }

    private HttpClient CreateClient(string pat)
    {
        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NoxAgent/1.0");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private async Task<string> ReadIssueAsync(string repo, int issueNumber, string pat)
    {
        var client = CreateClient(pat);
        var response = await client.GetAsync($"{BaseUrl}/repos/{repo}/issues/{issueNumber}");
        if (!response.IsSuccessStatusCode)
            return $"Error reading issue: HTTP {(int)response.StatusCode}";

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var title  = doc.GetProperty("title").GetString();
        var body   = doc.GetProperty("body").GetString() ?? "(no body)";
        var state  = doc.GetProperty("state").GetString();
        var labels = doc.GetProperty("labels").EnumerateArray()
                        .Select(l => l.GetProperty("name").GetString())
                        .Where(n => n is not null)
                        .ToList();

        return $"Issue #{issueNumber} [{state}]: {title}\nLabels: {string.Join(", ", labels)}\n\n{body}";
    }

    private async Task<string> CreateBranchAsync(string repo, string branchName, string baseBranch, string pat)
    {
        var client = CreateClient(pat);

        var refResp = await client.GetAsync($"{BaseUrl}/repos/{repo}/git/ref/heads/{baseBranch}");
        if (!refResp.IsSuccessStatusCode)
            return $"Error getting base branch '{baseBranch}': HTTP {(int)refResp.StatusCode}";

        var sha = JsonDocument.Parse(await refResp.Content.ReadAsStringAsync())
                              .RootElement.GetProperty("object").GetProperty("sha").GetString();

        var payload = new { @ref = $"refs/heads/{branchName}", sha };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var createResp = await client.PostAsync($"{BaseUrl}/repos/{repo}/git/refs", body);

        if (!createResp.IsSuccessStatusCode)
        {
            var err = await createResp.Content.ReadAsStringAsync();
            return $"Error creating branch: HTTP {(int)createResp.StatusCode} — {err}";
        }

        return $"Branch '{branchName}' created from '{baseBranch}' (sha: {sha?[..7]}).";
    }

    private async Task<string> ReadFileAsync(string repo, string path, string branch, string pat)
    {
        var client = CreateClient(pat);
        var response = await client.GetAsync($"{BaseUrl}/repos/{repo}/contents/{path}?ref={branch}");
        if (!response.IsSuccessStatusCode)
            return $"File not found: {path} on branch {branch}";

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        if (root.ValueKind == JsonValueKind.Array)
            return string.Join("\n", root.EnumerateArray()
                .Select(e => $"[{e.GetProperty("type").GetString()}] {e.GetProperty("name").GetString()}"));

        var base64  = root.GetProperty("content").GetString()?.Replace("\n", "") ?? "";
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        return decoded.Length > 8_000 ? decoded[..8_000] + "\n...[truncated]" : decoded;
    }

    private async Task<string> WriteFileAsync(
        string repo, string path, string content, string message, string branch, string pat)
    {
        var client = CreateClient(pat);

        string? existingSha = null;
        var checkResp = await client.GetAsync($"{BaseUrl}/repos/{repo}/contents/{path}?ref={branch}");
        if (checkResp.IsSuccessStatusCode)
        {
            var existing = JsonDocument.Parse(await checkResp.Content.ReadAsStringAsync()).RootElement;
            existingSha = existing.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() : null;
        }

        var encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var payload = existingSha is not null
            ? (object)new { message, content = encodedContent, branch, sha = existingSha }
            : new { message, content = encodedContent, branch };

        var reqBody = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{BaseUrl}/repos/{repo}/contents/{path}", reqBody);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            return $"Error writing file: HTTP {(int)response.StatusCode} — {err}";
        }

        return $"File '{path}' written to branch '{branch}'.";
    }

    private async Task<string> ListFilesAsync(string repo, string path, string branch, string pat)
    {
        var client = CreateClient(pat);
        var url = string.IsNullOrEmpty(path)
            ? $"{BaseUrl}/repos/{repo}/contents?ref={branch}"
            : $"{BaseUrl}/repos/{repo}/contents/{path}?ref={branch}";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return $"Error listing files at '{path}': HTTP {(int)response.StatusCode}";

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                                .RootElement.EnumerateArray()
                                .Select(e => $"[{e.GetProperty("type").GetString()}] {e.GetProperty("name").GetString()}")
                                .ToList();

        return items.Count == 0 ? "(empty directory)" : string.Join("\n", items);
    }

    private async Task<string> CreatePrAsync(
        string repo, string title, string body, string headBranch, string baseBranch, string pat)
    {
        var client = CreateClient(pat);
        var payload = new { title, body, head = headBranch, @base = baseBranch };
        var reqBody = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{BaseUrl}/repos/{repo}/pulls", reqBody);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            return $"Error creating PR: HTTP {(int)response.StatusCode} — {err}";
        }

        var doc    = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var prUrl  = doc.GetProperty("html_url").GetString();
        var prNum  = doc.GetProperty("number").GetInt32();
        return $"PR #{prNum} created: {prUrl}";
    }
}
